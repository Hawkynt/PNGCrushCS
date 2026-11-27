using System;
using System.IO;
using FileFormat.Codecs.Hqx;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Canopus HQX: twelve-bit 4:2:2 or 4:4:4 YUV, with or without an alpha plane, as eight-by-eight
/// transformed blocks in sixteen slices.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/hqx.c</c> and <c>hqxdsp.c</c>, with the code tables of
/// <c>hqxvlc.h</c> and <c>hq_common.c</c>, LGPL-2.1-or-later; see <c>Hqx/THIRD-PARTY-NOTICE.FFmpeg.txt</c>.
/// Samples are widened from twelve bits to sixteen as FFmpeg widens them. Pictures without alpha come out
/// as <see cref="PixelFormat.Yuv422P16"/> or <see cref="PixelFormat.Yuv444P16"/>; with alpha, which no
/// planar format here carries, as <see cref="PixelFormat.Rgba64"/> through BT.601 studio swing.
/// </remarks>
public sealed class HqxVideoDecoder : IVideoCodecDecoder<HqxVideoDecoder> {

  internal static readonly CodecTag Tag = CodecTag.FromCharacters("CHQX");

  private HqxVideoDecoder() { }

  public static string CodecName => "Canopus HQX";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(Tag);
  }

  public static HqxVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not Canopus HQX.");
    return new();
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    frame = ToImage(HqxFrame.Decode(packet.Data.Span));
    return true;
  }

  internal static RawImage ToImage(HqxFrame decoded) {
    int width = decoded.Width, height = decoded.Height;
    var (_, half) = HqxShared.Planes(decoded.Format);
    var chromaWidth = half ? (width + 1) / 2 : width;
    var pixels = new byte[(width + 2 * chromaWidth) * height * 2];
    var at = 0;
    for (var p = 0; p < 3; ++p) {
      var w = p == 0 ? width : chromaWidth;
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < w; ++x, at += 2) {
          var v = decoded.Planes[p][y * decoded.Strides[p] + x];
          pixels[at] = (byte)v;
          pixels[at + 1] = (byte)(v >> 8);
        }
    }
    var yuv = new RawImage {
      Width = width, Height = height, Format = half ? PixelFormat.Yuv422P16 : PixelFormat.Yuv444P16, PixelData = pixels,
      ColorInfo = RawImageColorInfo.Bt601Limited,
    };
    if (decoded.Planes.Length < 4)
      return yuv;

    var rgb = FastRawImageConverter.Convert(yuv, PixelFormat.Rgb48, RawImageColorInfo.Bt601Limited).PixelData;
    var rgba = new byte[width * height * 8];
    for (var i = 0; i < width * height; ++i) {
      rgb.AsSpan(i * 6, 6).CopyTo(rgba.AsSpan(i * 8));
      // Rgba64 holds each sample big-endian, as PNG does.
      var a = decoded.Planes[3][(i / width) * decoded.Strides[3] + i % width];
      rgba[i * 8 + 6] = (byte)(a >> 8);
      rgba[i * 8 + 7] = (byte)a;
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgba64, PixelData = rgba };
  }
}

/// <summary>
/// Encodes Canopus HQX: 4:2:2 or 4:4:4 YUV at twelve bits, with an alpha plane for a picture that has one.
/// </summary>
/// <remarks>
/// This package's own; FFmpeg has no HQX encoder. The first picture chooses the layout: 4:2:2 YUV stays
/// 4:2:2, 4:4:4 YUV and RGB go as 4:4:4, and RGBA as 4:4:4 with alpha. Progressive, 11-bit DC precision,
/// and each block at the finest of the quantisers 2, 4, 8 and 16 that holds it. Lossy, because HQX is.
/// FFmpeg's decoder is the oracle that reads what this writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class HqxVideoEncoder : IVideoCodecEncoder<HqxVideoEncoder> {

  private readonly MediaStreamInfo _requested;
  private HqxEncoder? _encoder;
  private HqxFormat _format;

  private HqxVideoEncoder(MediaStreamInfo requested) => this._requested = requested;

  public static string CodecName => HqxVideoDecoder.CodecName;
  public static CodecTag Codec => HqxVideoDecoder.Tag;

  public static bool Accepts(MediaStreamInfo stream) => stream.Codec.EqualsIgnoringCase(HqxVideoDecoder.Tag);

  public static HqxVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("HQX can only encode a video stream.");
    if (stream.Width is <= 0 or > 65535 || stream.Height is <= 0 or > 65535)
      throw new NotSupportedException($"HQX states its picture size in sixteen bits; stream {stream.Index} states {stream.Width}x{stream.Height}.");
    return new(stream);
  }

  /// <summary>An encoder fixed to one layout, the 4:2:2-with-alpha one included, which no input format chooses.</summary>
  internal static HqxVideoEncoder CreateFor(MediaStreamInfo stream, HqxFormat format) {
    var encoder = Create(stream);
    encoder._format = format;
    encoder._encoder = new(stream.Width, stream.Height, format);
    return encoder;
  }

  public MediaStreamInfo DescribeStream() {
    var requested = this._requested;
    return new() {
      Index = requested.Index,
      Kind = MediaStreamKind.Video,
      Codec = HqxVideoDecoder.Tag,
      Handler = HqxVideoDecoder.Tag,
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

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    if (frame.Width != this._requested.Width || frame.Height != this._requested.Height)
      throw new InvalidDataException($"HQX geometry is fixed at {this._requested.Width}x{this._requested.Height}; received {frame.Width}x{frame.Height}.");
    if (this._encoder == null) {
      this._format = frame.Format switch {
        PixelFormat.Yuv422P8 or PixelFormat.Yuv422P10 or PixelFormat.Yuv422P12 or PixelFormat.Yuv422P16 => HqxFormat.Yuv422,
        PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Argb32 or PixelFormat.Rgba64 => HqxFormat.Yuv444Alpha,
        _ => HqxFormat.Yuv444,
      };
      this._encoder = new(frame.Width, frame.Height, this._format);
    }
    packet = new(this._requested.Index, this._encoder.Encode(_Planes(frame, this._format)), PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp, Duration: 1, IsKeyFrame: true);
    return true;
  }

  /// <summary>The picture as twelve-bit planes in the layout's chroma, with alpha where it has one.</summary>
  private static ushort[][] _Planes(RawImage frame, HqxFormat format) {
    var (count, half) = HqxShared.Planes(format);
    var target = half ? PixelFormat.Yuv422P16 : PixelFormat.Yuv444P16;
    RawImage yuv;
    if (frame.Format == target)
      yuv = frame;
    else if (frame.Format is PixelFormat.Yuv422P8 or PixelFormat.Yuv444P8 or PixelFormat.Yuv422P10 or PixelFormat.Yuv444P10 or PixelFormat.Yuv422P12 or PixelFormat.Yuv444P12
             && (frame.Format is PixelFormat.Yuv422P8 or PixelFormat.Yuv422P10 or PixelFormat.Yuv422P12) == half)
      yuv = _Widen(frame, target);
    else
      yuv = FastRawImageConverter.Convert(frame, target, RawImageColorInfo.Bt601Limited);

    var planes = new ushort[count][];
    for (var p = 0; p < 3; ++p) {
      var data = yuv.GetPlaneData(p);
      planes[p] = new ushort[data.Length / 2];
      for (var i = 0; i < planes[p].Length; ++i)
        planes[p][i] = (ushort)((data[i * 2] | data[i * 2 + 1] << 8) >> 4);
    }
    if (count == 4) {
      var alpha = new ushort[frame.Width * frame.Height];
      if (frame.Format == PixelFormat.Rgba64)
        for (var i = 0; i < alpha.Length; ++i)
          alpha[i] = (ushort)((frame.PixelData[i * 8 + 6] << 8 | frame.PixelData[i * 8 + 7]) >> 4);
      else {
        var rgba = frame.Format is PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Argb32 ? frame.ToRgba32() : null;
        for (var i = 0; i < alpha.Length; ++i)
          alpha[i] = rgba == null ? (ushort)0xFFF : (ushort)((rgba[i * 4 + 3] << 4) | (rgba[i * 4 + 3] >> 4));
      }
      planes[3] = alpha;
    }
    return planes;
  }

  /// <summary>8-, 10- and 12-bit samples widened to sixteen bits in the same chroma layout.</summary>
  private static RawImage _Widen(RawImage frame, PixelFormat target) {
    var bits = frame.Format switch {
      PixelFormat.Yuv422P8 or PixelFormat.Yuv444P8 => 8,
      PixelFormat.Yuv422P10 or PixelFormat.Yuv444P10 => 10,
      _ => 12,
    };
    var source = frame.PixelData;
    var samples = bits == 8 ? source.Length : source.Length / 2;
    var widened = new byte[samples * 2];
    for (var i = 0; i < samples; ++i) {
      var v = bits == 8 ? source[i] : source[i * 2] | source[i * 2 + 1] << 8;
      var w = (v << (16 - bits)) | (v >> (2 * bits - 16 > 0 ? 2 * bits - 16 : 0));
      widened[i * 2] = (byte)w;
      widened[i * 2 + 1] = (byte)(w >> 8);
    }
    return new() { Width = frame.Width, Height = frame.Height, Format = target, PixelData = widened, ColorInfo = frame.ColorInfo };
  }
}
