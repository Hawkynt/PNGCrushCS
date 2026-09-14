using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Indeo;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes Intel Indeo Video Interactive 5 (<c>IV50</c>) with intra pictures followed by
/// forward-predicted pictures.
/// </summary>
/// <remarks>
/// Indeo 5 has five picture types: intra, reference P, scalable droppable P, droppable P and null.
/// It has no bidirectional picture type. This writer uses the ordinary non-scalable profile: the first
/// packet is an intra picture and later packets are reference P-pictures with zero-motion prediction
/// from the preceding reconstructed picture. Macroblocks whose residual is empty use the format's
/// repeat syntax; the rest carry transformed residuals. The decoder therefore sees the same reference
/// chain the encoder used rather than prediction from the unquantised source pictures.
/// <para/>
/// The stream uses one luminance band and one band per chrominance plane, YVU9 sampling and 64-sample
/// tiles. Luminance is transformed with the 8x8 slant transform and chrominance with the 4x4 slant
/// transform, exactly the transforms those band positions select in an IV50 GOP header. A fixed
/// six-bit custom Huffman descriptor keeps entropy coding deterministic: coefficients use the default
/// run/value map's escape symbol, followed by their run and signed value, while motion-vector deltas
/// are always zero.
/// <para/>
/// Intel never published an IV50 encoder specification and FFmpeg has only a decoder. The bitstream
/// syntax follows the LGPL-2.1-or-later FFmpeg decoder already represented by
/// <see cref="Indeo5Decoder"/>. The forward slant matrices below were independently derived by
/// algebraically inverting that decoder's integer slant transform; they are not copied from an
/// external encoder implementation.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Indeo5VideoEncoder : IVideoCodecEncoder<Indeo5VideoEncoder> {

  private static readonly CodecTag _TAG = CodecTag.FromCharacters("IV50");

  private const int _PICTURE_SIZE_ESCAPE = 15;
  private const int _TILE_SIZE = 64;
  private const int _LUMA_MACROBLOCK = 16;
  private const int _LUMA_BLOCK = 8;
  private const int _CHROMA_MACROBLOCK = 4;
  private const int _CHROMA_BLOCK = 4;
  private const int _HUFFMAN_BITS = 6;
  private const int _LONG_TILE_SIZE = 0xFFFFFF;
  private const int _MIN_ESCAPED_VALUE = -2047;
  private const int _MAX_ESCAPED_VALUE = 2048;

  private static readonly int _BLOCK_ESCAPE = IviRunValueMap.Defaults[8].EscapeSymbol;
  private static readonly int _BLOCK_END = IviRunValueMap.Defaults[8].EndOfBlockSymbol;

  // A^-1 for the ideal (unrounded) eight-point slant kernel used by IviTransforms._Slant8.
  // Rows are expressed as exact rational values where that keeps the derivation visible.
  private static readonly double[] _FORWARD_SLANT8 = [
    1d / 8, 1d / 8, 1d / 8, 1d / 8, 1d / 8, 1d / 8, 1d / 8, 1d / 8,
    363d / 1885, 267d / 1885, 139d / 1885, 43d / 1885, -43d / 1885, -139d / 1885, -267d / 1885, -363d / 1885,
    5d / 29, 2d / 29, -2d / 29, -5d / 29, -5d / 29, -2d / 29, 2d / 29, 5d / 29,
    164d / 1885, -4d / 1885, -228d / 1885, -396d / 1885, 396d / 1885, 228d / 1885, 4d / 1885, -164d / 1885,
    1d / 8, -1d / 8, -1d / 8, 1d / 8, 1d / 8, -1d / 8, -1d / 8, 1d / 8,
    1d / 8, -1d / 8, -1d / 8, 1d / 8, -1d / 8, 1d / 8, 1d / 8, -1d / 8,
    -2d / 29, 5d / 29, -5d / 29, 2d / 29, 2d / 29, -5d / 29, 5d / 29, -2d / 29,
    2d / 29, -5d / 29, 5d / 29, -2d / 29, 2d / 29, -5d / 29, 5d / 29, -2d / 29,
  ];

  // A^-1 for the corresponding four-point slant kernel.
  private static readonly double[] _FORWARD_SLANT4 = [
    1d / 4, 1d / 4, 1d / 4, 1d / 4,
    10d / 29, 4d / 29, -4d / 29, -10d / 29,
    1d / 4, -1d / 4, -1d / 4, 1d / 4,
    4d / 29, -10d / 29, 10d / 29, -4d / 29,
  ];

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly Indeo5Decoder _reconstructionDecoder;
  private MediaStreamInfo? _stream;
  private IviPicture? _reference;
  private byte _frameNumber;

  private readonly record struct _Planes(byte[] Luma, byte[] ChromaBlue, byte[] ChromaRed, int ChromaWidth, int ChromaHeight);

  private Indeo5VideoEncoder(MediaStreamInfo stream) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._reconstructionDecoder = new(this._width, this._height);
  }

  public static string CodecName => "Intel Indeo Video Interactive 5";

  public static CodecTag Codec => _TAG;

  public static Indeo5VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Intel Indeo Video Interactive 5 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An Indeo 5 encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");
    if (stream.Width > 0x1FFF || stream.Height > 0x1FFF)
      throw new NotSupportedException(
        $"Indeo 5 states an escaped picture size in thirteen bits per dimension; {stream.Width}x{stream.Height} does not fit.");

    var pixels = (long)stream.Width * stream.Height;
    if (pixels > 1 << 26)
      throw new NotSupportedException(
        $"A {stream.Width}x{stream.Height} Indeo 5 picture has {pixels} luminance samples; this implementation limits a picture to {1 << 26}.");
    if (pixels * 3 > int.MaxValue)
      throw new NotSupportedException(
        $"A {stream.Width}x{stream.Height} picture needs {pixels * 3} RGB bytes, which is more than one managed array can hold ({int.MaxValue}).");

    return new(stream);
  }

  /// <summary>
  /// Encodes one IV50 packet. The first packet is intra; later packets are reference P-pictures.
  /// </summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"Indeo 5 geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var planes = this._ToYvu9(frame);
    var isIntra = this._reference == null;
    var writer = new _BitWriter();

    this._WritePictureHeader(writer, isIntra);

    _WriteBand(
      writer,
      planes.Luma,
      isIntra ? null : this._reference!.Luma,
      this._width,
      this._height,
      _TILE_SIZE,
      _TILE_SIZE,
      _LUMA_MACROBLOCK,
      _LUMA_BLOCK,
      IviTables.ZigzagDirect,
      plane: 0,
      isIntra);

    var chromaTile = (_TILE_SIZE + 3) >> 2;
    _WriteBand(
      writer,
      planes.ChromaRed,
      isIntra ? null : this._reference!.ChromaRed,
      planes.ChromaWidth,
      planes.ChromaHeight,
      chromaTile,
      chromaTile,
      _CHROMA_MACROBLOCK,
      _CHROMA_BLOCK,
      IviTables.DirectScan4x4,
      plane: 1,
      isIntra);
    _WriteBand(
      writer,
      planes.ChromaBlue,
      isIntra ? null : this._reference!.ChromaBlue,
      planes.ChromaWidth,
      planes.ChromaHeight,
      chromaTile,
      chromaTile,
      _CHROMA_MACROBLOCK,
      _CHROMA_BLOCK,
      IviTables.DirectScan4x4,
      plane: 2,
      isIntra);

    var data = writer.ToArray();

    // Video prediction must be against the picture the decoder reconstructed, not the source picture
    // that existed before transform rounding and quantisation. Reusing the decoder here also keeps
    // encoder and decoder buffer rotation locked to the same semantics.
    this._reference = this._reconstructionDecoder.Decode(data)
      ?? throw new InvalidDataException("The Indeo 5 encoder produced a null picture while encoding a non-null frame.");

    packet = new(
      this._requested.Index,
      data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: isIntra);

    unchecked {
      ++this._frameNumber;
    }

    return true;
  }

  /// <summary>Describes an <c>IV50</c> VFW stream suitable for AVI or Matroska.</summary>
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
  // Picture and GOP headers
  // ============================================================================================

  private void _WritePictureHeader(_BitWriter writer, bool isIntra) {
    writer.Write(0x1F, 5);
    writer.Write(isIntra ? Indeo5Decoder.FrameTypeIntra : Indeo5Decoder.FrameTypeInter, 3);
    writer.Write(this._frameNumber, 8);

    if (isIntra)
      this._WriteGroupHeader(writer);

    writer.Write(0x40, 8); // Explicit macroblock Huffman descriptor; no optional size/checksum fields.
    _WriteSixBitCodebook(writer);
    writer.Write(0, 3); // Unknown/reserved bits consumed by all known decoders.
    writer.Align();
  }

  private void _WriteGroupHeader(_BitWriter writer) {
    writer.Write(0x40, 8); // Explicit tile size, YVU9, no protection, no transparency.
    writer.Write(0, 2);    // 64 << 0 = 64 luminance samples per tile.
    writer.Write(0, 2);    // One luminance band.
    writer.WriteFlag(false); // One chrominance band.

    writer.Write(_PICTURE_SIZE_ESCAPE, 4);
    writer.Write(this._height, 13);
    writer.Write(this._width, 13);

    // Luminance band zero: 16x16 macroblocks made from four 8x8 slant blocks.
    writer.WriteFlag(false); // Whole-sample motion vectors.
    writer.WriteFlag(false); // Macroblock is twice the block width.
    writer.WriteFlag(false); // 8x8 block.
    writer.WriteFlag(false); // No extended transform information.
    writer.Write(0, 2);

    // Chroma band zero: one 4x4 slant block per macroblock.
    writer.WriteFlag(false); // Whole-sample motion vectors.
    writer.WriteFlag(true);  // Macroblock and block have the same size.
    writer.WriteFlag(true);  // 4x4 block.
    writer.WriteFlag(false); // No extended transform information.
    writer.Write(0, 2);

    writer.Align();
    writer.Write(0, 23); // Unknown/reserved GOP bits in the reference decoder.
    writer.WriteFlag(false); // No GOP extension.
    writer.Align();
  }

  private static void _WriteSixBitCodebook(_BitWriter writer) {
    writer.Write(7, 3);          // Custom descriptor follows.
    writer.Write(1, 4);          // One descriptor row.
    writer.Write(_HUFFMAN_BITS, 4);
  }

  // ============================================================================================
  // Bands, tiles and macroblocks
  // ============================================================================================

  private static void _WriteBand(
    _BitWriter writer,
    byte[] samples,
    byte[]? reference,
    int width,
    int height,
    int tileWidth,
    int tileHeight,
    int macroblockSize,
    int blockSize,
    byte[] scan,
    int plane,
    bool isIntra) {

    writer.Write(0x80, 8); // Non-empty band; explicit block Huffman descriptor; default run/value map.
    _WriteSixBitCodebook(writer);
    writer.WriteFlag(false); // Checksum absent.
    writer.Write(0, 5);      // Highest-quality quantiser level.
    writer.Align();

    var alignment = plane == 0 ? 16 : 8;
    var pitch = _Align(width, alignment);
    var alignedHeight = _Align(height, alignment);
    var residuals = new short[pitch * alignedHeight];

    for (var y = 0; y < height; ++y) {
      var source = y * width;
      var target = y * pitch;
      for (var x = 0; x < width; ++x) {
        var value = samples[source + x];
        residuals[target + x] = (short)(isIntra ? value - 128 : value - reference![source + x]);
      }
    }

    for (var tileY = 0; tileY < height; tileY += tileHeight)
      for (var tileX = 0; tileX < width; tileX += tileWidth) {
        var actualWidth = Math.Min(tileWidth, width - tileX);
        var actualHeight = Math.Min(tileHeight, height - tileY);
        var tile = _WriteTile(
          residuals,
          pitch,
          tileX,
          tileY,
          actualWidth,
          actualHeight,
          macroblockSize,
          blockSize,
          scan,
          plane,
          isIntra);
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
    int plane,
    bool isIntra) {

    var blocksPerMacroblock = macroblockSize == blockSize ? 1 : 4;
    var macroblockHeaders = new _BitWriter();
    var blockData = new _BitWriter();
    var previousDc = 0;
    Span<int> coefficients = stackalloc int[4 * 64];

    for (var y = tileY; y < tileY + tileHeight; y += macroblockSize)
      for (var x = tileX; x < tileX + tileWidth; x += macroblockSize) {
        var pattern = 0;

        for (var block = 0; block < blocksPerMacroblock; ++block) {
          var blockX = x + ((block & 1) != 0 ? blockSize : 0);
          var blockY = y + ((block & 2) != 0 ? blockSize : 0);
          var target = coefficients.Slice(block * 64, 64);
          target.Clear();

          _ForwardSlant(residuals, pitch, blockX, blockY, blockSize, target);
          _QuantizeBlock(target, blockSize, plane, isIntra, ref previousDc);

          if (!_IsZero(target, blockSize * blockSize))
            pattern |= 1 << block;
        }

        if (!isIntra && pattern == 0) {
          macroblockHeaders.WriteFlag(true); // Repeat the same zero-motion rectangle from the reference.
        } else {
          macroblockHeaders.WriteFlag(false);
          if (!isIntra)
            macroblockHeaders.WriteFlag(true); // Inter macroblock.

          macroblockHeaders.Write(pattern, blocksPerMacroblock);

          if (!isIntra) {
            _WriteSymbol(macroblockHeaders, 0); // Motion-vector Y delta.
            _WriteSymbol(macroblockHeaders, 0); // Motion-vector X delta.
          }
        }

        for (var block = 0; block < blocksPerMacroblock; ++block)
          if ((pattern & (1 << block)) != 0)
            _WriteBlock(blockData, coefficients.Slice(block * 64, 64), blockSize * blockSize, scan);
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
        $"An Indeo 5 tile needs {tileLength} bytes and its extended size field reaches {_LONG_TILE_SIZE}.");

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
        $"The Indeo 5 tile writer produced {result.Length} bytes after stating {tileLength}.");

    return result;
  }

  // ============================================================================================
  // Forward transform and quantisation
  // ============================================================================================

  /// <summary>
  /// Applies the forward form of the separable IV50 slant transform.
  /// </summary>
  /// <remarks>
  /// The decoder computes <c>A * C * A^T / 2</c>, with integer rounding inside <c>A</c>. The writer
  /// therefore starts from <c>2 * A^-1 * S * A^-T</c>, rounds the coefficients once, and lets the
  /// normative integer inverse transform define the final reconstructed samples.
  /// </remarks>
  private static void _ForwardSlant(
    short[] samples,
    int pitch,
    int x,
    int y,
    int size,
    Span<int> coefficients) {

    var matrix = size == 8 ? _FORWARD_SLANT8 : _FORWARD_SLANT4;
    Span<double> intermediate = stackalloc double[64];

    for (var row = 0; row < size; ++row)
      for (var column = 0; column < size; ++column) {
        double sum = 0;
        for (var sourceRow = 0; sourceRow < size; ++sourceRow)
          sum += matrix[row * size + sourceRow]
                 * (2d * samples[(y + sourceRow) * pitch + x + column]);

        intermediate[row * size + column] = sum;
      }

    for (var row = 0; row < size; ++row)
      for (var column = 0; column < size; ++column) {
        double sum = 0;
        for (var sourceColumn = 0; sourceColumn < size; ++sourceColumn)
          sum += intermediate[row * size + sourceColumn]
                 * matrix[column * size + sourceColumn];

        coefficients[row * size + column] = _Round(sum);
      }
  }

  private static void _QuantizeBlock(Span<int> coefficients, int blockSize, int plane, bool isIntra, ref int previousDc) {
    ReadOnlySpan<ushort> basis;
    int scale;

    if (blockSize == 8) {
      basis = isIntra ? Indeo5Tables.BaseQuant8x8Intra[0] : Indeo5Tables.BaseQuant8x8Inter[0];
      scale = isIntra ? Indeo5Tables.ScaleQuant8x8Intra[0][0] : Indeo5Tables.ScaleQuant8x8Inter[0][0];
    } else {
      basis = isIntra ? Indeo5Tables.BaseQuant4x4Intra : Indeo5Tables.BaseQuant4x4Inter;
      scale = isIntra ? Indeo5Tables.ScaleQuant4x4Intra[0] : Indeo5Tables.ScaleQuant4x4Inter[0];
    }

    var count = blockSize * blockSize;
    for (var i = 0; i < count; ++i) {
      var desired = coefficients[i];
      if (i == 0 && isIntra)
        desired -= previousDc;

      var weight = (basis[i] * scale) >> 9;
      var quantized = _Quantize(desired, weight);
      coefficients[i] = quantized;

      if (i == 0 && isIntra)
        previousDc += _Dequantize(quantized, weight);
    }
  }

  private static int _Quantize(int desired, int weight) {
    if (desired == 0)
      return 0;
    if (weight <= 1)
      return Math.Clamp(desired, _MIN_ESCAPED_VALUE, _MAX_ESCAPED_VALUE);

    var center = Math.Clamp(desired / weight, _MIN_ESCAPED_VALUE, _MAX_ESCAPED_VALUE);
    var best = 0;
    var bestError = Math.Abs(desired);

    for (var candidate = Math.Max(_MIN_ESCAPED_VALUE, center - 3);
         candidate <= Math.Min(_MAX_ESCAPED_VALUE, center + 3);
         ++candidate) {
      var error = Math.Abs(_Dequantize(candidate, weight) - desired);
      if (error > bestError || error == bestError && Math.Abs(candidate) >= Math.Abs(best))
        continue;

      best = candidate;
      bestError = error;
    }

    return best;
  }

  private static int _Dequantize(int value, int weight) {
    if (value == 0 || weight <= 1)
      return value;

    var bias = ((weight ^ 1) - 1) >> 1;
    return value * weight + (value > 0 ? bias : -bias);
  }

  private static int _Round(double value)
    => value >= 0 ? (int)Math.Floor(value + 0.5) : (int)Math.Ceiling(value - 0.5);

  private static bool _IsZero(ReadOnlySpan<int> coefficients, int count) {
    for (var i = 0; i < count; ++i)
      if (coefficients[i] != 0)
        return false;

    return true;
  }

  // ============================================================================================
  // Run/value coding
  // ============================================================================================

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
          $"An Indeo 5 escaped coefficient needs run {run} and signed value code {signed}; the fixed six-bit "
          + "writer can state runs through 64 and values through 4095.");

      _WriteSymbol(writer, _BLOCK_ESCAPE);
      _WriteSymbol(writer, run - 1);
      _WriteSymbol(writer, signed & 0x3F);
      _WriteSymbol(writer, signed >> 6);
      previous = position;
    }

    _WriteSymbol(writer, _BLOCK_END);
  }

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
