using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Indeo;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Intel Indeo Video Interactive 4 (<c>IV41</c>) as self-contained intra pictures.
/// </summary>
/// <remarks>
/// This is deliberately a small encoder rather than a second Indeo implementation. The bitstream
/// shape is derived from <see cref="Indeo4Decoder"/> and the shared IVI layer already in this package:
/// one band per plane, YVU9 chrominance, 256-sample tiles, the direct 8x8 transform for luminance and
/// the 4x4 Haar transform for chrominance. Every macroblock is coded and every packet is an intra
/// picture, so there is no invented motion search, bidirectional scheduling or rate-control policy.
/// <para/>
/// The encoder uses a custom fixed six-bit Huffman book. That is enough because block coefficients
/// are always sent through the format's escape symbol: the remaining symbols are the zero run, two
/// six-bit halves of the signed value and end-of-block. It is intentionally less compact than Intel's
/// encoder, but it uses only syntax the format itself defines and makes malformed size arithmetic much
/// easier to audit.
/// <para/>
/// Luminance is exact once RGB has been converted to the codec's YVU9 sample space: transform 3 stores
/// each 8x8 block's samples directly. Chroma uses the inverse of Indeo's integer Haar transform; its
/// two pre-scaled coefficient rows require division by two on encode and can therefore round by one.
/// The format is chroma-subsampled by construction, so RGB round trips are lossy even where the coded
/// planes themselves are exact.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Indeo4VideoEncoder : IVideoCodecEncoder<Indeo4VideoEncoder> {

  private static readonly CodecTag _TAG = CodecTag.FromCharacters("IV41");

  private const uint _PICTURE_START_CODE = 0x3FFF8;
  private const int _PICTURE_SIZE_ESCAPE = 7;
  private const int _TILE_FACTOR = 7; // (7 + 1) * 32 = 256 luminance samples.
  private const int _TILE_SIZE = 256;

  private const int _LUMA_MACROBLOCK = 16;
  private const int _LUMA_BLOCK = 8;
  private const int _CHROMA_MACROBLOCK = 4;
  private const int _CHROMA_BLOCK = 4;

  private const int _BLOCK_ESCAPE = 11;
  private const int _BLOCK_END = 4;
  private const int _HUFFMAN_BITS = 6;
  private const int _LONG_TILE_SIZE = 0xFFFFFF;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private MediaStreamInfo? _stream;

  private readonly record struct _Planes(byte[] Luma, byte[] ChromaBlue, byte[] ChromaRed, int ChromaWidth, int ChromaHeight);

  private Indeo4VideoEncoder(MediaStreamInfo stream) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
  }

  public static string CodecName => "Intel Indeo Video Interactive 4";

  public static CodecTag Codec => _TAG;

  public static Indeo4VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Intel Indeo Video Interactive 4 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An Indeo 4 encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");
    if (stream.Width > ushort.MaxValue || stream.Height > ushort.MaxValue)
      throw new NotSupportedException(
        $"Indeo 4 states an escaped picture size in sixteen bits per dimension; {stream.Width}x{stream.Height} does not fit.");

    var rgbBytes = (long)stream.Width * stream.Height * 3;
    if (rgbBytes > int.MaxValue)
      throw new NotSupportedException(
        $"A {stream.Width}x{stream.Height} picture needs {rgbBytes} RGB bytes before it can be converted to YVU9, "
        + $"which is more than one managed array can hold ({int.MaxValue}).");

    return new(stream);
  }

  /// <summary>Encodes one self-contained intra picture.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"Indeo 4 geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var planes = this._ToYvu9(frame);
    var writer = new _BitWriter();

    this._WritePictureHeader(writer);
    _WriteBand(writer, plane: 0, planes.Luma, this._width, this._height, _TILE_SIZE, _TILE_SIZE,
      macroblockSize: _LUMA_MACROBLOCK, blockSize: _LUMA_BLOCK, geometry: 0, transform: 3,
      scan: IviTables.ZigzagDirect, scanIndex: 0, haar: false);

    var chromaTile = (_TILE_SIZE + 3) >> 2;
    _WriteBand(writer, plane: 1, planes.ChromaRed, planes.ChromaWidth, planes.ChromaHeight, chromaTile, chromaTile,
      macroblockSize: _CHROMA_MACROBLOCK, blockSize: _CHROMA_BLOCK, geometry: 2, transform: 10,
      scan: IviTables.DirectScan4x4, scanIndex: 5, haar: true);
    _WriteBand(writer, plane: 2, planes.ChromaBlue, planes.ChromaWidth, planes.ChromaHeight, chromaTile, chromaTile,
      macroblockSize: _CHROMA_MACROBLOCK, blockSize: _CHROMA_BLOCK, geometry: 2, transform: 10,
      scan: IviTables.DirectScan4x4, scanIndex: 5, haar: true);

    packet = new(
      this._requested.Index,
      writer.ToArray(),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
    return true;
  }

  /// <summary>Describes an <c>IV41</c> VFW stream suitable for AVI or Matroska.</summary>
  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;

    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize,
      Width: this._width,
      Height: this._height,
      Planes: 1,
      BitsPerPixel: 24,
      Compression: unchecked((int)_TAG.Value),
      ImageSize: 0,
      XPixelsPerMeter: 0,
      YPixelsPerMeter: 0,
      ColorsUsed: 0,
      ImportantColors: 0);
    var format = new byte[BitmapInfoHeader.StructSize];
    header.WriteTo(format);

    return this._stream = new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _TAG,
      Handler = _TAG,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      DeclaredFrameCount = this._requested.DeclaredFrameCount,
      Width = this._width,
      Height = this._height,
      BitsPerPixel = 24,
      CodecPrivateData = format,
      Language = this._requested.Language,
      Name = this._requested.Name,
    };
  }

  // ============================================================================================
  // Picture header
  // ============================================================================================

  private void _WritePictureHeader(_BitWriter writer) {
    writer.Write(_PICTURE_START_CODE, 18);
    writer.Write(Indeo4Decoder.FrameTypeIntra, 3);
    writer.WriteFlag(false); // Transparency.
    writer.WriteFlag(false); // Reserved.
    writer.WriteFlag(false); // Picture-data size absent; bands and tiles state their own sizes.
    writer.WriteFlag(false); // No password lock word.

    writer.Write(_PICTURE_SIZE_ESCAPE, 3);
    writer.Write(this._height, 16);
    writer.Write(this._width, 16);

    writer.WriteFlag(true);
    writer.Write(_TILE_FACTOR, 4); // Height first.
    writer.Write(_TILE_FACTOR, 4); // Width.

    writer.Write(0, 2); // YVU9 / 4:1:0.
    writer.Write(3, 2); // One luminance band.
    writer.Write(3, 2); // One chrominance band.

    writer.WriteFlag(false); // Frame number.
    writer.WriteFlag(false); // Decode-time estimate.
    _WriteSixBitCodebook(writer);
    _WriteSixBitCodebook(writer);
    writer.WriteFlag(false); // Picture-wide run/value selector; every band selects its own.
    writer.WriteFlag(false); // Not interleaved.
    writer.WriteFlag(false); // Quantiser delta is not forced onto uncoded macroblocks.
    writer.Write(0, 5);      // Picture quantiser; every band restates it.
    writer.WriteFlag(false); // Optional three-bit field absent.
    writer.WriteFlag(false); // Checksum absent.
    writer.WriteFlag(false); // End of header extensions.
    writer.WriteFlag(false); // No known-bad blocks.
    writer.Align();
  }

  /// <summary>
  /// Writes a custom codebook descriptor containing one row of 64 fixed six-bit symbols.
  /// </summary>
  private static void _WriteSixBitCodebook(_BitWriter writer) {
    writer.WriteFlag(true); // A codebook is explicitly selected.
    writer.Write(7, 3);     // Selector seven means a custom descriptor follows.
    writer.Write(1, 4);     // One descriptor row.
    writer.Write(_HUFFMAN_BITS, 4);
  }

  // ============================================================================================
  // Bands and tiles
  // ============================================================================================

  private static void _WriteBand(
    _BitWriter writer,
    int plane,
    byte[] samples,
    int width,
    int height,
    int tileWidth,
    int tileHeight,
    int macroblockSize,
    int blockSize,
    int geometry,
    int transform,
    byte[] scan,
    int scanIndex,
    bool haar) {

    writer.Write(plane, 2);
    writer.Write(0, 4);     // One band per plane, so every band is number zero.
    writer.WriteFlag(false); // The band is not empty.
    writer.WriteFlag(false); // Band-header size omitted.
    writer.Write(0, 2);      // Whole-sample motion vectors (unused in an intra picture).
    writer.WriteFlag(false); // Checksum absent.
    writer.Write(geometry, 2);
    writer.WriteFlag(false); // Do not inherit motion vectors.
    writer.WriteFlag(false); // Do not inherit quantiser deltas.
    writer.Write(0, 5);      // Quantiser zero: the values below are not rescaled.
    writer.WriteFlag(false); // State the transform rather than inherit it.
    writer.Write(transform, 5);
    writer.Write(scanIndex, 4);
    writer.Write(0, 5);      // The first dequantisation matrix; q=0 makes it inert.
    writer.WriteFlag(false); // Use the picture's block codebook.
    writer.WriteFlag(false); // Use run/value map 8, the implicit default.
    writer.WriteFlag(false); // No run/value corrections.
    writer.Align();

    var pitchAlignment = plane == 0 ? 16 : 8;
    var pitch = _Align(width, pitchAlignment);
    var alignedHeight = _Align(height, pitchAlignment);
    var residuals = new short[pitch * alignedHeight];
    for (var y = 0; y < height; ++y) {
      var source = y * width;
      var target = y * pitch;
      for (var x = 0; x < width; ++x)
        residuals[target + x] = (short)(samples[source + x] - 128);
    }

    for (var tileY = 0; tileY < height; tileY += tileHeight)
      for (var tileX = 0; tileX < width; tileX += tileWidth) {
        var actualWidth = Math.Min(tileWidth, width - tileX);
        var actualHeight = Math.Min(tileHeight, height - tileY);
        var tile = _WriteTile(
          residuals, pitch, tileX, tileY, actualWidth, actualHeight,
          macroblockSize, blockSize, scan, haar);
        writer.WriteBytes(tile);
      }
  }

  private static byte[] _WriteTile(
    short[] residuals,
    int pitch,
    int tileX,
    int tileY,
    int tileWidth,
    int tileHeight,
    int macroblockSize,
    int blockSize,
    byte[] scan,
    bool haar) {

    var blocksPerMacroblock = macroblockSize == blockSize ? 1 : 4;
    var macroblockHeaders = new _BitWriter();
    var blockData = new _BitWriter();
    var previousDc = 0;
    Span<int> coefficients = stackalloc int[64];

    for (var y = tileY; y < tileY + tileHeight; y += macroblockSize)
      for (var x = tileX; x < tileX + tileWidth; x += macroblockSize) {
        macroblockHeaders.WriteFlag(false); // Intra macroblocks may not repeat a reference picture.
        macroblockHeaders.Write((1 << blocksPerMacroblock) - 1, blocksPerMacroblock);
        _WriteSymbol(macroblockHeaders, 0); // Quantiser delta zero.

        for (var block = 0; block < blocksPerMacroblock; ++block) {
          var blockX = x + ((block & 1) != 0 ? blockSize : 0);
          var blockY = y + ((block & 2) != 0 ? blockSize : 0);
          coefficients.Clear();

          if (haar)
            _ForwardHaar4x4(residuals, pitch, blockX, blockY, coefficients);
          else
            _Direct8x8(residuals, pitch, blockX, blockY, coefficients);

          var dc = coefficients[0];
          coefficients[0] = dc - previousDc;
          previousDc = dc;
          _WriteBlock(blockData, coefficients, blockSize * blockSize, scan);
        }
      }

    macroblockHeaders.Align();
    blockData.Align();
    var headers = macroblockHeaders.ToArray();
    var blocks = blockData.ToArray();
    var payloadLength = checked(headers.Length + blocks.Length);

    var shortLength = payloadLength + 2;
    var longLength = payloadLength + 5;
    var useShortLength = shortLength < 255;
    var tileLength = useShortLength ? shortLength : longLength;
    if (tileLength > _LONG_TILE_SIZE)
      throw new InvalidDataException(
        $"An Indeo 4 tile needs {tileLength} bytes and its extended size field reaches {_LONG_TILE_SIZE}.");

    var writer = new _BitWriter(tileLength);
    writer.WriteFlag(false); // Tile is not empty.
    writer.WriteFlag(true);  // Tile data size follows.
    if (useShortLength)
      writer.Write(tileLength, 8);
    else {
      writer.Write(255, 8);
      writer.Write(tileLength, 24);
    }

    writer.Align();
    writer.WriteBytes(headers);
    writer.WriteBytes(blocks);

    var result = writer.ToArray();
    if (result.Length != tileLength)
      throw new InvalidDataException(
        $"The Indeo 4 tile writer produced {result.Length} bytes after stating {tileLength}.");

    return result;
  }

  // ============================================================================================
  // Blocks
  // ============================================================================================

  private static void _Direct8x8(short[] residuals, int pitch, int x, int y, Span<int> coefficients) {
    for (var row = 0; row < 8; ++row) {
      var source = (y + row) * pitch + x;
      for (var column = 0; column < 8; ++column)
        coefficients[row * 8 + column] = residuals[source + column];
    }
  }

  /// <summary>Inverts <see cref="IviTransforms.InverseHaar4x4"/> for one chrominance block.</summary>
  private static void _ForwardHaar4x4(short[] residuals, int pitch, int x, int y, Span<int> coefficients) {
    Span<int> intermediate = stackalloc int[16];
    Span<int> input = stackalloc int[4];
    Span<int> transformed = stackalloc int[4];

    for (var row = 0; row < 4; ++row) {
      var at = (y + row) * pitch + x;
      for (var column = 0; column < 4; ++column)
        input[column] = residuals[at + column];

      _ForwardHaar4(input, transformed);
      transformed.CopyTo(intermediate.Slice(row * 4, 4));
    }

    for (var column = 0; column < 4; ++column) {
      for (var row = 0; row < 4; ++row)
        input[row] = intermediate[row * 4 + column];

      _ForwardHaar4(input, transformed);
      var scale = (column & 2) == 0;
      coefficients[column] = scale ? _HalfRounded(transformed[0]) : transformed[0];
      coefficients[4 + column] = scale ? _HalfRounded(transformed[1]) : transformed[1];
      coefficients[8 + column] = transformed[2];
      coefficients[12 + column] = transformed[3];
    }
  }

  /// <summary>
  /// Exact inverse of the four-point integer Haar used by the decoder before the IV4 coefficient
  /// pre-scaling is applied.
  /// </summary>
  private static void _ForwardHaar4(ReadOnlySpan<int> samples, Span<int> coefficients) {
    var low = samples[0] + samples[1];
    var high = samples[2] + samples[3];
    coefficients[0] = low + high;
    coefficients[1] = low - high;
    coefficients[2] = samples[0] - samples[1];
    coefficients[3] = samples[2] - samples[3];
  }

  private static int _HalfRounded(int value)
    => value >= 0 ? (value + 1) >> 1 : -((-value + 1) >> 1);

  private static void _WriteBlock(_BitWriter writer, ReadOnlySpan<int> coefficients, int coefficientCount, byte[] scan) {
    var previous = -1;

    for (var position = 0; position < coefficientCount; ++position) {
      var value = coefficients[scan[position]];
      if (value == 0)
        continue;

      var run = position - previous;
      var signed = value > 0 ? (value << 1) - 1 : (-value) << 1;
      if (run is < 1 or > 64 || signed > 0xFFF)
        throw new InvalidDataException(
          $"An Indeo 4 escaped coefficient needs run {run} and signed value code {signed}; the fixed six-bit "
          + "writer can state runs through 64 and values through 4095.");

      _WriteSymbol(writer, _BLOCK_ESCAPE);
      _WriteSymbol(writer, run - 1);
      _WriteSymbol(writer, signed & 0x3F);
      _WriteSymbol(writer, signed >> 6);
      previous = position;
    }

    _WriteSymbol(writer, _BLOCK_END);
  }

  /// <summary>
  /// Writes one symbol from the custom [6] descriptor. Huffman codes are canonical MSB-first while
  /// IV4's fields are LSB-first, so the six physical bits are the symbol number reversed.
  /// </summary>
  private static void _WriteSymbol(_BitWriter writer, int symbol)
    => writer.Write(_ReverseSix(symbol), _HUFFMAN_BITS);

  private static int _ReverseSix(int value) {
    var reversed = 0;
    for (var i = 0; i < _HUFFMAN_BITS; ++i)
      reversed |= ((value >> i) & 1) << (_HUFFMAN_BITS - 1 - i);

    return reversed;
  }

  // ============================================================================================
  // RGB -> YVU9
  // ============================================================================================

  private _Planes _ToYvu9(RawImage frame) {
    var rgb = frame.ToRgb24();
    var luma = new byte[this._width * this._height];
    var chromaWidth = (this._width + 3) >> 2;
    var chromaHeight = (this._height + 3) >> 2;
    var chromaBlue = new byte[chromaWidth * chromaHeight];
    var chromaRed = new byte[chromaBlue.Length];
    var blueTotals = new int[chromaBlue.Length];
    var redTotals = new int[chromaBlue.Length];
    var counts = new byte[chromaBlue.Length];

    for (var y = 0; y < this._height; ++y) {
      var source = y * this._width * 3;
      var target = y * this._width;
      var chromaRow = (y >> 2) * chromaWidth;

      for (var x = 0; x < this._width; ++x) {
        var r = rgb[source];
        var g = rgb[source + 1];
        var b = rgb[source + 2];
        source += 3;

        luma[target + x] = _ClampByte(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);
        var chromaAt = chromaRow + (x >> 2);
        blueTotals[chromaAt] += ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
        redTotals[chromaAt] += ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;
        ++counts[chromaAt];
      }
    }

    for (var i = 0; i < chromaBlue.Length; ++i) {
      var count = counts[i];
      chromaBlue[i] = _ClampByte((blueTotals[i] + count / 2) / count);
      chromaRed[i] = _ClampByte((redTotals[i] + count / 2) / count);
    }

    return new(luma, chromaBlue, chromaRed, chromaWidth, chromaHeight);
  }

  private static byte _ClampByte(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

  private static int _Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

  // ============================================================================================
  // Little-endian bit output
  // ============================================================================================

  /// <summary>Indeo's least-significant-bit-first field writer.</summary>
  private sealed class _BitWriter {

    private byte[] _buffer;
    private int _position;

    internal _BitWriter(int capacity = 256) => this._buffer = new byte[Math.Max(capacity, 1)];

    internal void WriteFlag(bool value) => this.Write(value ? 1 : 0, 1);

    internal void Write(int value, int count) => this.Write(unchecked((uint)value), count);

    internal void Write(uint value, int count) {
      if (count is < 0 or > 32)
        throw new ArgumentOutOfRangeException(nameof(count));

      this._Ensure(this._position + count);
      for (var bit = 0; bit < count; ++bit, ++this._position)
        if (((value >> bit) & 1) != 0)
          this._buffer[this._position >> 3] |= (byte)(1 << (this._position & 7));
    }

    internal void Align() {
      this._position = (this._position + 7) & ~7;
      this._Ensure(this._position);
    }

    internal void WriteBytes(ReadOnlySpan<byte> bytes) {
      if ((this._position & 7) != 0)
        throw new InvalidOperationException("Whole bytes can only be appended to an aligned Indeo bitstream.");

      this._Ensure(this._position + bytes.Length * 8);
      bytes.CopyTo(this._buffer.AsSpan(this._position >> 3));
      this._position += bytes.Length * 8;
    }

    internal byte[] ToArray() {
      var length = (this._position + 7) >> 3;
      return this._buffer.AsSpan(0, length).ToArray();
    }

    private void _Ensure(int bits) {
      var bytes = (bits + 7) >> 3;
      if (bytes <= this._buffer.Length)
        return;

      var size = this._buffer.Length;
      while (size < bytes)
        size = checked(size * 2);

      Array.Resize(ref this._buffer, size);
    }
  }
}
