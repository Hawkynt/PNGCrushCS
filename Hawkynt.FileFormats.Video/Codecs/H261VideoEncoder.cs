using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bmp;
using FileFormat.Codecs.H261;
using FileFormat.Codecs.H263;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes H.261 video, ITU-T Recommendation H.261 — the write direction of
/// <see cref="H261VideoDecoder"/>, and the whole of the Recommendation's normative coding except the
/// four macroblock types that restate the quantiser.
/// </summary>
/// <remarks>
/// <b>QCIF and CIF, and nothing else.</b> Clause 3.1 defines two picture formats and PTYPE names one of
/// them in a single bit; there is no source-format field with five choices, no extended header and
/// nothing anywhere in the syntax that could state another size. So a stream of any other geometry is
/// refused by name at <see cref="Create"/> rather than scaled, padded or cropped into one of the two —
/// a 320x240 picture written as CIF would decode, and would be the wrong picture.
/// <para/>
/// <b>What it writes.</b> An intra picture every <see cref="_KEY_FRAME_INTERVAL"/> frames and a
/// predicted picture between them; every group of blocks with its own header, as clause 4.2.2 requires
/// whether or not it carries macroblocks; and per macroblock a choice between an intra coding, a
/// prediction from the co-located block, a prediction at a searched whole-pixel motion vector, either
/// of the last two with the loop filter of clause 3.2.3 on the prediction, and not being transmitted at
/// all. That last one is H.261's only form of "nothing changed here": the address of the next
/// transmitted macroblock steps over it (clause 4.2.3.1) and the decoder reads back what the reference
/// left there.
/// <para/>
/// <b>The temporal reference is a clock, not a frame counter.</b> Clause 3.1 fixes the source picture
/// clock at 30000/1001 Hz and clause 4.2.1.2 says TR advances by one plus every source picture not
/// transmitted since the previous coded one. When packet timestamps and a time base are supplied they
/// are mapped onto that clock relative to the first picture; otherwise a stated frame rate determines
/// the skipped source pictures. With neither, consecutive input pictures are treated as consecutive
/// H.261 source pictures. A declared rate faster than the source clock is refused because no TR
/// sequence can represent it.
/// <para/>
/// <b>What it does not write.</b> The four macroblock types carrying MQUANT (Table 2 rows 2, 4, 7 and
/// 10). The quantiser is stated once in each group's header and held across the picture, which is a
/// choice rather than a limitation: there is no rate control here for a mid-group change to serve, and
/// a fixed step is what makes the same picture code to the same bytes every time. Nor the bit-stuffing
/// codeword of clause 4.2.3.1, which exists to fill a channel this encoder is not driving, nor the
/// still image transmission of Annex D, which the decoder beside this refuses to read.
/// <para/>
/// <b>Lossy, and by construction.</b> H.261 has no lossless form at all — every coded block goes
/// through the transform and the quantiser of clause 4.2.4, and nothing but a picture already sitting
/// on that quantiser's own reconstruction grid comes back exactly. What this encoder does guarantee is
/// that its own reconstruction is the one a decoder will build: every picture is coded against what the
/// last one reconstructed to and never against what was handed in, so the error of a long run of
/// predicted pictures stays the quantiser's own and does not accumulate on top of itself.
/// <para/>
/// <b>Measured against ffmpeg.</b> Six streams written here — two of sixty frames and four of thirty, at
/// both picture formats — were decoded by ffmpeg's own H.261 decoder, which accepted every picture of
/// every one, and compared against this encoder's own reconstruction plane by plane on the 4:2:0 samples
/// rather than after a colour conversion: seventy-nine differing samples of twenty-two million, none by
/// more than one level, which is what Annex A's accuracy bound allows two conforming inverse transforms
/// to differ by. Against the pictures that went in, the peak signal-to-noise ratio runs from 31.7 dB on
/// the noisiest of the six to 48.0 dB on the flattest. The stream-by-stream numbers are in
/// <a href="https://github.com/Hawkynt/PNGCrushCS/blob/main/Hawkynt.FileFormats.Video/codec-notes.md">codec-notes.md</a>.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class H261VideoEncoder : IVideoCodecEncoder<H261VideoEncoder> {

  /// <summary>The four-character code containers name ITU-T H.261 with.</summary>
  private static readonly CodecTag _Tag = CodecTag.FromCharacters("H261");

  /// <summary>QCIF, the smaller of the two formats clause 3.1 defines.</summary>
  private static readonly (int Width, int Height) _Qcif = (176, 144);

  /// <summary>CIF, the larger.</summary>
  private static readonly (int Width, int Height) _Cif = (352, 288);

  /// <summary>How many pictures apart the intra ones are.</summary>
  /// <remarks>
  /// Twelve, which is what the codec's own era used and what makes a stream seekable at roughly
  /// half-second granularity at the frame rates it was written for. It is also what bounds the drift:
  /// the encoder codes against its own reconstruction so nothing accumulates, but a predicted picture
  /// that predicts badly stays badly predicted until the next intra one.
  /// </remarks>
  private const int _KEY_FRAME_INTERVAL = 12;

  /// <summary>The quantiser every group states, the GQUANT field being five bits wide and holding 1 to 31.</summary>
  /// <remarks>
  /// Eight, near the middle of the range. There is no rate control here at all — the quantiser is
  /// stated per group of blocks and this encoder writes the same value into every one of them — so a
  /// fixed step is the whole of the decision, and a caller wanting a particular bit rate is better
  /// served by choosing the picture format than by anything this class could do within one.
  /// </remarks>
  private const int _QUANTISER = 8;

  /// <summary>The temporal reference field is five bits (clause 4.2.1.2), so it counts modulo this.</summary>
  private const int _TEMPORAL_REFERENCE_PERIOD = 32;

  /// <summary>The source picture clock numerator fixed by clause 3.1.</summary>
  private const int _SOURCE_PICTURE_RATE_NUMERATOR = 30_000;

  /// <summary>The source picture clock denominator fixed by clause 3.1.</summary>
  private const int _SOURCE_PICTURE_RATE_DENOMINATOR = 1_001;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly bool _isCif;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _groupCount;

  private H263Frame? _reference;
  private int _pictureIndex;
  private int _sinceKeyFrame;
  private long? _firstPresentationTimestamp;
  private Int128? _lastSourcePictureNumber;
  private MediaStreamInfo? _stream;

  private H261VideoEncoder(MediaStreamInfo stream, bool isCif) {
    this._requested = stream;
    this._isCif = isCif;
    this._width = stream.Width;
    this._height = stream.Height;
    this._macroblockWidth = this._width / 16;
    this._macroblockHeight = this._height / 16;

    // Figure 6: one column of groups for QCIF, two for CIF, three macroblock rows to a group.
    this._groupCount = (isCif ? 2 : 1) * (this._macroblockHeight / 3);
  }

  public static string CodecName => "H.261 (ITU-T H.261)";

  public static CodecTag Codec => _Tag;

  /// <summary>
  /// Builds an encoder for the stream described, or refuses a geometry H.261 cannot state.
  /// </summary>
  public static H261VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("H.261 can only encode a video stream.");

    var frameRate = stream.FrameRate;
    if (_IsPositive(frameRate)
        && (Int128)frameRate.Numerator * _SOURCE_PICTURE_RATE_DENOMINATOR
          > (Int128)_SOURCE_PICTURE_RATE_NUMERATOR * frameRate.Denominator)
      throw new NotSupportedException(
        $"H.261's source coder runs at {_SOURCE_PICTURE_RATE_NUMERATOR}/{_SOURCE_PICTURE_RATE_DENOMINATOR} pictures "
        + $"per second (ITU-T H.261 clause 3.1); the requested {frameRate} frame rate is faster, so its pictures "
        + "cannot be assigned distinct temporal-reference values on the H.261 source picture clock.");

    if (stream.Width == _Qcif.Width && stream.Height == _Qcif.Height)
      return new(stream, isCif: false);

    if (stream.Width == _Cif.Width && stream.Height == _Cif.Height)
      return new(stream, isCif: true);

    throw new NotSupportedException(
      $"H.261 codes {_Qcif.Width}x{_Qcif.Height} (QCIF) and {_Cif.Width}x{_Cif.Height} (CIF) and no other picture "
      + $"size; {stream.Width}x{stream.Height} was asked for. ITU-T H.261 clause 3.1 defines exactly those two "
      + "formats and clause 4.2.1.3 names one of them in a single bit of PTYPE, so there is no syntax in this "
      + "Recommendation for stating another — a picture of this size written as one of the two would decode, and "
      + "would be the wrong picture.");
  }

  /// <summary>Codes one picture, either whole or against the one before it.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This H.261 stream is {this._width}x{this._height}; a picture of {frame.Width}x{frame.Height} arrived. "
        + "The two formats of clause 3.1 are stated by one bit of every picture header, so a stream can change "
        + "between them only by starting again, and this one is not doing that.");

    var intra = this._reference == null || this._sinceKeyFrame >= _KEY_FRAME_INTERVAL;
    var source = this._ToPlanes(frame);
    var temporalReference = this._TemporalReference(presentationTimestamp);
    var target = new H263Frame(this._macroblockWidth, this._macroblockHeight);

    var encoder = new H261PictureEncoder(
      intra, temporalReference, _QUANTISER, source, target, this._reference, this._isCif, this._groupCount);

    var bytes = encoder.Encode();
    this._reference = target;
    this._sinceKeyFrame = intra ? 1 : this._sinceKeyFrame + 1;
    ++this._pictureIndex;

    packet = new(
      this._requested.Index,
      bytes,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: intra);

    return true;
  }

  /// <summary>Nothing is ever held back: H.261 has no bidirectional prediction to reorder around.</summary>
  public IEnumerable<CodedPacket> Flush() => [];

  /// <summary>
  /// The stream as a muxer needs it: a <c>BITMAPINFOHEADER</c> naming H.261.
  /// </summary>
  /// <remarks>
  /// Unlike Microsoft's MPEG-4, an H.261 picture header states its own size — one bit choosing between
  /// the two formats — so the header here is what a container's stream description wants rather than
  /// something the bitstream would be undecodable without.
  /// </remarks>
  public MediaStreamInfo DescribeStream() {
    if (this._stream != null)
      return this._stream;

    var header = new BitmapInfoHeader(
      HeaderSize: BitmapInfoHeader.StructSize,
      Width: this._width,
      Height: this._height,
      Planes: 1,
      BitsPerPixel: 24,
      Compression: unchecked((int)_Tag.Value),
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
      Codec = _Tag,
      Handler = _Tag,
      CodecId = "V_MS/VFW/FOURCC",
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      Width = this._width,
      Height = this._height,
      BitsPerPixel = 24,
      CodecPrivateData = format,
    };
  }

  /// <summary>
  /// Maps this picture onto H.261's 30000/1001 Hz source picture clock and returns the five low bits.
  /// </summary>
  private int _TemporalReference(long? presentationTimestamp) {
    var sourcePictureNumber = this._SourcePictureNumber(presentationTimestamp);
    if (this._lastSourcePictureNumber is { } previous && sourcePictureNumber <= previous)
      throw new InvalidDataException(
        $"This H.261 picture maps to source picture {sourcePictureNumber}, not after the previously coded source "
        + $"picture {previous}. ITU-T H.261 clauses 3.1 and 4.2.1.2 require each transmitted picture to occupy a "
        + "later 30000/1001 Hz source-picture interval; reduce the frame rate or supply increasing timestamps.");

    this._lastSourcePictureNumber = sourcePictureNumber;
    return (int)(sourcePictureNumber % _TEMPORAL_REFERENCE_PERIOD);
  }

  /// <summary>
  /// The number of the H.261 source-picture interval occupied by this input picture, relative to the first one.
  /// </summary>
  private Int128 _SourcePictureNumber(long? presentationTimestamp) {
    var timeBase = this._requested.TimeBase;
    if (this._pictureIndex == 0 && presentationTimestamp is { } firstTimestamp && _IsPositive(timeBase))
      this._firstPresentationTimestamp = firstTimestamp;

    if (this._firstPresentationTimestamp is { } origin
        && presentationTimestamp is { } timestamp
        && _IsPositive(timeBase)) {
      var elapsed = (Int128)timestamp - origin;
      if (elapsed < 0)
        throw new InvalidDataException(
          $"This H.261 picture has presentation timestamp {timestamp}, before the stream's first timestamp {origin}. "
          + "H.261 has no reordered pictures, so coding order is presentation order.");

      return _ScaleToSourcePictureClock(elapsed, timeBase.Numerator, timeBase.Denominator);
    }

    var frameRate = this._requested.FrameRate;
    if (_IsPositive(frameRate))
      return _ScaleToSourcePictureClock(this._pictureIndex, frameRate.Denominator, frameRate.Numerator);

    // With no timing information at all there is no evidence that source pictures were omitted, so
    // consecutive inputs are the consecutive 29.97 Hz source pictures clause 4.2.1.2 describes.
    return this._pictureIndex;
  }

  /// <summary>Scales non-negative units of a rational number of seconds onto H.261's source clock.</summary>
  private static Int128 _ScaleToSourcePictureClock(Int128 units, long secondsNumerator, long secondsDenominator) {
    var numerator = checked(units * secondsNumerator * _SOURCE_PICTURE_RATE_NUMERATOR);
    var denominator = checked((Int128)secondsDenominator * _SOURCE_PICTURE_RATE_DENOMINATOR);

    // External container clocks are commonly coarser than 30000/1001 Hz. Assign the picture to the
    // nearest source interval rather than systematically one interval early when its timestamp was
    // rounded by such a clock; exact H.261/NTSC time bases of course divide without a remainder.
    var quotient = numerator / denominator;
    var remainder = numerator % denominator;
    return remainder >= (denominator + 1) / 2 ? checked(quotient + 1) : quotient;
  }

  private static bool _IsPositive(Rational value)
    => value.IsKnown && value.Numerator > 0 && value.Denominator > 0;

  /// <summary>
  /// The picture as the 4:2:0 planes the coding works on.
  /// </summary>
  /// <remarks>
  /// Both formats are a whole number of macroblocks in each direction, so nothing is padded and nothing
  /// is cropped — every sample of the planes is a sample of the picture. A picture already in
  /// <see cref="PixelFormat.Yuv420P8"/> is taken exactly as it stands, with no colour conversion
  /// anywhere in the path; anything else goes through the ITU-R BT.601 studio-swing convention this
  /// package's decoders display with, and it is there and only there that the rounding of a colour
  /// matrix enters.
  /// </remarks>
  private H263Frame _ToPlanes(RawImage frame) {
    var planes = RawYuvPlanes.Subsampled(frame, PixelFormat.Yuv420P8, this._width / 2, this._height / 2);
    var source = new H263Frame(this._macroblockWidth, this._macroblockHeight);
    var lumaSamples = this._width * this._height;
    var chromaSamples = lumaSamples / 4;

    Array.Copy(planes, 0, source.Luma, 0, lumaSamples);
    Array.Copy(planes, lumaSamples, source.Cb, 0, chromaSamples);
    Array.Copy(planes, lumaSamples + chromaSamples, source.Cr, 0, chromaSamples);

    return source;
  }
}
