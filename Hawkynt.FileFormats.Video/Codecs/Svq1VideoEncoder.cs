using System;
using System.IO;
using FileFormat.Codecs.Svq1;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Encodes Sorenson Video 1 (<c>SVQ1</c>) from any picture, through YUV 4:1:0.</summary>
/// <remarks>
/// See <see cref="Svq1Encoder"/>: FFmpeg's vector-quantisation search converted, this package's own
/// motion search around it, FFmpeg's decoder as the oracle. SVQ1 is lossy by construction — every
/// vector is a mean plus up to six codebook entries — and chrominance is coded at a quarter of the size
/// each way. A key frame every twelfth picture; between them each macroblock is skipped, moved by a
/// half-sample vector or intra-coded, by distortion plus bits.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Svq1VideoEncoder : IVideoCodecEncoder<Svq1VideoEncoder> {

  private const int _KEYFRAME_INTERVAL = 12;
  private const int _LAMBDA = 8;

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("SVQ1");

  private readonly MediaStreamInfo _stream;
  private readonly Svq1Encoder _encoder;
  private int _pictureNumber;

  private Svq1VideoEncoder(MediaStreamInfo requested) {
    this._encoder = new(requested.Width, requested.Height, _LAMBDA);
    this._stream = new() {
      Index = requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = _Tag,
      Handler = _Tag,
      TimeBase = requested.TimeBase,
      FrameRate = requested.FrameRate,
      DeclaredFrameCount = requested.DeclaredFrameCount,
      Width = requested.Width,
      Height = requested.Height,
      BitsPerPixel = 24,
      Language = requested.Language,
      Name = requested.Name,
    };
  }

  public static string CodecName => "Sorenson Video 1";
  public static CodecTag Codec => _Tag;

  public static Svq1VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("Sorenson Video 1 can only encode a video stream.");
    if (stream.Width < 4 || stream.Height < 4)
      throw new NotSupportedException($"SVQ1 needs a picture at least 4x4; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    if (stream.Width >= 4096 || stream.Height >= 4096)
      throw new NotSupportedException($"SVQ1 states its size in twelve bits; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    return new(stream);
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException($"SVQ1 geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");

    var yuv = frame.Format == PixelFormat.Yuv410P8 ? frame : FastRawImageConverter.Convert(frame, PixelFormat.Yuv410P8);
    var (chromaWidth, chromaHeight) = yuv.GetPlaneDimensions(1);
    var keyFrame = this._pictureNumber % _KEYFRAME_INTERVAL == 0;
    var data = this._encoder.Encode(
      yuv.GetPlaneData(0).ToArray(), yuv.GetPlaneData(1).ToArray(), yuv.GetPlaneData(2).ToArray(), chromaWidth, chromaHeight, keyFrame);
    ++this._pictureNumber;

    packet = new(this._stream.Index, data, PresentationTimestamp: presentationTimestamp, DecodeTimestamp: presentationTimestamp,
      Duration: 1, IsKeyFrame: keyFrame);
    return true;
  }
}
