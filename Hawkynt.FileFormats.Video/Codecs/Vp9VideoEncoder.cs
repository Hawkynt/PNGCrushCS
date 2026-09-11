using System;
using System.IO;
using FileFormat.Codecs.Vp9;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes lossless VP9 profile-0 video from native eight-bit 4:2:0 pictures.</summary>
/// <remarks>
/// Every input picture becomes an independently decodable keyframe. The writer intentionally accepts
/// only <see cref="PixelFormat.Yuv420P8"/>: silently converting RGB, alpha or another chroma layout
/// would make a writer advertised as lossless lose information before VP9 even saw it.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Vp9VideoEncoder : IVideoCodecEncoder<Vp9VideoEncoder> {

  private static readonly CodecTag _VP90 = CodecTag.FromCharacters("VP90");
  private const string _MATROSKA_CODEC_ID = "V_VP9";

  private readonly MediaStreamInfo _stream;

  private Vp9VideoEncoder(MediaStreamInfo stream) {
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException($"VP9 codes video, and stream {stream.Index} is {stream.Kind}.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"Video stream {stream.Index} states a picture size of {stream.Width}x{stream.Height}, and VP9 needs it before the first picture.");
    if (stream.Width > Vp9Constants.MAX_TILE_WIDTH_B64 * 64 || stream.Height > 65536)
      throw new NotSupportedException(
        $"This VP9 writer emits one tile and supports at most {Vp9Constants.MAX_TILE_WIDTH_B64 * 64}x65536; "
        + $"stream {stream.Index} states {stream.Width}x{stream.Height}.");

    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _VP90,
      Handler = _VP90,
      CodecId = _MATROSKA_CODEC_ID,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = 12,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "VP9 (profiles 0-3)";

  public static CodecTag Codec => _VP90;

  public static Vp9VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"The encoder describes {this._stream.Width}x{this._stream.Height} pictures, and this one is {frame.Width}x{frame.Height}.");

    var data = Vp9Encoder.Encode(frame);
    packet = new(
      StreamIndex: this._stream.Index,
      Data: data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;
}
