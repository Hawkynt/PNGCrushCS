using System;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.Vc1;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes VC-1 / Windows Media Video 9 as progressive Main-profile intra pictures.
/// </summary>
/// <remarks>
/// Every input picture becomes an independently decodable I picture. Each 8x8 block is transformed
/// with the analytical forward transform from SMPTE 421M Annex A.2, quantised with the same uniform
/// quantiser the decoder reverses, and carries both its predicted DC coefficient and all non-zero AC
/// coefficients. AC values use the standard's Escape Mode 3 representation; it is larger than choosing
/// an optimal trained VLC for every run/level pair, but it covers the complete coefficient domain and
/// keeps the first writer focused on reconstruction rather than a rate-control heuristic.
/// <para/>
/// The sequence is Main profile, progressive, one reference-independent picture per packet, uniform
/// quantiser 3, no overlap smoothing, no range reduction, no multi-resolution coding and no in-loop
/// filter. The container receives a Video-for-Windows <c>BITMAPINFOHEADER</c> followed by the four-byte
/// <c>STRUCT_C</c> sequence header, which is how <c>WMV3</c> carries Simple/Main profile state.
/// <para/>
/// RGB is converted to studio-swing BT.601 YCbCr 4:2:0 before transformation. Chroma is averaged over
/// each 2x2 luma square and picture edges are replicated out to the macroblock boundary; the crop back
/// to the declared display size belongs to the decoder.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Vc1VideoEncoder : IVideoCodecEncoder<Vc1VideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("WMV3");

  /// <summary>
  /// Main-profile STRUCT_C: no post-processing hints, filters, range reduction, B pictures or
  /// multi-resolution coding; reserved bits 0101; uniform sequence quantiser.
  /// </summary>
  private static ReadOnlySpan<byte> _SequenceHeader => [0x40, 0x01, 0x00, 0x0D];

  private const int _PICTURE_QUANTISER = 3;
  private const int _DC_STEP_SIZE = 8;
  private const int _AC_STEP_SIZE = _PICTURE_QUANTISER * 2;
  private const int _DEFAULT_DC_PREDICTOR = 128;
  private const int _DC_ESCAPE_INDEX = 119;
  private const int _MODE3_LEVEL_BITS = 11;
  private const int _MODE3_RUN_BITS = 6;

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private int _frameCount;

  private Vc1VideoEncoder(MediaStreamInfo stream) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._macroblockWidth = (stream.Width + 15) / 16;
    this._macroblockHeight = (stream.Height + 15) / 16;

    var format = new byte[BitmapInfoHeader.StructSize + _SequenceHeader.Length];
    var header = new BitmapInfoHeader(
      HeaderSize: format.Length,
      Width: stream.Width,
      Height: stream.Height,
      Planes: 1,
      BitsPerPixel: 24,
      Compression: unchecked((int)_Tag.Value),
      ImageSize: 0,
      XPixelsPerMeter: 0,
      YPixelsPerMeter: 0,
      ColorsUsed: 0,
      ImportantColors: 0);
    header.WriteTo(format);
    _SequenceHeader.CopyTo(format.AsSpan(BitmapInfoHeader.StructSize));

    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = 24,
      CodecPrivateData = format,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "VC-1 / Windows Media Video 9 (SMPTE 421M, Simple and Main profile intra pictures)";

  public static CodecTag Codec => _Tag;

  public static Vc1VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("VC-1 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"A VC-1 encoder needs the picture size up front; {stream.Width}x{stream.Height} was supplied.");

    var macroblockWidth = ((long)stream.Width + 15) / 16;
    var macroblockHeight = ((long)stream.Height + 15) / 16;
    var paddedPixels = checked(macroblockWidth * 16 * macroblockHeight * 16);
    if (paddedPixels > int.MaxValue || (long)stream.Width * stream.Height * 3 > int.MaxValue)
      throw new NotSupportedException(
        $"A picture of {stream.Width}x{stream.Height} is larger than one managed VC-1 frame can hold.");

    return new(stream);
  }

  /// <summary>Encodes one independently decodable Main-profile I picture.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"VC-1 geometry is fixed at {this._width}x{this._height} for the life of the stream; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var rgb = frame.Format == PixelFormat.Rgb24
      ? frame
      : FastRawImageConverter.Convert(frame, PixelFormat.Rgb24);
    var samples = this._ToYuv420(rgb.PixelData);
    var data = this._EncodePicture(samples);

    packet = new(
      this._stream.Index,
      data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  // ============================================================================================
  // Picture syntax
  // ============================================================================================

  private byte[] _EncodePicture(Vc1Frame samples) {
    var writer = new Vc1BitWriter();

    // Figure 13, progressive Simple/Main picture layer. FRMCNT is diagnostic only; PTYPE zero is I
    // because STRUCT_C says MAXBFRAMES=0; BF is the encoder-buffer-fullness hint and has no effect on
    // reconstruction.
    writer.WriteBits(this._frameCount++ & 3, 2);
    writer.WriteBit(false);
    writer.WriteBits(0, 7);
    writer.WriteBits(_PICTURE_QUANTISER, 5);
    writer.WriteBit(false); // HALFQP, present because PQINDEX <= 8.

    // TRANSACFRM, TRANSACFRM2 and TRANSDCTAB: coding-set index zero for luma/chroma and the low-motion
    // DC tables. At PQINDEX 3 this selects the High Rate intra/inter AC sets.
    writer.WriteBit(false);
    writer.WriteBit(false);
    writer.WriteBit(false);

    var luma = new Vc1IntraPrediction(this._macroblockWidth * 2, this._macroblockHeight * 2);
    var cb = new Vc1IntraPrediction(this._macroblockWidth, this._macroblockHeight);
    var cr = new Vc1IntraPrediction(this._macroblockWidth, this._macroblockHeight);
    var lumaCoded = new byte[checked(this._macroblockWidth * this._macroblockHeight * 4)];
    var escape = new Vc1EscapeState();

    Span<int> blocks = stackalloc int[6 * 64];

    for (var mbY = 0; mbY < this._macroblockHeight; ++mbY)
      for (var mbX = 0; mbX < this._macroblockWidth; ++mbX) {
        var pattern = 0;
        for (var blockIndex = 0; blockIndex < 6; ++blockIndex) {
          var isLuma = blockIndex < 4;
          var (plane, stride) = samples.PlaneOf(blockIndex);
          var x = isLuma ? (mbX * 16) + ((blockIndex & 1) * 8) : mbX * 8;
          var y = isLuma ? (mbY * 16) + ((blockIndex >> 1) * 8) : mbY * 8;
          var block = blocks.Slice(blockIndex * 64, 64);

          Vc1ForwardTransform.Quantise8x8(plane, stride, x, y, _DC_STEP_SIZE, _AC_STEP_SIZE, block);
          if (_HasAc(block))
            pattern |= 1 << (5 - blockIndex);
        }

        writer.WriteCode(Vc1Tables.IPictureCbpcy, _EncodeCodedBlockPattern(pattern, mbX, mbY, lumaCoded));
        writer.WriteBit(false); // ACPRED: coefficients are stated without neighbour-edge prediction.

        for (var blockIndex = 0; blockIndex < 6; ++blockIndex) {
          var isLuma = blockIndex < 4;
          var prediction = isLuma ? luma : blockIndex == 4 ? cb : cr;
          var column = isLuma ? (mbX * 2) + (blockIndex & 1) : mbX;
          var row = isLuma ? (mbY * 2) + (blockIndex >> 1) : mbY;
          var block = blocks.Slice(blockIndex * 64, 64);
          var (predictor, _) = prediction.Predict(column, row, _DEFAULT_DC_PREDICTOR);

          _WriteDcDifferential(writer, block[0] - predictor, isLuma);
          if ((pattern & (1 << (5 - blockIndex))) != 0)
            _WriteAcCoefficients(writer, block, isLuma, escape);

          prediction.Store(column, row, block);
        }
      }

    return writer.ToArray();
  }

  /// <summary>Applies the inverse of the I-picture luma coded-block-pattern prediction in 8.1.2.1.</summary>
  private int _EncodeCodedBlockPattern(int pattern, int mbX, int mbY, byte[] coded) {
    var y0 = (pattern >> 5) & 1;
    var y1 = (pattern >> 4) & 1;
    var y2 = (pattern >> 3) & 1;
    var y3 = (pattern >> 2) & 1;

    var left = mbX > 0;
    var above = mbY > 0;
    var l1 = left ? _Coded(coded, mbX - 1, mbY, 1) : 0;
    var l3 = left ? _Coded(coded, mbX - 1, mbY, 3) : 0;
    var t2 = above ? _Coded(coded, mbX, mbY - 1, 2) : 0;
    var t3 = above ? _Coded(coded, mbX, mbY - 1, 3) : 0;
    var lt3 = left && above ? _Coded(coded, mbX - 1, mbY - 1, 3) : 0;

    var encoded = ((y0 ^ (lt3 == t2 ? l1 : t2)) << 5)
                  | ((y1 ^ (t2 == t3 ? y0 : t3)) << 4)
                  | ((y2 ^ (l1 == y0 ? l3 : y0)) << 3)
                  | ((y3 ^ (y0 == y1 ? y2 : y1)) << 2)
                  | (pattern & 0x03);

    var at = ((mbY * this._macroblockWidth) + mbX) * 4;
    coded[at] = (byte)y0;
    coded[at + 1] = (byte)y1;
    coded[at + 2] = (byte)y2;
    coded[at + 3] = (byte)y3;
    return encoded;
  }

  private int _Coded(byte[] coded, int mbX, int mbY, int block)
    => coded[(((mbY * this._macroblockWidth) + mbX) * 4) + block];

  private static bool _HasAc(ReadOnlySpan<int> block) {
    for (var i = 1; i < 64; ++i)
      if (block[i] != 0)
        return true;

    return false;
  }

  /// <summary>Writes all non-zero AC coefficients in normal scan order through Escape Mode 3.</summary>
  private static void _WriteAcCoefficients(
    Vc1BitWriter writer,
    ReadOnlySpan<int> block,
    bool luma,
    Vc1EscapeState escape) {
    var scan = Vc1Tables.NormalScan;
    var last = 63;
    while (last > 0 && block[scan[last]] == 0)
      --last;

    var run = 0;
    for (var position = 1; position <= last; ++position) {
      var level = block[scan[position]];
      if (level == 0) {
        ++run;
        continue;
      }

      var table = luma ? Vc1Tables.HighRateIntraCodes : Vc1Tables.HighRateInterCodes;
      var escapeIndex = luma ? Vc1Tables.HighRateIntraEscapeIndex : Vc1Tables.HighRateInterEscapeIndex;
      writer.WriteCode(table, escapeIndex);
      writer.WriteBit(false);
      writer.WriteBit(false); // ESCMODE 00b: Mode 3.
      writer.WriteBit(position == last); // ESCLR.

      if (escape.First) {
        // Table 59 because PQUANT=3: 00011b states an eleven-bit level. Table 61: 11b states a
        // six-bit run. These are the widest forms and therefore cover every coefficient position.
        writer.WriteBits(0b00011, 5);
        writer.WriteBits(0b11, 2);
        escape.First = false;
        escape.LevelCodeSize = _MODE3_LEVEL_BITS;
        escape.RunCodeSize = _MODE3_RUN_BITS;
      }

      var magnitude = level < 0 ? -level : level;
      if ((uint)magnitude >= 1u << escape.LevelCodeSize)
        throw new InvalidOperationException(
          $"A quantised VC-1 AC level of {level} does not fit the {escape.LevelCodeSize}-bit Mode 3 field.");

      writer.WriteBits(run, escape.RunCodeSize);
      writer.WriteBit(level < 0);
      writer.WriteBits(magnitude, escape.LevelCodeSize);
      run = 0;
    }
  }

  private static void _WriteDcDifferential(Vc1BitWriter writer, int differential, bool luma) {
    var magnitude = differential < 0 ? -differential : differential;
    var table = luma ? Vc1Tables.LowMotionLumaDc : Vc1Tables.LowMotionChromaDc;

    if (magnitude == 0) {
      writer.WriteCode(table, 0);
      return;
    }

    if (magnitude < _DC_ESCAPE_INDEX)
      writer.WriteCode(table, magnitude);
    else {
      if (magnitude > byte.MaxValue)
        throw new InvalidOperationException($"A DC differential of {differential} does not fit VC-1's 8-bit escape at PQINDEX 3.");

      writer.WriteCode(table, _DC_ESCAPE_INDEX);
      writer.WriteBits(magnitude, 8);
    }

    writer.WriteBit(differential < 0);
  }

  // ============================================================================================
  // RGB -> studio-swing BT.601 YCbCr 4:2:0
  // ============================================================================================

  private Vc1Frame _ToYuv420(ReadOnlySpan<byte> rgb) {
    var result = new Vc1Frame(this._macroblockWidth, this._macroblockHeight);

    for (var y = 0; y < result.LumaHeight; ++y) {
      var sourceY = Math.Min(y, this._height - 1);
      for (var x = 0; x < result.LumaWidth; ++x) {
        var sourceX = Math.Min(x, this._width - 1);
        var at = ((sourceY * this._width) + sourceX) * 3;
        result.Luma[(y * result.LumaWidth) + x] = _Luma(rgb[at], rgb[at + 1], rgb[at + 2]);
      }
    }

    for (var y = 0; y < result.ChromaHeight; ++y)
      for (var x = 0; x < result.ChromaWidth; ++x) {
        var x0 = Math.Min(x * 2, this._width - 1);
        var x1 = Math.Min(x0 + 1, this._width - 1);
        var y0 = Math.Min(y * 2, this._height - 1);
        var y1 = Math.Min(y0 + 1, this._height - 1);

        var cb = _Chroma(rgb, x0, y0, this._width, redDifference: false)
                 + _Chroma(rgb, x1, y0, this._width, redDifference: false)
                 + _Chroma(rgb, x0, y1, this._width, redDifference: false)
                 + _Chroma(rgb, x1, y1, this._width, redDifference: false);
        var cr = _Chroma(rgb, x0, y0, this._width, redDifference: true)
                 + _Chroma(rgb, x1, y0, this._width, redDifference: true)
                 + _Chroma(rgb, x0, y1, this._width, redDifference: true)
                 + _Chroma(rgb, x1, y1, this._width, redDifference: true);

        var at = (y * result.ChromaWidth) + x;
        result.Cb[at] = (cb + 2) >> 2;
        result.Cr[at] = (cr + 2) >> 2;
      }

    return result;
  }

  private static int _Luma(int red, int green, int blue)
    => _Clamp8((((66 * red) + (129 * green) + (25 * blue) + 128) >> 8) + 16);

  private static int _Chroma(ReadOnlySpan<byte> rgb, int x, int y, int width, bool redDifference) {
    var at = ((y * width) + x) * 3;
    var red = rgb[at];
    var green = rgb[at + 1];
    var blue = rgb[at + 2];
    var scaled = redDifference
      ? (112 * red) - (94 * green) - (18 * blue)
      : (-38 * red) - (74 * green) + (112 * blue);

    return _Clamp8(((scaled + 128) >> 8) + 128);
  }

  private static int _Clamp8(int value) => value < 0 ? 0 : value > 255 ? 255 : value;
}
