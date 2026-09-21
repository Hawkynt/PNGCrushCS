using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.DnxHd;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes progressive 8-bit 4:2:2 Avid DNxHD / SMPTE VC-3.</summary>
/// <remarks>
/// DNxHD is intra-frame by design: every packet emitted here is a complete independently decodable
/// frame and therefore a key frame. There are no P/B pictures, motion vectors, forward references or
/// backward references to maintain.
/// <para/>
/// This writer covers the classic fixed-raster HD profile that can be expressed by the library's
/// current stream model and eight-bit <see cref="RawImage"/> formats. DNxHR's variable-raster
/// profiles and interlaced output need stream-level choices this API does not presently expose and
/// are not silently invented.
/// </remarks>
public sealed class DnxHdVideoEncoder : IVideoCodecEncoder<DnxHdVideoEncoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("AVdn");
  private static readonly CodecTag _Alias = CodecTag.FromCharacters("AVd1");

  private readonly MediaStreamInfo _requested;
  private readonly DnxHdProfile _profile;
  private readonly DnxHdFrameEncoder _frameEncoder;
  private MediaStreamInfo? _stream;

  private DnxHdVideoEncoder(MediaStreamInfo stream, DnxHdProfile profile) {
    this._requested = stream;
    this._profile = profile;
    this._frameEncoder = new(profile);
  }

  public static string CodecName => "Avid DNxHD / DNxHR (SMPTE VC-3)";

  public static CodecTag Codec => _Tag;

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      return false;

    if (!stream.Codec.EqualsIgnoringCase(_Tag) && !stream.Codec.EqualsIgnoringCase(_Alias))
      return false;

    return DnxHdProfile.SelectProgressive8Bit422(stream.Width, stream.Height) is not null;
  }

  public static DnxHdVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("DNxHD can only encode a video stream.");

    if (!stream.Codec.EqualsIgnoringCase(_Tag) && !stream.Codec.EqualsIgnoringCase(_Alias))
      throw new NotSupportedException(
        $"This encoder writes classic DNxHD under AVdn/AVd1; stream codec {stream.Codec} asks for another bitstream.");

    var profile = DnxHdProfile.SelectProgressive8Bit422(stream.Width, stream.Height)
      ?? throw new NotSupportedException(
        $"This DNxHD encoder writes the classic progressive 8-bit 4:2:2 rasters 1920x1080, 1440x1080, 1280x720 and 960x720; {stream.Width}x{stream.Height} was requested.");

    return new(stream, profile);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._profile.Width || frame.Height != this._profile.Height)
      throw new InvalidDataException(
        $"This DNxHD stream is {this._profile.Width}x{this._profile.Height}; a picture of {frame.Width}x{frame.Height} arrived.");

    var planes = RawYuvPlanes.Subsampled(
      frame,
      PixelFormat.Yuv422P8,
      this._profile.Width / 2,
      this._profile.Height);
    var data = this._frameEncoder.Encode(planes);

    packet = new(
      this._requested.Index,
      data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public IEnumerable<CodedPacket> Flush() => [];

  public MediaStreamInfo DescribeStream()
    => this._stream ??= new() {
      Index = this._requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      CodecId = "V_DNXHD",
      TimeBase = this._requested.TimeBase,
      FrameRate = this._requested.FrameRate,
      Width = this._profile.Width,
      Height = this._profile.Height,
      BitsPerPixel = 16,
      Name = this._requested.Name,
      Language = this._requested.Language,
    };
}
