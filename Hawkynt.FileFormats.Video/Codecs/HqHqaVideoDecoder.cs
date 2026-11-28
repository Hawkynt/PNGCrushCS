using System;
using System.IO;
using FileFormat.Codecs.HqHqa;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Canopus HQ and HQA: eight-bit 4:2:2 YUV as eight-by-eight transformed blocks in slices — HQ at
/// one of its profiles' fixed sizes, HQA at any size and with an alpha plane.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/hq_hqa.c</c> and <c>hq_hqadsp.c</c>, with the tables of
/// <c>hq_hqadata.h</c> and <c>hq_common.c</c>, LGPL-2.1-or-later; see <c>HqHqa/THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// HQ pictures come out as <see cref="PixelFormat.Yuv422P8"/>; HQA's, whose alpha no planar format here
/// carries, as <see cref="PixelFormat.Rgba32"/> through BT.601 studio swing.
/// </remarks>
public sealed class HqHqaVideoDecoder : IVideoCodecDecoder<HqHqaVideoDecoder> {

  internal static readonly CodecTag Tag = CodecTag.FromCharacters("CUVC");

  private HqHqaVideoDecoder() { }

  public static string CodecName => "Canopus HQ/HQA";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(Tag);
  }

  public static HqHqaVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not Canopus HQ or HQA.");
    return new();
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    frame = ToImage(HqFrame.Decode(packet.Data.Span));
    return true;
  }

  internal static RawImage ToImage(HqFrame decoded) {
    int width = decoded.Width, height = decoded.Height, chroma = (width + 1) / 2;
    var pixels = new byte[(width + 2 * chroma) * height];
    var at = 0;
    for (var p = 0; p < 3; ++p) {
      var w = p == 0 ? width : chroma;
      for (var y = 0; y < height; ++y) {
        decoded.Planes[p].AsSpan(y * decoded.Strides[p], w).CopyTo(pixels.AsSpan(at));
        at += w;
      }
    }
    var yuv = new RawImage { Width = width, Height = height, Format = PixelFormat.Yuv422P8, PixelData = pixels, ColorInfo = RawImageColorInfo.Bt601Limited };
    if (!decoded.Alpha)
      return yuv;
    var rgb = FastRawImageConverter.Convert(yuv, PixelFormat.Rgb24, RawImageColorInfo.Bt601Limited).PixelData;
    var rgba = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      rgba[i * 4] = rgb[i * 3];
      rgba[i * 4 + 1] = rgb[i * 3 + 1];
      rgba[i * 4 + 2] = rgb[i * 3 + 2];
      rgba[i * 4 + 3] = decoded.Planes[3][(i / width) * decoded.Strides[3] + i % width];
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = rgba };
  }
}

/// <summary>
/// Encodes Canopus HQ and HQA: HQ for a picture of one of HQ's profile sizes, HQA, with alpha, for any other
/// or for a picture with alpha of its own.
/// </summary>
/// <remarks>
/// This package's own; FFmpeg has no HQ or HQA encoder. The first picture decides. Progressive, quantiser
/// group 0, and each block at the finest of its four matrices that holds it. Lossy, because HQ is. FFmpeg's
/// decoder is the oracle that reads what this writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class HqHqaVideoEncoder : IVideoCodecEncoder<HqHqaVideoEncoder> {

  private readonly MediaStreamInfo _requested;
  private HqEncoder? _encoder;

  private HqHqaVideoEncoder(MediaStreamInfo requested) => this._requested = requested;

  public static string CodecName => HqHqaVideoDecoder.CodecName;
  public static CodecTag Codec => HqHqaVideoDecoder.Tag;

  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(HqHqaVideoDecoder.Tag);

  public static HqHqaVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("HQ can only encode a video stream.");
    if (stream.Width is <= 0 or > 65535 || stream.Height is <= 0 or > 65535)
      throw new NotSupportedException($"HQA states its picture size in sixteen bits; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    return new(stream);
  }

  public MediaStreamInfo DescribeStream() {
    var requested = this._requested;
    return new() {
      Index = requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = HqHqaVideoDecoder.Tag,
      Handler = HqHqaVideoDecoder.Tag,
      TimeBase = requested.TimeBase,
      FrameRate = requested.FrameRate,
      DeclaredFrameCount = requested.DeclaredFrameCount,
      Width = requested.Width,
      Height = requested.Height,
      BitsPerPixel = 16,
      Language = requested.Language,
      Name = requested.Name,
    };
  }

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._requested.Width || frame.Height != this._requested.Height)
      throw new InvalidDataException($"HQ geometry is fixed at {this._requested.Width}x{this._requested.Height}; received {frame.Width}x{frame.Height}.");
    var hasAlpha = frame.Format is PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Argb32 or PixelFormat.Rgba64;
    this._encoder ??= new(frame.Width, frame.Height, hasAlpha ? -1 : HqEncoder.ProfileOf(frame.Width, frame.Height));

    var yuv = frame.Format == PixelFormat.Yuv422P8 ? frame : FastRawImageConverter.Convert(frame, PixelFormat.Yuv422P8, RawImageColorInfo.Bt601Limited);
    byte[][] planes = [yuv.GetPlaneData(0).ToArray(), yuv.GetPlaneData(1).ToArray(), yuv.GetPlaneData(2).ToArray(), []];
    if (this._encoder.Hqa) {
      var alpha = new byte[frame.Width * frame.Height];
      if (hasAlpha) {
        var rgba = frame.ToRgba32();
        for (var i = 0; i < alpha.Length; ++i)
          alpha[i] = rgba[i * 4 + 3];
      } else
        alpha.AsSpan().Fill(255);
      planes[3] = alpha;
    }
    packet = new(this._requested.Index, this._encoder.Encode(planes), PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp, Duration: 1, IsKeyFrame: true);
    return true;
  }
}
