using System;
using System.IO;
using FileFormat.Codecs.Tscc2;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes TechSmith Screen Codec 2: three full-size planes in sixteen-by-eight macroblocks, each either
/// left as it was, filled, stored raw, or coded as four-by-four transformed blocks.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/tscc2.c</c> and <c>tscc2data.h</c>, LGPL-2.1-or-later; see
/// <c>Tscc2/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. A frame of type 0 repeats the last and, as in FFmpeg, yields
/// no picture. The transform stores each sample's low eight bits without clamping, as FFmpeg does.
/// Pictures come out as <see cref="PixelFormat.Yuv444P8"/>.
/// </remarks>
public sealed class Tscc2VideoDecoder : IVideoCodecDecoder<Tscc2VideoDecoder> {

  internal static readonly CodecTag Tag = CodecTag.FromCharacters("tsc2");

  private readonly Tscc2FrameDecoder _decoder;
  private readonly int _width;
  private readonly int _height;

  private Tscc2VideoDecoder(int width, int height) {
    this._decoder = new(width, height);
    this._width = width;
    this._height = height;
  }

  public static string CodecName => "TechSmith Screen Codec 2";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(Tag);
  }

  public static Tscc2VideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not TechSmith Screen Codec 2.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException($"Stream {stream.Index} states no picture size, which TSCC2 needs.");
    return new(stream.Width, stream.Height);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    if (!this._decoder.Decode(packet.Data.Span)) {
      frame = null!;
      return false;
    }
    int width = this._width, height = this._height, stride = this._decoder.Stride;
    var pixels = new byte[width * height * 3];
    for (var plane = 0; plane < 3; ++plane)
      for (var y = 0; y < height; ++y)
        this._decoder.Planes[plane].AsSpan(y * stride, width).CopyTo(pixels.AsSpan(plane * width * height + y * width));
    frame = new() { Width = width, Height = height, Format = PixelFormat.Yuv444P8, PixelData = pixels, ColorInfo = RawImageColorInfo.Bt601Limited };
    return true;
  }
}

/// <summary>
/// Encodes TechSmith Screen Codec 2 losslessly: an unchanged macroblock skipped, and each plane of the rest
/// a fill, the transform where it reproduces the samples exactly, or raw samples, whichever is smallest.
/// </summary>
/// <remarks>
/// This package's own; FFmpeg has no TSCC2 encoder. A <see cref="PixelFormat.Yuv444P8"/> picture comes
/// back exactly; any other is taken to it through BT.601 studio swing first. The transform is tried at
/// quantiser 2, the finest, and kept only where its coefficients reproduce every sample of the block.
/// FFmpeg's decoder is the oracle that reads what this writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Tscc2VideoEncoder : IVideoCodecEncoder<Tscc2VideoEncoder> {

  private readonly MediaStreamInfo _stream;
  private readonly Tscc2Encoder _encoder;
  private bool _started;

  private Tscc2VideoEncoder(MediaStreamInfo requested) {
    this._stream = new() {
      Index = requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = Tscc2VideoDecoder.Tag,
      Handler = Tscc2VideoDecoder.Tag,
      TimeBase = requested.TimeBase,
      FrameRate = requested.FrameRate,
      DeclaredFrameCount = requested.DeclaredFrameCount,
      Width = requested.Width,
      Height = requested.Height,
      BitsPerPixel = 24,
      Language = requested.Language,
      Name = requested.Name,
    };
    this._encoder = new(requested.Width, requested.Height);
  }

  public static string CodecName => Tscc2VideoDecoder.CodecName;
  public static CodecTag Codec => Tscc2VideoDecoder.Tag;

  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(Tscc2VideoDecoder.Tag);

  public static Tscc2VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("TSCC2 can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException($"Stream {stream.Index} states no picture size, which TSCC2 needs.");
    return new(stream);
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._stream.Width || frame.Height != this._stream.Height)
      throw new InvalidDataException($"TSCC2 geometry is fixed at {this._stream.Width}x{this._stream.Height}; received {frame.Width}x{frame.Height}.");
    var planar = frame.Format == PixelFormat.Yuv444P8 ? frame : FastRawImageConverter.Convert(frame, PixelFormat.Yuv444P8, RawImageColorInfo.Bt601Limited);
    var data = this._encoder.Encode(planar.GetPlaneData(0), planar.GetPlaneData(1), planar.GetPlaneData(2));
    // Only the first frame stands alone: every later one skips what has not changed.
    var key = !this._started;
    this._started = true;
    packet = new(this._stream.Index, data, PresentationTimestamp: presentationTimestamp, DecodeTimestamp: presentationTimestamp, Duration: 1, IsKeyFrame: key);
    return true;
  }
}
