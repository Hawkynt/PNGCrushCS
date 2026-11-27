using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Codecs.Lagarith;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Decodes Lagarith (<c>LAGS</c>): arithmetic-coded RGB24, RGBA, YUY2 and YV12 pictures, the
/// unaligned RGB24 form, and the solid grey, colour and RGBA pictures; every plane coding — range
/// coded with or without zero runs, zero runs alone, stored, solid — and all three predictors.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/lagarith.c</c> and <c>lagarithrac.c</c>, LGPL-2.1-or-later;
/// see <c>Lagarith/THIRD-PARTY-NOTICE.FFmpeg.txt</c>. RGB pictures come out as
/// <see cref="PixelFormat.Rgb24"/> or <see cref="PixelFormat.Rgba32"/>, YUY2 as
/// <see cref="PixelFormat.Yuv422P8"/> and YV12 as <see cref="PixelFormat.Yuv420P8"/>. The uncompressed,
/// obsolete arithmetic RGB and reduced-resolution frame types are refused by name, as FFmpeg refuses
/// them; Lagarith's own encoders stopped writing the second before 1.1.0. An empty packet is a dropped
/// frame and gives no picture.
/// </remarks>
public sealed class LagarithVideoDecoder : IVideoCodecDecoder<LagarithVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("LAGS");

  private readonly int _width;
  private readonly int _height;
  private readonly bool _hasAlpha;
  private readonly LagarithPlaneCodec _planes = new();

  private LagarithVideoDecoder(MediaStreamInfo stream) {
    this._width = stream.Width;
    this._height = stream.Height;
    this._hasAlpha = stream.BitsPerPixel != 24;
  }

  public static string CodecName => "Lagarith lossless";

  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  public static LagarithVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (!Accepts(stream))
      throw new NotSupportedException($"Stream {stream.Index} is not Lagarith.");
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new NotSupportedException(
        $"Stream {stream.Index} states a size of {stream.Width}x{stream.Height}; Lagarith carries none of its own.");
    return new(stream);
  }

  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var data = packet.Data.ToArray();
    if (data.Length == 0) {
      frame = null!;
      return false;
    }

    frame = this.Decode(data);
    return true;
  }

  internal RawImage Decode(byte[] buffer) {
    // FFmpeg reads the two offsets whatever the frame type; a short packet reads as zeroes there.
    var data = buffer.Length >= 16 ? buffer : [.. buffer, .. new byte[16 - buffer.Length]];
    var type = (LagarithFrameType)data[0];
    var offsetGu = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(1));
    var offsetBv = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(5));
    var end = buffer.Length;
    int width = this._width, height = this._height;

    switch (type) {
      case LagarithFrameType.SolidGray:
        return this._hasAlpha
          ? _Solid(width, height, data[1], data[1], data[1], data[1], alpha: true)
          : _Solid(width, height, data[1], data[1], data[1], 0, alpha: false);
      case LagarithFrameType.SolidColor:
        return _Solid(width, height, data[3], data[2], data[1], 255, this._hasAlpha);
      case LagarithFrameType.SolidRgba:
        return _Solid(width, height, data[3], data[2], data[1], data[4], alpha: true);

      case LagarithFrameType.ArithmeticRgb24:
      case LagarithFrameType.UnalignedRgb24:
      case LagarithFrameType.ArithmeticRgba: {
        var alpha = type == LagarithFrameType.ArithmeticRgba;
        var offsetRy = alpha ? 13u : 9u;
        var offsetA = alpha ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(9)) : 0u;
        foreach (var offset in (ReadOnlySpan<uint>)[offsetBv, offsetGu, offsetRy, alpha ? offsetA : offsetRy])
          if (offset >= end)
            throw new InvalidDataException($"A Lagarith frame states a plane at offset {offset} of a {end}-byte packet.");

        var size = width * height;
        var first = new byte[size];
        var green = new byte[size];
        var third = new byte[size];
        this._planes.Decode(buffer, (int)offsetBv, end, first, width, height, LagarithPrediction.Rgb);
        this._planes.Decode(buffer, (int)offsetGu, end, green, width, height, LagarithPrediction.Rgb);
        this._planes.Decode(buffer, (int)offsetRy, end, third, width, height, LagarithPrediction.Rgb);
        byte[]? alphaPlane = null;
        if (alpha) {
          alphaPlane = new byte[size];
          this._planes.Decode(buffer, (int)offsetA, end, alphaPlane, width, height, LagarithPrediction.Rgb);
        }

        // The other two planes are coded as differences from green, and the rows run bottom-up.
        var channels = alpha ? 4 : 3;
        var pixels = new byte[size * channels];
        for (var row = 0; row < height; ++row) {
          var source = row * width;
          var target = (height - 1 - row) * width * channels;
          for (var x = 0; x < width; ++x) {
            var g = green[source + x];
            var at = target + x * channels;
            pixels[at] = (byte)(first[source + x] + g);
            pixels[at + 1] = g;
            pixels[at + 2] = (byte)(third[source + x] + g);
            if (alphaPlane != null)
              pixels[at + 3] = alphaPlane[source + x];
          }
        }

        return new() { Width = width, Height = height, Format = alpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24, PixelData = pixels };
      }

      case LagarithFrameType.ArithmeticYuy2:
      case LagarithFrameType.ArithmeticYv12: {
        var yv12 = type == LagarithFrameType.ArithmeticYv12;
        if (9 >= end || offsetGu >= end || offsetBv >= end)
          throw new InvalidDataException($"A Lagarith frame states a plane outside its {end}-byte packet.");

        var chromaWidth = (width + 1) / 2;
        var chromaHeight = yv12 ? (height + 1) / 2 : height;
        var luma = width * height;
        var chroma = chromaWidth * chromaHeight;
        var pixels = new byte[luma + 2 * chroma];
        var y = new byte[luma];
        var u = new byte[chroma];
        var v = new byte[chroma];
        var lumaPrediction = yv12 ? LagarithPrediction.Yv12 : LagarithPrediction.Yuy2Luma;
        var chromaPrediction = yv12 ? LagarithPrediction.Yv12 : LagarithPrediction.Yuy2Chroma;
        this._planes.Decode(buffer, 9, end, y, width, height, lumaPrediction);
        // YV12 stores V at the first offset and U at the second; YUY2 the other way round.
        this._planes.Decode(buffer, (int)offsetGu, end, yv12 ? v : u, chromaWidth, chromaHeight, chromaPrediction);
        this._planes.Decode(buffer, (int)offsetBv, end, yv12 ? u : v, chromaWidth, chromaHeight, chromaPrediction);
        y.CopyTo(pixels, 0);
        u.CopyTo(pixels, luma);
        v.CopyTo(pixels, luma + chroma);
        return new() { Width = width, Height = height, Format = yv12 ? PixelFormat.Yuv420P8 : PixelFormat.Yuv422P8, PixelData = pixels };
      }

      default:
        throw new NotSupportedException(
          $"This Lagarith frame is of type {(int)type} ({type}). FFmpeg reads none of the uncompressed, obsolete "
          + "arithmetic RGB and reduced-resolution types, and nothing has been found that does to check a decoder against.");
    }
  }

  private static RawImage _Solid(int width, int height, byte r, byte g, byte b, byte a, bool alpha) {
    var channels = alpha ? 4 : 3;
    var pixels = new byte[width * height * channels];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * channels] = r;
      pixels[i * channels + 1] = g;
      pixels[i * channels + 2] = b;
      if (alpha)
        pixels[i * channels + 3] = a;
    }
    return new() { Width = width, Height = height, Format = alpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24, PixelData = pixels };
  }
}
