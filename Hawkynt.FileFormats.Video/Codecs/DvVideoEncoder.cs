using System;
using System.IO;
using FileFormat.Codecs.Dv;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes the implemented DV family profiles: IEC 61834-2/SMPTE 314M standard definition and
/// SMPTE 370M DVCPRO HD.
/// </summary>
/// <remarks>
/// DV is intra-frame only: every packet is independently decodable and therefore a key frame. The
/// raster fixes the recording system and frame rate. Standard-definition pictures use the existing
/// six-block encoder; 720/1080 DVCPRO HD pictures use the separate eight-block DV100 encoder.
/// <para/>
/// Both block layers are cross-checked against FFmpeg's LGPL-2.1-or-later DV encoder. Attribution and
/// licence details are in <c>Codecs/Dv/THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class DvVideoEncoder : IVideoCodecEncoder<DvVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("dvsd");

  private readonly MediaStreamInfo _stream;
  private readonly int _width;
  private readonly int _height;
  private readonly RawImageColorInfo _colour;
  private readonly DvSegmentEncoder.Scratch _scratch = new();
  private readonly Dv100SegmentEncoder.Scratch _dv100Scratch = new();

  private DvProfile? _profile;
  private DvGeometry.Segment[] _segments = [];

  private DvVideoEncoder(MediaStreamInfo stream, Rational frameRate) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._colour = stream.Height > 576 ? RawImageColorInfo.Bt709Limited : RawImageColorInfo.Bt601Limited;
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      TimeBase = stream.TimeBase,
      FrameRate = frameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = 24,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "DV (IEC 61834 / SMPTE 314M / SMPTE 370M)";

  public static CodecTag Codec => _Tag;

  public static DvVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("DV can only encode a video stream.");

    var rateProfile = DvProfile.ForRaster(stream.Width, stream.Height)
      ?? throw new NotSupportedException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}. The implemented DV "
        + "profiles are 720x480, 720x576, DVCPRO HD 1280x1080, 1440x1080 and 960x720; a DV frame is a "
        + "fixed DIF layout, so an arbitrary raster cannot be padded into one.");

    var frameRate = new Rational(rateProfile.FrameRateNumerator, rateProfile.FrameRateDenominator);
    if (stream.FrameRate.IsKnown
        && (stream.FrameRate.Numerator <= 0
            || stream.FrameRate.Denominator <= 0
            || !_SameRate(stream.FrameRate, frameRate)))
      throw new NotSupportedException(
        $"The {rateProfile.Name} profile is fixed at {frameRate} frames/s; stream {stream.Index} requests "
        + $"{stream.FrameRate} frames/s.");

    return new(stream, frameRate);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"DV geometry is fixed at {this._width}x{this._height}; received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    packet = new(
      this._stream.Index,
      this.EncodePlanes(this._Planes(frame, out var sampling), sampling),
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  internal byte[] EncodePlanes(DvPlanes planes, DvSampling sampling) {
    var profile = DvProfile.ForPicture(planes.Width, planes.Height, sampling)
      ?? throw new NotSupportedException(
        $"No implemented DV profile codes a {planes.Width}x{planes.Height} picture at {_SamplingName(sampling)}.");

    if (this._profile != null && this._profile != profile)
      throw new NotSupportedException(
        $"This stream began as {this._profile.Name} and this frame would be written as {profile.Name}. The two are "
        + "different fixed DIF layouts and cannot share one stream description.");

    if (this._profile == null) {
      this._segments = DvGeometry.Segments(profile);
      this._profile = profile;
    }

    var frame = new byte[profile.FrameSize];
    DvFrameLayout.Format(frame, profile);

    if (profile.IsDv100) {
      foreach (var segment in this._segments)
        Dv100SegmentEncoder.Encode(frame, profile, segment, planes, this._dv100Scratch);
    } else {
      foreach (var segment in this._segments)
        DvSegmentEncoder.Encode(frame, profile, segment, planes, this._scratch);
    }

    return frame;
  }

  private DvPlanes _Planes(RawImage frame, out DvSampling sampling) {
    sampling = frame.Format switch {
      PixelFormat.Yuv422P8 => DvSampling.FourTwoTwo,
      PixelFormat.Yuv420P8 when this._height == 576 => DvSampling.FourTwoZero,
      _ when this._height > 576 => DvSampling.FourTwoTwo,
      _ => this._height == 576 ? DvSampling.FourTwoZero : DvSampling.FourOneOne,
    };

    if (sampling == DvSampling.FourOneOne) {
      var (luma411, cb411, cr411) = Yuv411Planes.FromImage(frame);
      return new() {
        Width = this._width,
        Height = this._height,
        ChromaWidth = this._width / 4,
        ChromaHeight = this._height,
        Luma = luma411,
        Cb = cb411,
        Cr = cr411,
      };
    }

    var target = sampling == DvSampling.FourTwoTwo ? PixelFormat.Yuv422P8 : PixelFormat.Yuv420P8;
    var source = frame.Format == target
      ? frame
      : FastRawImageConverter.Convert(frame, target, this._colour);
    var (chromaWidth, chromaHeight) = source.GetPlaneDimensions(1);
    return new() {
      Width = this._width,
      Height = this._height,
      ChromaWidth = chromaWidth,
      ChromaHeight = chromaHeight,
      Luma = source.GetPlaneData(0).ToArray(),
      Cb = source.GetPlaneData(1).ToArray(),
      Cr = source.GetPlaneData(2).ToArray(),
    };
  }

  private static bool _SameRate(Rational left, Rational right)
    => (Int128)left.Numerator * right.Denominator == (Int128)right.Numerator * left.Denominator;

  private static string _SamplingName(DvSampling sampling) => sampling switch {
    DvSampling.FourOneOne => "4:1:1",
    DvSampling.FourTwoZero => "4:2:0",
    _ => "4:2:2",
  };
}
