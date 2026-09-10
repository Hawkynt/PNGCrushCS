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
/// The writer deliberately starts with the smallest complete VC-1 coding subset rather than a
/// half-implemented motion coder. Every input picture becomes an I picture. Each 8x8 block carries its
/// predicted DC coefficient and no AC coefficients, so the result is a block-average representation of
/// the source: visibly coarse, but fully specified, independently decodable, and a valid foundation for
/// adding the transform/AC and inter-picture layers later without changing the stream contract.
/// <para/>
/// The sequence is Main profile, progressive, one reference-independent picture per packet, uniform
/// quantiser 3, no overlap smoothing, no range reduction, no multi-resolution coding and no in-loop
/// filter. Those choices are exactly the subset <see cref="Vc1VideoDecoder"/> reads today. The
/// container receives a Video-for-Windows <c>BITMAPINFOHEADER</c> followed by the four-byte
/// <c>STRUCT_C</c> sequence header, which is how <c>WMV3</c> carries Simple/Main profile state.
/// <para/>
/// RGB is converted to studio-swing BT.601 YCbCr 4:2:0 before block averaging. Chroma is averaged over
/// each 2x2 luma square and picture edges are replicated out to the macroblock boundary; the crop back
/// to the declared display size belongs to the decoder.
/// </remarks>
public sealed class Vc1VideoEncoder : IVideoCodecEncoder<Vc1VideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("WMV3");

  /// <summary>
  /// Main-profile STRUCT_C: no post-processing hints, filters, range reduction, B pictures or
  /// multi-resolution coding; reserved bits 0101; uniform sequence quantiser.
  /// </summary>
  private static ReadOnlySpan<byte> _SequenceHeader => [0x40, 0x01, 0x00, 0x0D];

  private const int _PICTURE_QUANTISER = 3;
  private const int _DC_STEP_SIZE = 8;
  private const int _DEFAULT_DC_PREDICTOR = 128;
  private const int _DC_ESCAPE_INDEX = 119;

  private static readonly byte[] _DcForSample = _BuildDcForSample();

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
    // DC tables. No AC symbol follows in the subset written here, but these fields are still mandatory.
    writer.WriteBit(false);
    writer.WriteBit(false);
    writer.WriteBit(false);

    var luma = new Vc1IntraPrediction(this._macroblockWidth * 2, this._macroblockHeight * 2);
    var cb = new Vc1IntraPrediction(this._macroblockWidth, this._macroblockHeight);
    var cr = new Vc1IntraPrediction(this._macroblockWidth, this._macroblockHeight);
    Span<int> quantised = stackalloc int[64];

    for (var mbY = 0; mbY < this._macroblockHeight; ++mbY)
      for (var mbX = 0; mbX < this._macroblockWidth; ++mbX) {
        // Table 168 index zero is the all-clear coded-block pattern. Since every neighbouring luma
        // block is all-clear as well, the prediction of 8.1.2.1 leaves all six blocks uncoded for AC.
        writer.WriteCode(Vc1Tables.IPictureCbpcy, 0);
        writer.WriteBit(false); // ACPRED

        for (var blockIndex = 0; blockIndex < 6; ++blockIndex) {
          var isLuma = blockIndex < 4;
          var prediction = isLuma ? luma : blockIndex == 4 ? cb : cr;
          var column = isLuma ? (mbX * 2) + (blockIndex & 1) : mbX;
          var row = isLuma ? (mbY * 2) + (blockIndex >> 1) : mbY;
          var (plane, stride) = samples.PlaneOf(blockIndex);
          var x = isLuma ? (mbX * 16) + ((blockIndex & 1) * 8) : mbX * 8;
          var y = isLuma ? (mbY * 16) + ((blockIndex >> 1) * 8) : mbY * 8;
          var average = _BlockAverage(plane, stride, x, y);
          var dc = _DcForSample[average];
          var (predictor, _) = prediction.Predict(column, row, _DEFAULT_DC_PREDICTOR);

          _WriteDcDifferential(writer, dc - predictor, isLuma);

          quantised.Clear();
          quantised[0] = dc;
          prediction.Store(column, row, quantised);
        }
      }

    return writer.ToArray();
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

  private static int _BlockAverage(int[] plane, int stride, int x, int y) {
    var sum = 0;
    for (var row = 0; row < 8; ++row) {
      var at = ((y + row) * stride) + x;
      for (var column = 0; column < 8; ++column)
        sum += plane[at + column];
    }

    return (sum + 32) >> 6;
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

  // ============================================================================================
  // DC-only quantisation
  // ============================================================================================

  /// <summary>
  /// For every possible target sample, picks the quantised DC whose exact VC-1 inverse transform is
  /// nearest. This is derived from Annex A's integer transform rather than from a floating-point DCT.
  /// </summary>
  private static byte[] _BuildDcForSample() {
    var result = new byte[256];

    for (var sample = 0; sample < result.Length; ++sample) {
      var best = 0;
      var bestError = int.MaxValue;
      for (var dc = 0; dc <= byte.MaxValue; ++dc) {
        var error = Math.Abs(_ReconstructDc(dc) - sample);
        if (error >= bestError)
          continue;

        best = dc;
        bestError = error;
        if (error == 0)
          break;
      }

      result[sample] = (byte)best;
    }

    return result;
  }

  private static int _ReconstructDc(int quantisedDc) {
    var coefficient = quantisedDc * _DC_STEP_SIZE;
    var firstStage = ((coefficient * 12) + 4) >> 3;
    return ((firstStage * 12) + 64) >> 7;
  }
}
