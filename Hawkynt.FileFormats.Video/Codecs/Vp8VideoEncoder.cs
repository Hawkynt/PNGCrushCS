using System;
using System.IO;
using FileFormat.Core;
using FileFormat.WebP.Vp8;

namespace FileFormat.Codecs;

/// <summary>Encodes VP8 video as independent RFC 6386 key frames.</summary>
/// <remarks>
/// VP8 permits a stream to consist entirely of key frames. Each input picture is therefore encoded
/// independently through the image package's existing pure-managed VP8 encoder, and the video layer
/// only supplies stream identity and packet timing. This deliberately spends compression ratio to
/// avoid maintaining a second transform, prediction and entropy encoder beside the WebP one.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Vp8VideoEncoder : IVideoCodecEncoder<Vp8VideoEncoder> {

  private const int _QUALITY = 75;
  private const string _MATROSKA_CODEC_ID = "V_VP8";
  private static readonly CodecTag _codec = CodecTag.FromCharacters("VP80");

  private readonly MediaStreamInfo _stream;

  private Vp8VideoEncoder(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("VP8 can only encode a video stream.");
    if (stream.Width is < 1 or > 16383 || stream.Height is < 1 or > 16383)
      throw new NotSupportedException(
        $"VP8 key-frame dimensions are limited to 1..16383 pixels; {stream.Width}x{stream.Height} was supplied.");

    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _codec,
      Handler = _codec,
      CodecId = _MATROSKA_CODEC_ID,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "VP8 (RFC 6386)";

  public static CodecTag Codec => _codec;

  public static Vp8VideoEncoder Create(MediaStreamInfo stream) => new(stream);

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"The encoder was created for {this._stream.Width}x{this._stream.Height} pictures, but received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        $"A {frame.Width}x{frame.Height} {frame.Format} picture needs {frame.MinimumPixelDataLength} bytes and carries {frame.PixelData.Length}.");

    var data = Vp8Encoder.Encode(frame, _QUALITY, tokenPartitions: 1, threadTokenPartitions: false);
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
