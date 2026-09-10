using System;
using FileFormat.Codecs.Vp3;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes On2 VP3.1 video as independent intra pictures.</summary>
/// <remarks>
/// VP3 is lossy by definition: spatial residuals are transformed and quantised before they are
/// entropy-coded. This encoder chooses the finest built-in quantiser and writes every picture as an
/// intra frame. The resulting packets are larger than a motion-compensated VP3 stream, but they are
/// ordinary VP3.1 packets and every packet is independently seekable.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Vp3VideoEncoder : IVideoCodecEncoder<Vp3VideoEncoder> {
  private static readonly CodecTag _Tag = CodecTag.FromCharacters("VP31");

  private readonly MediaStreamInfo _stream;
  private readonly Vp3Encoder _encoder;

  private Vp3VideoEncoder(MediaStreamInfo stream) {
    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      BitsPerPixel = 12,
      Language = stream.Language,
      Name = stream.Name,
    };
    this._encoder = new(stream.Width, stream.Height);
  }

  public static string CodecName => "On2 VP3";

  public static CodecTag Codec => _Tag;

  public static Vp3VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("On2 VP3 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"On2 VP3 needs the output dimensions before the muxer is created; {stream.Width}x{stream.Height} was supplied.");

    return new(stream);
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    var data = this._encoder.Encode(frame);
    packet = new(
      StreamIndex: this._stream.Index,
      Data: data,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      Duration: 1,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;
}
