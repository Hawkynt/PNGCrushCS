using System;
using System.IO;
using FileFormat.Codecs.SpeedHq;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes NewTek SpeedHQ in the form the requested FourCC names: <c>SHQ0</c>/<c>SHQ2</c>/<c>SHQ4</c>
/// (4:2:0, 4:2:2, 4:4:4), <c>SHQ1</c>/<c>SHQ3</c>/<c>SHQ5</c> (the same with run-length alpha) and
/// <c>SHQ7</c>/<c>SHQ9</c> (4:2:2 and 4:4:4 with DCT alpha); <c>SHQ2</c> when none is asked for.
/// </summary>
/// <remarks>
/// See <see cref="SpeedHqEncoder"/>. SpeedHQ is an intra-only DCT codec, lossy by construction; this
/// writes it at quantiser scale 4 of the format's 1-100, where FFmpeg's encoder writes 2 to 62 at its
/// own default. FFmpeg's decoder is the oracle that reads what it writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class SpeedHqVideoEncoder : IVideoCodecEncoder<SpeedHqVideoEncoder> {

  private const int _SCALE = 4;

  private static readonly CodecTag _Default = CodecTag.FromCharacters("SHQ2");

  private readonly MediaStreamInfo _stream;
  private readonly SpeedHqEncoder _encoder;
  private readonly SpeedHqSubsampling _subsampling;
  private readonly SpeedHqAlpha _alpha;

  private SpeedHqVideoEncoder(MediaStreamInfo requested, CodecTag tag, SpeedHqSubsampling subsampling, SpeedHqAlpha alpha) {
    this._subsampling = subsampling;
    this._alpha = alpha;
    this._encoder = new(requested.Width, requested.Height, subsampling, alpha, _SCALE);
    this._stream = new() {
      Index = requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = tag,
      Handler = tag,
      TimeBase = requested.TimeBase,
      FrameRate = requested.FrameRate,
      DeclaredFrameCount = requested.DeclaredFrameCount,
      Width = requested.Width,
      Height = requested.Height,
      BitsPerPixel = alpha == SpeedHqAlpha.None ? 24 : 32,
      Language = requested.Language,
      Name = requested.Name,
    };
  }

  public static string CodecName => "NewTek SpeedHQ";
  public static CodecTag Codec => _Default;

  public static bool Accepts(MediaStreamInfo stream) => SpeedHqVideoDecoder.Form(stream.Codec) != null;

  public static SpeedHqVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("NewTek SpeedHQ can only encode a video stream.");
    if (stream.Width < 8 || stream.Width % 8 != 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"SpeedHQ codes rows in eight-sample columns, so a picture must be a positive multiple of eight wide; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    var tag = SpeedHqVideoDecoder.Form(stream.Codec) != null ? stream.Codec : _Default;
    var (subsampling, alpha) = SpeedHqVideoDecoder.Form(tag)!.Value;
    return new(stream, tag, subsampling, alpha);
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException($"SpeedHQ geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");

    var format = this._subsampling switch {
      SpeedHqSubsampling.Yuv420 => PixelFormat.Yuv420P8,
      SpeedHqSubsampling.Yuv422 => PixelFormat.Yuv422P8,
      _ => PixelFormat.Yuv444P8,
    };
    var yuv = frame.Format == format ? frame : FastRawImageConverter.Convert(frame, format);
    var (chromaWidth, chromaHeight) = yuv.GetPlaneDimensions(1);

    byte[]? alpha = null;
    if (this._alpha != SpeedHqAlpha.None) {
      var rgba = frame.ToRgba32();
      alpha = new byte[frame.Width * frame.Height];
      for (var i = 0; i < alpha.Length; ++i)
        alpha[i] = rgba[i * 4 + 3];
    }

    var data = this._encoder.Encode(
      yuv.GetPlaneData(0).ToArray(), yuv.GetPlaneData(1).ToArray(), yuv.GetPlaneData(2).ToArray(), chromaWidth, chromaHeight, alpha);
    packet = new(this._stream.Index, data, PresentationTimestamp: presentationTimestamp, DecodeTimestamp: presentationTimestamp,
      Duration: 1, IsKeyFrame: true);
    return true;
  }
}
