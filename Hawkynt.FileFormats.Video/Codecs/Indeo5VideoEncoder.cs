using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Indeo;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>How an explicitly requested predicted IV50 picture participates in the reference chain.</summary>
public enum Indeo5FrameMode {
  /// <summary>A normal P-picture which becomes the reference for following pictures.</summary>
  Reference,

  /// <summary>A droppable P-picture which is never retained as a normal reference (IV50 frame type 3).</summary>
  Disposable,

  /// <summary>
  /// A droppable P-picture in an IV50 scalable stream (frame type 2). Consecutive pictures of this
  /// kind form the temporary scalable reference chain and a later normal reference picture ignores it.
  /// </summary>
  ScalableDisposable,
}

/// <summary>
/// Encodes Intel Indeo Video Interactive 5 (<c>IV50</c>) with intra, reference P, droppable P and
/// scalable-droppable P pictures.
/// </summary>
/// <remarks>
/// The generic video-encoder contract has no per-picture reference/disposable flag, so its normal
/// <see cref="TryEncode(RawImage,long?,out CodedPacket)"/> path deliberately emits only reference
/// pictures. Call the IV50-specific overload with <see cref="Indeo5FrameMode"/> when a picture may be
/// discarded. Scalable layout is selected when the encoder is created because the I-picture's GOP
/// header fixes the wavelet subdivision for every following picture in that GOP.
/// <para/>
/// IV50 defines no B-picture. Its predicted picture types are a normal reference P-picture, a
/// scalable droppable P-picture and a no-reference droppable P-picture. This encoder emits exactly
/// those three semantics and mirrors the decoder's three-buffer rotation rather than pretending the
/// two droppable forms are interchangeable.
/// <para/>
/// Scalable encoding performs the inverse of the decoder's 5/3 recomposition: luminance is split into
/// four half-size bands, then each band uses the transform fixed by IV50 (2-D slant, row slant, column
/// slant, direct samples). Non-scalable luminance remains one 8x8 slant band. Chroma is YVU9 and uses
/// the 4x4 slant transform in both profiles.
/// <para/>
/// Intel never published an IV50 encoder specification and FFmpeg has only a decoder. Bitstream syntax
/// and reference-buffer semantics were checked against FFmpeg's LGPL-2.1-or-later
/// <c>libavcodec/indeo5.c</c>. The forward slant matrices and forward 5/3 analysis are independently
/// derived from the inverse operations already present in this repository.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Indeo5VideoEncoder : IVideoCodecEncoder<Indeo5VideoEncoder> {

  private static readonly CodecTag _TAG = CodecTag.FromCharacters("IV50");

  private const int _PICTURE_SIZE_ESCAPE = 15;
  private const int _TILE_SIZE = 64;
  private const int _LUMA_MACROBLOCK = 16;
  private const int _LUMA_BLOCK = 8;
  private const int _SCALABLE_LUMA_MACROBLOCK = 8;
  private const int _CHROMA_MACROBLOCK = 4;
  private const int _CHROMA_BLOCK = 4;
  private const int _HUFFMAN_BITS = 6;
  private const int _LONG_TILE_SIZE = 0xFFFFFF;
  private const int _MIN_ESCAPED_VALUE = -2047;
  private const int _MAX_ESCAPED_VALUE = 2048;

  private static readonly int _BLOCK_ESCAPE = IviRunValueMap.Defaults[8].EscapeSymbol;
  private static readonly int _BLOCK_END = IviRunValueMap.Defaults[8].EndOfBlockSymbol;

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

  private static readonly double[] _FORWARD_SLANT4 = [
    1d / 4, 1d / 4, 1d / 4, 1d / 4,
    10d / 29, 4d / 29, -4d / 29, -10d / 29,
    1d / 4, -1d / 4, -1d / 4, 1d / 4,
    4d / 29, -10d / 29, 10d / 29, -4d / 29,
  ];

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly bool _scalable;
  private readonly Indeo5Decoder _reconstructionDecoder;
  private readonly _ReferenceBands?[] _buffers = new _ReferenceBands?[3];
  private MediaStreamInfo? _stream;
  private byte _frameNumber;
  private bool _started;
  private int _previousFrameType = Indeo5Decoder.FrameTypeIntra;
  private int _bufferSwitch;
  private int _destinationBuffer;
  private int _referenceBuffer;
  private int _scalableReferenceBuffer;
  private bool _scalableSequence;

  private enum _TransformKind {
    Slant8x8,
    RowSlant8,
    ColumnSlant8,
    Direct8x8,
    Slant4x4,
  }

  private sealed record _ReferenceBands(short[][] Luma, short[] ChromaRed, short[] ChromaBlue);

  private readonly record struct _Planes(
    short[] Luma,
    short[] ChromaBlue,
    short[] ChromaRed,
    int ChromaWidth,
    int ChromaHeight);

  private Indeo5VideoEncoder(MediaStreamInfo stream, bool scalable) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._scalable = scalable;
    this._reconstructionDecoder = new(this._width, this._height);
  }

  public static string CodecName => "Intel Indeo Video Interactive 5";

  public static CodecTag Codec => _TAG;

  public static Indeo5VideoEncoder Create(MediaStreamInfo stream) => Create(stream, scalable: false);

  public static Indeo5VideoEncoder Create(MediaStreamInfo stream, bool scalable) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Intel Indeo Video Interactive 5 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"An Indeo 5 encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");
    if (stream.Width > 0x1FFF || stream.Height > 0x1FFF)
      throw new NotSupportedException(
        $"Indeo 5 states an escaped picture size in thirteen bits per dimension; {stream.Width}x{stream.Height} does not fit.");
    if (scalable && ((stream.Width | stream.Height) & 1) != 0)
      throw new NotSupportedException(
        $"IV50 scalable recomposition produces pixels in 2x2 groups; {stream.Width}x{stream.Height} is not even in both dimensions.");

    var pixels = (long)stream.Width * stream.Height;
    if (pixels > 1 << 26)
      throw new NotSupportedException(
        $"A {stream.Width}x{stream.Height} Indeo 5 picture has {pixels} luminance samples; this implementation limits a picture to {1 << 26}.");
    if (pixels * 3 > int.MaxValue)
      throw new NotSupportedException(
        $"A {stream.Width}x{stream.Height} picture needs {pixels * 3} RGB bytes, which is more than one managed array can hold ({int.MaxValue}).");

    return new(stream, scalable);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet)
    => this.TryEncode(frame, presentationTimestamp, Indeo5FrameMode.Reference, out packet);

  public bool TryEncode(
    RawImage frame,
    long? presentationTimestamp,
    Indeo5FrameMode mode,
    out CodedPacket packet) {

    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"Indeo 5 geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        "The source RawImage does not contain enough pixel data for its declared format and dimensions.");
    if (!this._started && mode != Indeo5FrameMode.Reference)
      throw new InvalidOperationException("The first Indeo 5 picture must be a reference picture so it can carry the GOP I-frame.");
    if (mode == Indeo5FrameMode.ScalableDisposable && !this._scalable)
      throw new InvalidOperationException(
        "A scalable disposable IV50 P-picture requires an encoder created with scalable: true.");

    var frameType = !this._started
      ? Indeo5Decoder.FrameTypeIntra
      : mode switch {
        Indeo5FrameMode.Reference => Indeo5Decoder.FrameTypeInter,
        Indeo5FrameMode.Disposable => Indeo5Decoder.FrameTypeInterNoReference,
        Indeo5FrameMode.ScalableDisposable => Indeo5Decoder.FrameTypeInterScalable,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
      };

    this._SwitchBuffers(frameType);

    var reference = frameType == Indeo5Decoder.FrameTypeIntra
      ? null
      : this._buffers[this._referenceBuffer]
        ?? throw new InvalidOperationException(
          "This Indeo 5 predicted picture has no reconstructed reference in the buffer selected by the format's frame-type state machine.");

    var planes = this._ToYvu9(frame);
    var luma = this._scalable
      ? _DecomposeFiveThree(planes.Luma, this._width, this._height)
      : [planes.Luma];

    var writer = new _BitWriter();
    this._WritePictureHeader(writer, frameType);

    var reconstructedLuma = new short[luma.Length][];
    var lumaWidth = this._scalable ? this._width >> 1 : this._width;
    var lumaHeight = this._scalable ? this._height >> 1 : this._height;
    var lumaTile = this._scalable ? _TILE_SIZE >> 1 : _TILE_SIZE;
    var lumaMacroblock = this._scalable ? _SCALABLE_LUMA_MACROBLOCK : _LUMA_MACROBLOCK;

    for (var band = 0; band < luma.Length; ++band)
      reconstructedLuma[band] = _WriteBand(
        writer,
        luma[band],
        reference?.Luma[band],
        lumaWidth,
        lumaHeight,
        lumaTile,
        lumaTile,
        lumaMacroblock,
        _LUMA_BLOCK,
        _LumaTransform(band, this._scalable),
        _LumaScan(band, this._scalable),
        plane: 0,
        band,
        lumaBands: luma.Length,
        isIntra: frameType == Indeo5Decoder.FrameTypeIntra);

    var chromaTile = (_TILE_SIZE + 3) >> 2;
    var reconstructedRed = _WriteBand(
      writer,
      planes.ChromaRed,
      reference?.ChromaRed,
      planes.ChromaWidth,
      planes.ChromaHeight,
      chromaTile,
      chromaTile,
      _CHROMA_MACROBLOCK,
      _CHROMA_BLOCK,
      _TransformKind.Slant4x4,
      IviTables.DirectScan4x4,
      plane: 1,
      bandNumber: 0,
      lumaBands: luma.Length,
      isIntra: frameType == Indeo5Decoder.FrameTypeIntra);
    var reconstructedBlue = _WriteBand(
      writer,
      planes.ChromaBlue,
      reference?.ChromaBlue,
      planes.ChromaWidth,
      planes.ChromaHeight,
      chromaTile,
      chromaTile,
      _CHROMA_MACROBLOCK,
      _CHROMA_BLOCK,
      _TransformKind.Slant4x4,
      IviTables.DirectScan4x4,
      plane: 2,
      bandNumber: 0,
      lumaBands: luma.Length,
      isIntra: frameType == Indeo5Decoder.FrameTypeIntra);

    var data = writer.ToArray();

    _ = this._reconstructionDecoder.Decode(data)
      ?? throw new InvalidDataException("The Indeo 5 encoder produced a null picture while encoding a non-null frame.");

    this._buffers[this._destinationBuffer] = new(reconstructedLuma, reconstructedRed, reconstructedBlue);
    this._previousFrameType = frameType;
    this._started = true;

    packet = new(
      this._requested.Index,
      data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: frameType == Indeo5Decoder.FrameTypeIntra);

    unchecked {
      ++this._frameNumber;
    }

    return true;
  }

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

  private void _SwitchBuffers(int frameType) {
    switch (this._previousFrameType) {
      case Indeo5Decoder.FrameTypeIntra:
      case Indeo5Decoder.FrameTypeInter:
        this._bufferSwitch ^= 1;
        this._destinationBuffer = this._bufferSwitch;
        this._referenceBuffer = this._bufferSwitch ^ 1;
        break;

      case Indeo5Decoder.FrameTypeInterScalable:
        if (!this._scalableSequence) {
          this._scalableReferenceBuffer = 2;
          this._scalableSequence = true;
        }

        (this._destinationBuffer, this._scalableReferenceBuffer)
          = (this._scalableReferenceBuffer, this._destinationBuffer);
        this._referenceBuffer = this._scalableReferenceBuffer;
        break;
    }

    switch (frameType) {
      case Indeo5Decoder.FrameTypeIntra:
        this._bufferSwitch = 0;
        goto case Indeo5Decoder.FrameTypeInter;

      case Indeo5Decoder.FrameTypeInter:
        this._scalableSequence = false;
        this._destinationBuffer = this._bufferSwitch;
        this._referenceBuffer = this._bufferSwitch ^ 1;
        break;
    }
  }

  private void _WritePictureHeader(_BitWriter writer, int frameType) {
    writer.Write(0x1F, 5);
    writer.Write(frameType, 3);
    writer.Write(this._frameNumber, 8);

    if (frameType == Indeo5Decoder.FrameTypeIntra)
      this._WriteGroupHeader(writer);

    writer.Write(0x40, 8);
    _WriteSixBitCodebook(writer);
    writer.Write(0, 3);
    writer.Align();
  }

  private void _WriteGroupHeader(_BitWriter writer) {
    writer.Write(0x40, 8);
    writer.Write(0, 2);
    writer.Write(this._scalable ? 1 : 0, 2);
    writer.WriteFlag(false);

    writer.Write(_PICTURE_SIZE_ESCAPE, 4);
    writer.Write(this._height, 13);
    writer.Write(this._width, 13);

    var lumaBands = this._scalable ? 4 : 1;
    for (var band = 0; band < lumaBands; ++band) {
      writer.WriteFlag(false);
      writer.WriteFlag(this._scalable);
      writer.WriteFlag(false);
      writer.WriteFlag(false);
      writer.Write(0, 2);
    }

    writer.WriteFlag(false);
    writer.WriteFlag(true);
    writer.WriteFlag(true);
    writer.WriteFlag(false);
    writer.Write(0, 2);

    writer.Align();
    writer.Write(0, 23);
    writer.WriteFlag(false);
    writer.Align();
  }

  private static void _WriteSixBitCodebook(_BitWriter writer) {
    writer.Write(7, 3);
    writer.Write(1, 4);
    writer.Write(_HUFFMAN_BITS, 4);
  }

  private static short[] _WriteBand(
    _BitWriter writer,
    short[] samples,
    short[]? reference,
    int width,
    int height,
    int tileWidth,
    int tileHeight,
    int macroblockSize,
    int blockSize,
    _TransformKind transform,
    byte[] scan,
    int plane,
    int bandNumber,
    int lumaBands,
    bool isIntra) {

    writer.Write(0x80, 8);
    _WriteSixBitCodebook(writer);
    writer.WriteFlag(false);
    writer.Write(0, 5);
    writer.Align();

    var alignment = plane == 0 ? 16 : 8;
    var pitch = _Align(width, alignment);
    var alignedHeight = _Align(height, alignment);
    var residuals = new short[pitch * alignedHeight];
    var reconstructedResiduals = new short[pitch * alignedHeight];

    for (var y = 0; y < height; ++y) {
      var source = y * width;
      var target = y * pitch;
      for (var x = 0; x < width; ++x)
        residuals[target + x] = (short)(isIntra ? samples[source + x] : samples[source + x] - reference![source + x]);
    }

    for (var tileY = 0; tileY < height; tileY += tileHeight)
      for (var tileX = 0; tileX < width; tileX += tileWidth) {
        var actualWidth = Math.Min(tileWidth, width - tileX);
        var actualHeight = Math.Min(tileHeight, height - tileY);
        var tile = _WriteTile(
          residuals,
          reconstructedResiduals,
          pitch,
          tileX,
          tileY,
          actualWidth,
          actualHeight,
          macroblockSize,
          blockSize,
          transform,
          scan,
          plane,
          bandNumber,
          lumaBands,
          isIntra);
        writer.WriteBytes(tile);
      }

    var result = new short[width * height];
    for (var y = 0; y < height; ++y) {
      var source = y * pitch;
      var target = y * width;
      for (var x = 0; x < width; ++x)
        result[target + x] = (short)(reconstructedResiduals[source + x]
                                     + (isIntra ? 0 : reference![target + x]));
    }

    return result;
  }

  private static byte[] _WriteTile(
    short[] residuals,
    short[] reconstructedResiduals,
    int pitch,
    int tileX,
    int tileY,
    int tileWidth,
    int tileHeight,
    int macroblockSize,
    int blockSize,
    _TransformKind transform,
    byte[] scan,
    int plane,
    int bandNumber,
    int lumaBands,
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

          _ForwardTransform(residuals, pitch, blockX, blockY, blockSize, transform, target);
          _QuantizeBlock(target, blockSize, plane, bandNumber, lumaBands, isIntra, _IsTwoDimensional(transform), ref previousDc);

          var coded = !_IsZero(target, blockSize * blockSize);
          if (coded)
            pattern |= 1 << block;

          _ReconstructBlock(
            target,
            reconstructedResiduals,
            pitch,
            blockX,
            blockY,
            blockSize,
            transform,
            plane,
            bandNumber,
            lumaBands,
            isIntra,
            coded,
            previousDc);
        }

        if (!isIntra && pattern == 0) {
          macroblockHeaders.WriteFlag(true);
        } else {
          macroblockHeaders.WriteFlag(false);
          if (!isIntra)
            macroblockHeaders.WriteFlag(true);

          macroblockHeaders.Write(pattern, blocksPerMacroblock);

          if (!isIntra) {
            _WriteSymbol(macroblockHeaders, 0);
            _WriteSymbol(macroblockHeaders, 0);
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
    writer.WriteFlag(false);
    writer.WriteFlag(true);
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

  private static void _ForwardTransform(
    short[] samples,
    int pitch,
    int x,
    int y,
    int size,
    _TransformKind transform,
    Span<int> coefficients) {

    switch (transform) {
      case _TransformKind.Slant8x8:
      case _TransformKind.Slant4x4:
        _ForwardSlant(samples, pitch, x, y, size, coefficients);
        return;

      case _TransformKind.RowSlant8:
        _ForwardRowSlant8(samples, pitch, x, y, coefficients);
        return;

      case _TransformKind.ColumnSlant8:
        _ForwardColumnSlant8(samples, pitch, x, y, coefficients);
        return;

      case _TransformKind.Direct8x8:
        for (var row = 0; row < 8; ++row)
          for (var column = 0; column < 8; ++column)
            coefficients[row * 8 + column] = samples[(y + row) * pitch + x + column];
        return;

      default:
        throw new ArgumentOutOfRangeException(nameof(transform));
    }
  }

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

  private static void _ForwardRowSlant8(short[] samples, int pitch, int x, int y, Span<int> coefficients) {
    for (var row = 0; row < 8; ++row)
      for (var frequency = 0; frequency < 8; ++frequency) {
        double sum = 0;
        for (var source = 0; source < 8; ++source)
          sum += 2d * samples[(y + row) * pitch + x + source] * _FORWARD_SLANT8[frequency * 8 + source];

        coefficients[row * 8 + frequency] = _Round(sum);
      }
  }

  private static void _ForwardColumnSlant8(short[] samples, int pitch, int x, int y, Span<int> coefficients) {
    for (var column = 0; column < 8; ++column)
      for (var frequency = 0; frequency < 8; ++frequency) {
        double sum = 0;
        for (var source = 0; source < 8; ++source)
          sum += 2d * samples[(y + source) * pitch + x + column] * _FORWARD_SLANT8[frequency * 8 + source];

        coefficients[frequency * 8 + column] = _Round(sum);
      }
  }

  private static void _QuantizeBlock(
    Span<int> coefficients,
    int blockSize,
    int plane,
    int bandNumber,
    int lumaBands,
    bool isIntra,
    bool isTwoDimensional,
    ref int previousDc) {

    _Quantisation(blockSize, plane, bandNumber, lumaBands, isIntra, out var basis, out var scale);

    var count = blockSize * blockSize;
    for (var i = 0; i < count; ++i) {
      var desired = coefficients[i];
      if (i == 0 && isIntra && isTwoDimensional)
        desired -= previousDc;

      var weight = (basis[i] * scale) >> 9;
      var quantized = _Quantize(desired, weight);
      coefficients[i] = quantized;

      if (i == 0 && isIntra && isTwoDimensional)
        previousDc += _Dequantize(quantized, weight);
    }
  }

  private static void _ReconstructBlock(
    ReadOnlySpan<int> quantized,
    short[] destination,
    int pitch,
    int x,
    int y,
    int blockSize,
    _TransformKind transform,
    int plane,
    int bandNumber,
    int lumaBands,
    bool isIntra,
    bool coded,
    int previousDc) {

    var offset = y * pitch + x;
    if (!coded) {
      if (isIntra)
        _DcTransform(transform, previousDc, destination, offset, pitch, blockSize);

      return;
    }

    _Quantisation(blockSize, plane, bandNumber, lumaBands, isIntra, out var basis, out var scale);
    var count = blockSize * blockSize;
    var coefficients = new int[count];
    var columnFlags = new byte[blockSize];

    for (var i = 0; i < count; ++i) {
      var weight = (basis[i] * scale) >> 9;
      var value = _Dequantize(quantized[i], weight);
      coefficients[i] = value;
      if (value != 0)
        columnFlags[i & (blockSize - 1)] = 1;
    }

    if (isIntra && _IsTwoDimensional(transform)) {
      coefficients[0] = previousDc;
      if (previousDc != 0)
        columnFlags[0] = 1;
    }

    switch (transform) {
      case _TransformKind.Slant8x8:
        IviTransforms.InverseSlant8x8(coefficients, destination, offset, pitch, columnFlags);
        break;
      case _TransformKind.RowSlant8:
        IviTransforms.RowSlant8(coefficients, destination, offset, pitch, columnFlags);
        break;
      case _TransformKind.ColumnSlant8:
        IviTransforms.ColumnSlant8(coefficients, destination, offset, pitch, columnFlags);
        break;
      case _TransformKind.Direct8x8:
        IviTransforms.PutPixels8x8(coefficients, destination, offset, pitch, columnFlags);
        break;
      case _TransformKind.Slant4x4:
        IviTransforms.InverseSlant4x4(coefficients, destination, offset, pitch, columnFlags);
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(transform));
    }
  }

  private static void _DcTransform(
    _TransformKind transform,
    int dc,
    short[] destination,
    int offset,
    int pitch,
    int blockSize) {

    switch (transform) {
      case _TransformKind.Slant8x8:
      case _TransformKind.Slant4x4:
        IviTransforms.DcSlant2D(dc, destination, offset, pitch, blockSize);
        break;
      case _TransformKind.RowSlant8:
        IviTransforms.DcRowSlant(dc, destination, offset, pitch, blockSize);
        break;
      case _TransformKind.ColumnSlant8:
        IviTransforms.DcColumnSlant(dc, destination, offset, pitch, blockSize);
        break;
      case _TransformKind.Direct8x8:
        IviTransforms.PutDcPixel8x8(dc, destination, offset, pitch, blockSize);
        break;
      default:
        throw new ArgumentOutOfRangeException(nameof(transform));
    }
  }

  private static void _Quantisation(
    int blockSize,
    int plane,
    int bandNumber,
    int lumaBands,
    bool isIntra,
    out ReadOnlySpan<ushort> basis,
    out int scale) {

    if (blockSize == 4) {
      basis = isIntra ? Indeo5Tables.BaseQuant4x4Intra : Indeo5Tables.BaseQuant4x4Inter;
      scale = isIntra ? Indeo5Tables.ScaleQuant4x4Intra[0] : Indeo5Tables.ScaleQuant4x4Inter[0];
      return;
    }

    var matrix = plane == 0 && lumaBands > 1 ? bandNumber + 1 : 0;
    basis = isIntra ? Indeo5Tables.BaseQuant8x8Intra[matrix] : Indeo5Tables.BaseQuant8x8Inter[matrix];
    scale = isIntra ? Indeo5Tables.ScaleQuant8x8Intra[matrix][0] : Indeo5Tables.ScaleQuant8x8Inter[matrix][0];
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

  private static bool _IsTwoDimensional(_TransformKind transform)
    => transform is _TransformKind.Slant8x8 or _TransformKind.Slant4x4;

  private static _TransformKind _LumaTransform(int band, bool scalable)
    => !scalable ? _TransformKind.Slant8x8 : band switch {
      0 => _TransformKind.Slant8x8,
      1 => _TransformKind.RowSlant8,
      2 => _TransformKind.ColumnSlant8,
      3 => _TransformKind.Direct8x8,
      _ => throw new ArgumentOutOfRangeException(nameof(band)),
    };

  private static byte[] _LumaScan(int band, bool scalable)
    => !scalable ? IviTables.ZigzagDirect : band switch {
      0 => IviTables.ZigzagDirect,
      1 => IviTables.VerticalScan8x8,
      2 => IviTables.HorizontalScan8x8,
      3 => IviTables.HorizontalScan8x8,
      _ => throw new ArgumentOutOfRangeException(nameof(band)),
    };

  private static int _Round(double value)
    => value >= 0 ? (int)Math.Floor(value + 0.5) : (int)Math.Ceiling(value - 0.5);

  private static bool _IsZero(ReadOnlySpan<int> coefficients, int count) {
    for (var i = 0; i < count; ++i)
      if (coefficients[i] != 0)
        return false;

    return true;
  }

  private static short[][] _DecomposeFiveThree(short[] source, int width, int height) {
    var halfWidth = width >> 1;
    var halfHeight = height >> 1;
    var lowRows = new int[halfWidth * height];
    var highRows = new int[halfWidth * height];
    var row = new int[width];
    var low = new int[halfWidth];
    var high = new int[halfWidth];

    for (var y = 0; y < height; ++y) {
      for (var x = 0; x < width; ++x)
        row[x] = source[y * width + x];

      _AnalyseFiveThree(row, low, high);
      Array.Copy(low, 0, lowRows, y * halfWidth, halfWidth);
      Array.Copy(high, 0, highRows, y * halfWidth, halfWidth);
    }

    var result = new[] {
      new short[halfWidth * halfHeight],
      new short[halfWidth * halfHeight],
      new short[halfWidth * halfHeight],
      new short[halfWidth * halfHeight],
    };
    var column = new int[height];
    var lowColumn = new int[halfHeight];
    var highColumn = new int[halfHeight];

    for (var x = 0; x < halfWidth; ++x) {
      for (var y = 0; y < height; ++y)
        column[y] = lowRows[y * halfWidth + x];

      _AnalyseFiveThree(column, lowColumn, highColumn);
      for (var y = 0; y < halfHeight; ++y) {
        result[0][y * halfWidth + x] = checked((short)lowColumn[y]);
        result[1][y * halfWidth + x] = checked((short)highColumn[y]);
      }

      for (var y = 0; y < height; ++y)
        column[y] = highRows[y * halfWidth + x];

      _AnalyseFiveThree(column, lowColumn, highColumn);
      for (var y = 0; y < halfHeight; ++y) {
        result[2][y * halfWidth + x] = checked((short)lowColumn[y]);
        result[3][y * halfWidth + x] = checked((short)highColumn[y]);
      }
    }

    return result;
  }

  private static void _AnalyseFiveThree(ReadOnlySpan<int> source, Span<int> low, Span<int> high) {
    var count = source.Length >> 1;

    for (var i = 0; i < count; ++i) {
      var even = source[i << 1];
      var nextEven = source[i + 1 < count ? (i + 1) << 1 : i << 1];
      var odd = source[(i << 1) + 1];
      high[i] = _Round((even + nextEven) * 0.5) - odd;
    }

    for (var i = 0; i < count; ++i) {
      var previousHigh = high[i == 0 ? 0 : i - 1];
      low[i] = 2 * source[i << 1] - _Round((previousHigh + high[i]) * 0.5);
    }
  }

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

  private _Planes _ToYvu9(RawImage frame) {
    var rgb = frame.ToRgb24();
    var luma = new short[this._width * this._height];
    var chromaWidth = (this._width + 3) >> 2;
    var chromaHeight = (this._height + 3) >> 2;
    var chromaBlue = new short[chromaWidth * chromaHeight];
    var chromaRed = new short[chromaBlue.Length];
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

        luma[target + x] = (short)(_ClampByte(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16) - 128);
        var chromaAt = chromaRow + (x >> 2);
        blueTotals[chromaAt] += ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
        redTotals[chromaAt] += ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;
        ++counts[chromaAt];
      }
    }

    for (var i = 0; i < chromaBlue.Length; ++i) {
      var count = counts[i];
      chromaBlue[i] = (short)(_ClampByte((blueTotals[i] + count / 2) / count) - 128);
      chromaRed[i] = (short)(_ClampByte((redTotals[i] + count / 2) / count) - 128);
    }

    return new(luma, chromaBlue, chromaRed, chromaWidth, chromaHeight);
  }

  private static byte _ClampByte(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

  private static int _Align(int value, int alignment) => (value + alignment - 1) / alignment * alignment;

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
