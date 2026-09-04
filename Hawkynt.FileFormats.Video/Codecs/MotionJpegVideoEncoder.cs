using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Codecs;

/// <summary>Encodes independent video frames as baseline JPEG packets.</summary>
/// <remarks>
/// Motion JPEG has no inter-picture prediction: every input picture becomes one complete JPEG and
/// every output packet is therefore a key frame. The JPEG bytes are produced by the image package's
/// existing pure-managed writer, keeping the video codec as a thin adapter rather than a second JPEG
/// implementation.
/// </remarks>
public sealed class MotionJpegVideoEncoder : IVideoCodecEncoder<MotionJpegVideoEncoder> {

  private static readonly CodecTag _codec = CodecTag.FromCharacters("MJPG");
  private readonly MediaStreamInfo _stream;

  private MotionJpegVideoEncoder(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Motion JPEG can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"A Motion JPEG encoder needs the output dimensions before the muxer is created; {stream.Width}x{stream.Height} was supplied.");

    this._stream = new() {
      Index = stream.Index,
      Kind = MediaStreamKind.Video,
      Codec = _codec,
      Handler = _codec,
      CodecId = "V_MJPEG",
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
      DeclaredFrameCount = stream.DeclaredFrameCount,
      Width = stream.Width,
      Height = stream.Height,
      Language = stream.Language,
      Name = stream.Name,
    };
  }

  public static string CodecName => "Motion JPEG";

  public static CodecTag Codec => _codec;

  public static MotionJpegVideoEncoder Create(MediaStreamInfo stream) => new(stream);

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException(
        $"The encoder was created for {this._stream.Width}x{this._stream.Height} pictures, but received {frame.Width}x{frame.Height}.");
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException(
        $"A {frame.Width}x{frame.Height} {frame.Format} picture needs {frame.MinimumPixelDataLength} bytes and carries {frame.PixelData.Length}.");

    var jpeg = FormatIO.Encode<JpegFile>(frame);
    packet = new(
      StreamIndex: this._stream.Index,
      Data: jpeg,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: true);
    return true;
  }

  public MediaStreamInfo DescribeStream() => this._stream;
}
