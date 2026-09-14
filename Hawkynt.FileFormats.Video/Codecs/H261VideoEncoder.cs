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
/// <see cref="H261VideoDecoder"/>, including Annex D still-image transmission.
/// </summary>
/// <remarks>
/// <b>QCIF and CIF coded pictures, and nothing else.</b> Clause 3.1 defines two coded formats and PTYPE
/// names one of them in a single bit; there is no source-format field with five choices and no extended
/// header. Annex D obtains a larger still image without adding another coded format: a QCIF stream may
/// temporarily carry a 352x288 still, and a CIF stream a 704x576 still, through
/// <see cref="TryEncodeStillImage"/> as four decimated sub-images in the stream's ordinary coded size.
/// <para/>
/// <b>What it writes.</b> For ordinary motion video, an intra picture every
/// <see cref="_KEY_FRAME_INTERVAL"/> frames and predicted pictures between them; every group of blocks
/// with its own header, as clause 4.2.2 requires whether or not it carries macroblocks; and per
/// macroblock a choice between an intra coding, a prediction from the co-located block, a prediction at
/// a searched whole-pixel motion vector, either of the last two with the loop filter of clause 3.2.3 on
/// the prediction, and not being transmitted at all. H.261 has no picture-level P/B distinction and no
/// backward reference: prediction is always from the previously reconstructed picture.
/// <para/>
/// For Annex D a double-width, double-height still is separated according to Figure D.1 into sub-images
/// 0, 1, 2 and 3, each coded intra with HI_RES zero and TR equal to its sub-image number. The four
/// picture syntaxes share one bit writer, so no byte padding is inserted between them. The last decoded
/// sub-image remains the reference when motion video resumes, exactly as Annex D.3 requires.
/// <para/>
/// <b>The ordinary temporal reference is a clock, not a frame counter.</b> Clause 3.1 fixes the source
/// picture clock at 30000/1001 Hz and clause 4.2.1.2 says TR advances by one plus every source picture
/// not transmitted since the previous coded one. When packet timestamps and a time base are supplied
/// they are mapped onto that clock relative to the first ordinary picture; otherwise a stated frame
/// rate determines the skipped source pictures. Annex D owns TR's low two bits while HI_RES is zero and
/// therefore does not advance this ordinary-video source-picture counter.
/// <para/>
/// <b>What it deliberately does not choose.</b> The four macroblock types carrying MQUANT (Table 2 rows
/// 2, 4, 7 and 10). The quantiser is stated once in each group's header and held across the picture,
/// which is an encoder policy rather than missing syntax support: the decoder accepts MQUANT and there
/// is no rate-control loop here for a mid-group change to serve. The bit-stuffing codeword of clause
/// 4.2.3.1 is likewise unnecessary when not driving a fixed-rate channel.
/// <para/>
/// <b>Lossy, and by construction.</b> H.261 has no lossless form at all — every coded block goes through
/// the transform and the quantiser of clause 4.2.4. The encoder always predicts from its own previous
/// reconstruction rather than from the original source, so encoder and decoder references stay locked.
/// <para/>
/// <b>Measured against ffmpeg.</b> The ordinary-video path was measured over six streams, both formats,
/// with every picture accepted by ffmpeg and reconstructed samples agreeing within Annex A's transform
/// tolerance. FFmpeg does not assemble Annex D high-resolution stills; that path is therefore pinned by
/// the Recommendation's explicit Figure D.1 pattern and syntax tests rather than pretending ffmpeg is
/// an oracle for a feature it ignores.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class H261VideoEncoder : IVideoCodecEncoder<H261VideoEncoder> {

  /// <summary>The four-character code containers name ITU-T H.261 with.</summary>
  private static readonly CodecTag _Tag = CodecTag.FromCharacters("H261");

  /// <summary>QCIF, the smaller of the two coded formats clause 3.1 defines.</summary>
  private static readonly (int Width, int Height) _Qcif = (176, 144);

  /// <summary>CIF, the larger coded format.</summary>
  private static readonly (int Width, int Height) _Cif = (352, 288);

  /// <summary>Figure D.1 sample parity for sub-images 0, 1, 2 and 3 respectively.</summary>
  private static readonly (int X, int Y)[] _StillImageOffsets = [
    (0, 0),
    (0, 1),
    (1, 1),
    (1, 0),
  ];

  /// <summary>How many ordinary pictures apart the periodic intra pictures are.</summary>
  private const int _KEY_FRAME_INTERVAL = 12;

  /// <summary>The fixed GQUANT value this encoder writes.</summary>
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

  /// <summary>Builds an encoder for one of H.261's two coded stream geometries.</summary>
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
      $"H.261 codes {_Qcif.Width}x{_Qcif.Height} (QCIF) and {_Cif.Width}x{_Cif.Height} (CIF) and no other coded "
      + $"picture size; {stream.Width}x{stream.Height} was asked for. Annex D's larger still pictures are carried "
      + "inside a QCIF or CIF stream as four ordinary-size sub-images rather than as another stream geometry.");
  }

  /// <summary>Codes one ordinary motion picture in the stream's QCIF/CIF geometry.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This H.261 stream is {this._width}x{this._height}; a motion picture of {frame.Width}x{frame.Height} arrived. "
        + $"Use {nameof(TryEncodeStillImage)} for Annex D's {2 * this._width}x{2 * this._height} still-image mode.");

    return this._TryEncodeMotionPicture(frame, presentationTimestamp, out packet);
  }

  /// <summary>
  /// Codes one Annex D still image at twice the stream width and height as sub-images 0, 1, 2 and 3.
  /// </summary>
  public bool TryEncodeStillImage(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != 2 * this._width || frame.Height != 2 * this._height)
      throw new InvalidDataException(
        $"Annex D still-image transmission on this {this._width}x{this._height} H.261 stream requires exactly "
        + $"{2 * this._width}x{2 * this._height} samples; a {frame.Width}x{frame.Height} picture arrived.");

    return this._TryEncodeStillImage(frame, presentationTimestamp, out packet);
  }

  private bool _TryEncodeMotionPicture(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
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

  /// <summary>Writes one Annex D still as four unpadded, sequential intra sub-pictures.</summary>
  private bool _TryEncodeStillImage(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    var subImages = this._ToStillImageSubImages(frame);
    var writer = new H261BitWriter();
    var reference = this._reference;

    for (var index = 0; index < subImages.Length; ++index) {
      var target = new H263Frame(this._macroblockWidth, this._macroblockHeight);
      var encoder = new H261PictureEncoder(
        intra: true,
        temporalReference: index,
        quantiser: _QUANTISER,
        source: subImages[index],
        target: target,
        reference: reference,
        isCif: this._isCif,
        groupCount: this._groupCount,
        isStillImage: true,
        writer: writer);

      encoder.EncodeIntoBitstream();
      reference = target;
    }

    // Annex D.3: the previous frame remains the reference regardless of whether it was motion video or
    // a still-image sub-picture. Sub-image 3 is the last frame actually coded.
    this._reference = reference;
    this._sinceKeyFrame = 1;

    packet = new(
      this._requested.Index,
      writer.ToArray(),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);

    return true;
  }

  /// <summary>Nothing is ever held back: H.261 has no bidirectional prediction to reorder around.</summary>
  public IEnumerable<CodedPacket> Flush() => [];

  /// <summary>The stream description used by containers: the coded QCIF/CIF size, never Annex D's display size.</summary>
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

  /// <summary>Maps an ordinary motion picture onto H.261's 30000/1001 Hz source picture clock.</summary>
  private int _TemporalReference(long? presentationTimestamp) {
    var sourcePictureNumber = this._SourcePictureNumber(presentationTimestamp);
    if (this._lastSourcePictureNumber is { } previous && sourcePictureNumber <= previous)
      throw new InvalidDataException(
        $"This H.261 picture maps to source picture {sourcePictureNumber}, not after the previously coded source "
        + $"picture {previous}. ITU-T H.261 clauses 3.1 and 4.2.1.2 require each transmitted motion picture to "
        + "occupy a later 30000/1001 Hz source-picture interval; reduce the frame rate or supply increasing timestamps.");

    this._lastSourcePictureNumber = sourcePictureNumber;
    return (int)(sourcePictureNumber % _TEMPORAL_REFERENCE_PERIOD);
  }

  /// <summary>The ordinary source-picture interval number, relative to the first ordinary picture.</summary>
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

    return this._pictureIndex;
  }

  /// <summary>Scales non-negative units of a rational number of seconds onto H.261's source clock.</summary>
  private static Int128 _ScaleToSourcePictureClock(Int128 units, long secondsNumerator, long secondsDenominator) {
    var numerator = checked(units * secondsNumerator * _SOURCE_PICTURE_RATE_NUMERATOR);
    var denominator = checked((Int128)secondsDenominator * _SOURCE_PICTURE_RATE_DENOMINATOR);
    var quotient = numerator / denominator;
    var remainder = numerator % denominator;
    return remainder >= (denominator + 1) / 2 ? checked(quotient + 1) : quotient;
  }

  private static bool _IsPositive(Rational value)
    => value.IsKnown && value.Numerator > 0 && value.Denominator > 0;

  /// <summary>The ordinary picture as the 4:2:0 planes the coding works on.</summary>
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

  /// <summary>Separates a double-size Annex D still into Figure D.1's four coded-size 4:2:0 frames.</summary>
  private H263Frame[] _ToStillImageSubImages(RawImage frame) {
    var stillWidth = 2 * this._width;
    var stillHeight = 2 * this._height;

    // A high-resolution 4:2:0 still has chroma dimensions width/2,height/2 = the ordinary luma size.
    var planes = RawYuvPlanes.Subsampled(frame, PixelFormat.Yuv420P8, this._width, this._height);
    var lumaSamples = stillWidth * stillHeight;
    var chromaSamples = this._width * this._height;
    var luma = planes.AsSpan(0, lumaSamples);
    var cb = planes.AsSpan(lumaSamples, chromaSamples);
    var cr = planes.AsSpan(lumaSamples + chromaSamples, chromaSamples);

    var result = new H263Frame[4];
    for (var index = 0; index < result.Length; ++index) {
      var subImage = result[index] = new(this._macroblockWidth, this._macroblockHeight);
      var (offsetX, offsetY) = _StillImageOffsets[index];

      _DecimatePlane(luma, stillWidth, subImage.Luma, subImage.LumaWidth, offsetX, offsetY);
      _DecimatePlane(cb, this._width, subImage.Cb, subImage.ChromaWidth, offsetX, offsetY);
      _DecimatePlane(cr, this._width, subImage.Cr, subImage.ChromaWidth, offsetX, offsetY);
    }

    return result;
  }

  private static void _DecimatePlane(
    ReadOnlySpan<byte> source, int sourceWidth, Span<byte> target, int targetWidth, int offsetX, int offsetY) {
    var targetHeight = target.Length / targetWidth;
    for (var y = 0; y < targetHeight; ++y) {
      var sourceRow = (2 * y + offsetY) * sourceWidth + offsetX;
      var targetRow = y * targetWidth;
      for (var x = 0; x < targetWidth; ++x)
        target[targetRow + x] = source[sourceRow + 2 * x];
    }
  }
}
