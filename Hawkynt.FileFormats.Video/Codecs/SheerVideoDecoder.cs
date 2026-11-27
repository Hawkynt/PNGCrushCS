using System;
using System.IO;
using FileFormat.Codecs.SheerVideo;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes BitJazz SheerVideo: lossless RGB, RGBA and 4:4:4 and 4:2:2 YUV with or without alpha, at eight
/// or ten bits, each row stored raw or as Huffman-coded differences from the left or from a gradient.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/sheervideo.c</c> and <c>sheervideodata.h</c>, LGPL-2.1-or-later — the
/// row decoders transcribed mechanically; see <c>SheerVideo/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. Pictures come
/// out as <see cref="PixelFormat.Rgb24"/>, <see cref="PixelFormat.Rgba32"/>, <see cref="PixelFormat.Rgb48"/>,
/// <see cref="PixelFormat.Rgba64"/>, or planar YUV at eight or ten bits; YUV with alpha, which no planar
/// format here carries, as <see cref="PixelFormat.Rgba32"/> or <see cref="PixelFormat.Rgba64"/> through
/// BT.601 studio swing.
/// </remarks>
public sealed class SheerVideoDecoder : IVideoCodecDecoder<SheerVideoDecoder> {

  internal static readonly CodecTag Tag = CodecTag.FromCharacters("Shr0");

  private readonly int _width;
  private readonly int _height;

  private SheerVideoDecoder(int width, int height) {
    this._width = width;
    this._height = height;
  }

  public static string CodecName => "BitJazz SheerVideo";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      return false;
    var code = stream.Codec.ToString();
    return code.Length == 4 && code.StartsWith("Shr", StringComparison.Ordinal) && code[3] is >= '0' and <= '7';
  }

  public static SheerVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not SheerVideo.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException($"Stream {stream.Index} states no picture size, which SheerVideo needs.");
    return new(stream.Width, stream.Height);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    frame = ToImage(SheerFrame.Decode(packet.Data.Span, this._width, this._height), this._width, this._height);
    return true;
  }

  internal static RawImage ToImage(SheerFrame decoded, int width, int height) {
    var planes = decoded.Planes;
    var strides = decoded.Strides;
    switch (decoded.Format.PixelFormat) {
      case "RGB0" or "ARGB": {
        var alpha = decoded.Format.PixelFormat == "ARGB";
        var pixels = new byte[width * height * (alpha ? 4 : 3)];
        for (var y = 0; y < height; ++y)
          for (var x = 0; x < width; ++x) {
            var at = y * strides[0] + 4 * x;
            var o = (y * width + x) * (alpha ? 4 : 3);
            if (alpha) {
              pixels[o] = (byte)planes[0][at + 1];
              pixels[o + 1] = (byte)planes[0][at + 2];
              pixels[o + 2] = (byte)planes[0][at + 3];
              pixels[o + 3] = (byte)planes[0][at];
            } else {
              pixels[o] = (byte)planes[0][at];
              pixels[o + 1] = (byte)planes[0][at + 1];
              pixels[o + 2] = (byte)planes[0][at + 2];
            }
          }
        return new() { Width = width, Height = height, Format = alpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24, PixelData = pixels };
      }
      case "GBRP10" or "GBRAP10": {
        // Planes are green, blue, red and alpha; Rgb48 and Rgba64 hold big-endian sixteen-bit samples.
        var alpha = planes.Length == 4;
        var channels = alpha ? 4 : 3;
        var pixels = new byte[width * height * channels * 2];
        int[] order = alpha ? [2, 0, 1, 3] : [2, 0, 1];
        for (var y = 0; y < height; ++y)
          for (var x = 0; x < width; ++x)
            for (var c = 0; c < channels; ++c) {
              var v = planes[order[c]][y * strides[order[c]] + x];
              var wide = (v << 6) | (v >> 4);
              var o = ((y * width + x) * channels + c) * 2;
              pixels[o] = (byte)(wide >> 8);
              pixels[o + 1] = (byte)wide;
            }
        return new() { Width = width, Height = height, Format = alpha ? PixelFormat.Rgba64 : PixelFormat.Rgb48, PixelData = pixels };
      }
      default: {
        var format = decoded.Format.PixelFormat;
        var half = format.Contains("422");
        var ten = format.EndsWith("P10");
        var chroma = half ? (width + 1) / 2 : width;
        var size = ten ? 2 : 1;
        var data = new byte[(width + 2 * chroma) * height * size];
        var at = 0;
        for (var p = 0; p < 3; ++p) {
          var w = p == 0 ? width : chroma;
          for (var y = 0; y < height; ++y)
            for (var x = 0; x < w; ++x) {
              var v = planes[p][y * strides[p] + x];
              data[at++] = (byte)v;
              if (ten)
                data[at++] = (byte)(v >> 8);
            }
        }
        var pixelFormat = (half, ten) switch {
          (true, true) => PixelFormat.Yuv422P10,
          (true, false) => PixelFormat.Yuv422P8,
          (false, true) => PixelFormat.Yuv444P10,
          _ => PixelFormat.Yuv444P8,
        };
        var yuv = new RawImage { Width = width, Height = height, Format = pixelFormat, PixelData = data, ColorInfo = RawImageColorInfo.Bt601Limited };
        if (planes.Length < 4)
          return yuv;
        var rgb = FastRawImageConverter.Convert(yuv, ten ? PixelFormat.Rgb48 : PixelFormat.Rgb24, RawImageColorInfo.Bt601Limited).PixelData;
        var channelSize = ten ? 2 : 1;
        var rgba = new byte[width * height * 4 * channelSize];
        for (var i = 0; i < width * height; ++i) {
          rgb.AsSpan(i * 3 * channelSize, 3 * channelSize).CopyTo(rgba.AsSpan(i * 4 * channelSize));
          var a = planes[3][(i / width) * strides[3] + i % width];
          if (ten) {
            var wide = (a << 6) | (a >> 4);
            rgba[i * 8 + 6] = (byte)(wide >> 8);
            rgba[i * 8 + 7] = (byte)wide;
          } else
            rgba[i * 4 + 3] = (byte)a;
        }
        return new() { Width = width, Height = height, Format = ten ? PixelFormat.Rgba64 : PixelFormat.Rgba32, PixelData = rgba };
      }
    }
  }
}

/// <summary>
/// Encodes BitJazz SheerVideo losslessly at eight bits: 4:2:2 YUV as <c>BYRY</c>, 4:4:4 YUV as <c> YbR</c>, and
/// anything else as <c> RGB</c>, every row predicted from a gradient of its neighbours.
/// </summary>
/// <remarks>
/// This package's own; FFmpeg has no SheerVideo encoder. The first picture chooses the layout. A picture in
/// that layout comes back exactly. FFmpeg's decoder is the oracle that reads what this writes.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class SheerVideoEncoder : IVideoCodecEncoder<SheerVideoEncoder> {

  private readonly MediaStreamInfo _stream;
  private uint _layout;

  private SheerVideoEncoder(MediaStreamInfo requested) => this._stream = new() {
    Index = requested.Index,
    Kind = MediaStreamKind.Video,
    Codec = SheerVideoDecoder.Tag,
    Handler = SheerVideoDecoder.Tag,
    TimeBase = requested.TimeBase,
    FrameRate = requested.FrameRate,
    DeclaredFrameCount = requested.DeclaredFrameCount,
    Width = requested.Width,
    Height = requested.Height,
    BitsPerPixel = 24,
    Language = requested.Language,
    Name = requested.Name,
  };

  public static string CodecName => SheerVideoDecoder.CodecName;
  public static CodecTag Codec => SheerVideoDecoder.Tag;

  public static bool Accepts(MediaStreamInfo stream) => SheerVideoDecoder.Accepts(stream);

  public static SheerVideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("SheerVideo can only encode a video stream.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException($"Stream {stream.Index} states no picture size, which SheerVideo needs.");
    return new(stream);
  }

  public MediaStreamInfo DescribeStream() => this._stream;

  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);
    int width = this._stream.Width, height = this._stream.Height;
    if (frame.Width != width || frame.Height != height)
      throw new InvalidDataException($"SheerVideo geometry is fixed at {width}x{height}; received {frame.Width}x{frame.Height}.");
    if (this._layout == 0)
      this._layout = frame.Format switch {
        PixelFormat.Yuv422P8 => SheerEncoder.Byry,
        PixelFormat.Yuv444P8 => SheerEncoder.Ybr,
        _ => SheerEncoder.Rgb,
      };

    int[][] planes;
    int[] strides;
    if (this._layout == SheerEncoder.Rgb) {
      var rgb = frame.ToRgb24();
      strides = [4 * width + 16];
      planes = [new int[strides[0] * height]];
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x)
          for (var c = 0; c < 3; ++c)
            planes[0][y * strides[0] + 4 * x + c] = rgb[(y * width + x) * 3 + c];
    } else {
      var half = this._layout == SheerEncoder.Byry;
      var target = half ? PixelFormat.Yuv422P8 : PixelFormat.Yuv444P8;
      var yuv = frame.Format == target ? frame : FastRawImageConverter.Convert(frame, target, RawImageColorInfo.Bt601Limited);
      strides = [width + 16, width + 16, width + 16];
      planes = [new int[strides[0] * height], new int[strides[1] * height], new int[strides[2] * height]];
      for (var p = 0; p < 3; ++p) {
        var data = yuv.GetPlaneData(p);
        var w = p > 0 && half ? (width + 1) / 2 : width;
        for (var y = 0; y < height; ++y)
          // One sample past the edge too: an odd-width 4:2:2 row reads its last pair whole.
          for (var x = 0; x <= w; ++x)
            planes[p][y * strides[p] + x] = data[y * w + Math.Min(x, w - 1)];
      }
    }
    packet = new(this._stream.Index, SheerEncoder.Encode(this._layout, width, height, planes, strides), PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp, Duration: 1, IsKeyFrame: true);
    return true;
  }
}
