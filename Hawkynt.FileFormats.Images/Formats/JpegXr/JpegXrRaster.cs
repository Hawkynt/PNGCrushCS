using System;
using System.Buffers.Binary;
using System.Text.RegularExpressions;
using FileFormat.Core;
using FileFormat.JpegXr.JxrLib;

namespace FileFormat.JpegXr;

/// <summary>
/// Turns a picture in one of JPEG XR's own pixel formats into the nearest raster this library carries.
/// </summary>
/// <remarks>
/// Integer formats keep their depth: eight-bit samples become <see cref="PixelFormat.Gray8"/>,
/// <see cref="PixelFormat.Rgb24"/> or <see cref="PixelFormat.Rgba32"/>, sixteen-bit ones the 16-bit
/// formats. Fixed-point, half and float samples, and the shared-exponent RGBE format, become the
/// 32-bit float formats, scaled as jxrlib's own converters scale them: 13 fraction bits in a 16-bit
/// fixed-point sample, 24 in a 32-bit one, and an RGBE exponent biased by 136. Premultiplied formats
/// are divided back out.
/// <para/>
/// CMYK samples are ink amounts and come back as the light they leave, each colour times the
/// complement of the key. The n-channel formats state no colour meaning at all; their first three
/// channels are shown as red, green and blue, and every channel stays available in
/// <see cref="JpegXrFile.SourceSamples"/>.
/// </remarks>
internal static class JpegXrRaster {

  private enum Sample { Bit, U8, U16, Fixed16, Half, Fixed32, Float32, Packed555, Packed565, Packed101010, Rgbe }

  private enum Model { Gray, Rgb, Cmyk, Channels }

  /// <param name="Stored">Samples stored per pixel, padding included.</param>
  /// <param name="Colour">Where the colour samples sit, in the order gray, or R G B, or C M Y K.</param>
  /// <param name="Alpha">Where the alpha sample sits, or -1.</param>
  private sealed record Layout(Model Model, Sample Sample, int Stored, int[] Colour, int Alpha, bool Premultiplied);

  private static readonly Regex _Channels = new(@"^(\d+)bpp(\d)Channels(Alpha)?$", RegexOptions.CultureInvariant);

  private static Layout _LayoutOf(string name) {
    switch (name) {
      case "BlackWhite": return new(Model.Gray, Sample.Bit, 1, [0], -1, false);
      case "8bppGray": return new(Model.Gray, Sample.U8, 1, [0], -1, false);
      case "16bppGray": return new(Model.Gray, Sample.U16, 1, [0], -1, false);
      case "16bppGrayFixedPoint": return new(Model.Gray, Sample.Fixed16, 1, [0], -1, false);
      case "16bppGrayHalf": return new(Model.Gray, Sample.Half, 1, [0], -1, false);
      case "32bppGrayFixedPoint": return new(Model.Gray, Sample.Fixed32, 1, [0], -1, false);
      case "32bppGrayFloat": return new(Model.Gray, Sample.Float32, 1, [0], -1, false);

      case "24bppRGB": return new(Model.Rgb, Sample.U8, 3, [0, 1, 2], -1, false);
      case "24bppBGR": return new(Model.Rgb, Sample.U8, 3, [2, 1, 0], -1, false);
      case "32bppRGB": return new(Model.Rgb, Sample.U8, 4, [0, 1, 2], -1, false);
      case "32bppBGR": return new(Model.Rgb, Sample.U8, 4, [2, 1, 0], -1, false);
      case "48bppRGB": return new(Model.Rgb, Sample.U16, 3, [0, 1, 2], -1, false);
      case "48bppRGBFixedPoint": return new(Model.Rgb, Sample.Fixed16, 3, [0, 1, 2], -1, false);
      case "48bppRGBHalf": return new(Model.Rgb, Sample.Half, 3, [0, 1, 2], -1, false);
      case "64bppRGBFixedPoint": return new(Model.Rgb, Sample.Fixed16, 4, [0, 1, 2], -1, false);
      case "64bppRGBHalf": return new(Model.Rgb, Sample.Half, 4, [0, 1, 2], -1, false);
      case "96bppRGBFixedPoint": return new(Model.Rgb, Sample.Fixed32, 3, [0, 1, 2], -1, false);
      case "96bppRGBFloat": return new(Model.Rgb, Sample.Float32, 3, [0, 1, 2], -1, false);
      case "128bppRGBFixedPoint": return new(Model.Rgb, Sample.Fixed32, 4, [0, 1, 2], -1, false);
      case "128bppRGBFloat": return new(Model.Rgb, Sample.Float32, 4, [0, 1, 2], -1, false);

      case "32bppBGRA": return new(Model.Rgb, Sample.U8, 4, [2, 1, 0], 3, false);
      case "32bppRGBA": return new(Model.Rgb, Sample.U8, 4, [0, 1, 2], 3, false);
      case "32bppPBGRA": return new(Model.Rgb, Sample.U8, 4, [2, 1, 0], 3, true);
      case "32bppPRGBA": return new(Model.Rgb, Sample.U8, 4, [0, 1, 2], 3, true);
      case "64bppRGBA": return new(Model.Rgb, Sample.U16, 4, [0, 1, 2], 3, false);
      case "64bppPRGBA": return new(Model.Rgb, Sample.U16, 4, [0, 1, 2], 3, true);
      case "64bppRGBAFixedPoint": return new(Model.Rgb, Sample.Fixed16, 4, [0, 1, 2], 3, false);
      case "64bppRGBAHalf": return new(Model.Rgb, Sample.Half, 4, [0, 1, 2], 3, false);
      case "128bppRGBAFixedPoint": return new(Model.Rgb, Sample.Fixed32, 4, [0, 1, 2], 3, false);
      case "128bppRGBAFloat": return new(Model.Rgb, Sample.Float32, 4, [0, 1, 2], 3, false);
      case "128bppPRGBAFloat": return new(Model.Rgb, Sample.Float32, 4, [0, 1, 2], 3, true);

      case "16bppRGB555": return new(Model.Rgb, Sample.Packed555, 1, [0, 1, 2], -1, false);
      case "16bppRGB565": return new(Model.Rgb, Sample.Packed565, 1, [0, 1, 2], -1, false);
      case "32bppRGB101010": return new(Model.Rgb, Sample.Packed101010, 1, [0, 1, 2], -1, false);
      case "32bppRGBE": return new(Model.Rgb, Sample.Rgbe, 4, [0, 1, 2], -1, false);

      case "32bppCMYK": return new(Model.Cmyk, Sample.U8, 4, [0, 1, 2, 3], -1, false);
      case "64bppCMYK": return new(Model.Cmyk, Sample.U16, 4, [0, 1, 2, 3], -1, false);
      case "40bppCMYKAlpha": return new(Model.Cmyk, Sample.U8, 5, [0, 1, 2, 3], 4, false);
      case "80bppCMYKAlpha": return new(Model.Cmyk, Sample.U16, 5, [0, 1, 2, 3], 4, false);
    }

    var match = _Channels.Match(name);
    if (match.Success) {
      var channels = match.Groups[2].Value[0] - '0';
      var alpha = match.Groups[3].Success;
      var stored = channels + (alpha ? 1 : 0);
      var bits = int.Parse(match.Groups[1].Value) / stored;
      return new(Model.Channels, bits == 8 ? Sample.U8 : Sample.U16, stored, [0, 1, 2], alpha ? channels : -1, false);
    }

    throw new NotSupportedException($"JPEG XR pixel format {name} has no raster mapping.");
  }

  /// <summary>How many samples a pixel of this format carries that mean something: colour channels plus alpha.</summary>
  internal static int ComponentCount(string name) {
    var layout = _LayoutOf(name);
    var colour = layout.Model switch {
      Model.Gray => 1,
      Model.Cmyk => 4,
      Model.Channels => layout.Stored - (layout.Alpha >= 0 ? 1 : 0),
      _ => 3,
    };
    return colour + (layout.Alpha >= 0 ? 1 : 0);
  }

  internal static RawImage ToRawImage(JxrDecodedImage image) {
    var layout = _LayoutOf(image.PixelFormatName);
    var width = image.Width;
    var height = image.Height;
    var count = checked(width * height);
    var hasAlpha = layout.Alpha >= 0;

    if (layout.Model == Model.Gray) {
      switch (layout.Sample) {
        case Sample.Bit: {
          var gray = new byte[count];
          for (var y = 0; y < height; ++y)
          for (var x = 0; x < width; ++x)
            gray[y * width + x] = (byte)(((image.Pixels[y * image.Stride + (x >> 3)] >> (7 - (x & 7))) & 1) * 255);
          return new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = gray };
        }
        case Sample.U8: {
          var gray = new byte[count];
          for (var y = 0; y < height; ++y)
            Array.Copy(image.Pixels, y * image.Stride, gray, y * width, width);
          return new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = gray };
        }
        case Sample.U16: {
          var gray = new byte[count * 2];
          for (var i = 0; i < count; ++i)
            BinaryPrimitives.WriteUInt16BigEndian(gray.AsSpan(i * 2), (ushort)_Integer(image, layout, i, 0));
          return new() { Width = width, Height = height, Format = PixelFormat.Gray16, PixelData = gray };
        }
        default: {
          var gray = new byte[count * 4];
          for (var i = 0; i < count; ++i)
            BinaryPrimitives.WriteSingleLittleEndian(gray.AsSpan(i * 4), _Real(image, layout, i, 0));
          return new() { Width = width, Height = height, Format = PixelFormat.GrayF32, PixelData = gray };
        }
      }
    }

    var parts = hasAlpha ? 4 : 3;
    switch (layout.Sample) {
      case Sample.Packed555 or Sample.Packed565: {
        var rgb = new byte[count * 3];
        for (var i = 0; i < count; ++i) {
          int p = BinaryPrimitives.ReadUInt16LittleEndian(image.Pixels.AsSpan(_Offset(image, i, 2)));
          int r, g, b;
          if (layout.Sample == Sample.Packed555) {
            r = (p >> 10) & 31; g = (p >> 5) & 31; b = p & 31;
            g = (g << 3) | (g >> 2);
          } else {
            r = (p >> 11) & 31; g = (p >> 5) & 63; b = p & 31;
            g = (g << 2) | (g >> 4);
          }

          rgb[i * 3] = (byte)((r << 3) | (r >> 2));
          rgb[i * 3 + 1] = (byte)g;
          rgb[i * 3 + 2] = (byte)((b << 3) | (b >> 2));
        }

        return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
      }
      case Sample.Packed101010: {
        var rgb = new byte[count * 6];
        for (var i = 0; i < count; ++i) {
          var p = BinaryPrimitives.ReadUInt32LittleEndian(image.Pixels.AsSpan(_Offset(image, i, 4)));
          for (var c = 0; c < 3; ++c) {
            var v = (int)(p >> (20 - 10 * c)) & 1023;
            BinaryPrimitives.WriteUInt16BigEndian(rgb.AsSpan(i * 6 + c * 2), (ushort)((v << 6) | (v >> 4)));
          }
        }

        return new() { Width = width, Height = height, Format = PixelFormat.Rgb48, PixelData = rgb };
      }
      case Sample.U8 or Sample.U16: {
        var deep = layout.Sample == Sample.U16;
        var maximum = deep ? 65535 : 255;
        var bytes = deep ? 2 : 1;
        var data = new byte[count * parts * bytes];
        Span<int> colour = stackalloc int[3];
        for (var i = 0; i < count; ++i) {
          var a = hasAlpha ? _Integer(image, layout, i, layout.Alpha) : maximum;
          if (layout.Model == Model.Cmyk) {
            var k = maximum - _Integer(image, layout, i, layout.Colour[3]);
            for (var c = 0; c < 3; ++c)
              colour[c] = (int)(((long)(maximum - _Integer(image, layout, i, layout.Colour[c])) * k + maximum / 2) / maximum);
          } else
            for (var c = 0; c < 3; ++c)
              colour[c] = _Integer(image, layout, i, layout.Colour[c]);

          if (layout.Premultiplied && a > 0 && a < maximum)
            for (var c = 0; c < 3; ++c)
              colour[c] = (int)Math.Min(maximum, ((long)colour[c] * maximum + a / 2) / a);

          for (var c = 0; c < parts; ++c) {
            var value = c < 3 ? colour[c] : a;
            var at = (i * parts + c) * bytes;
            if (deep)
              BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(at), (ushort)value);
            else
              data[at] = (byte)value;
          }
        }

        var format = deep ? hasAlpha ? PixelFormat.Rgba64 : PixelFormat.Rgb48 : hasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24;
        return new() { Width = width, Height = height, Format = format, PixelData = data };
      }
      default: {
        var data = new byte[count * parts * 4];
        for (var i = 0; i < count; ++i) {
          var a = hasAlpha ? _Real(image, layout, i, layout.Alpha) : 1f;
          for (var c = 0; c < parts; ++c) {
            var value = c < 3 ? _Real(image, layout, i, layout.Colour[c]) : a;
            if (c < 3 && layout.Premultiplied && a > 0)
              value /= a;
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan((i * parts + c) * 4), value);
          }
        }

        return new() { Width = width, Height = height, Format = hasAlpha ? PixelFormat.RgbaF32 : PixelFormat.RgbF32, PixelData = data };
      }
    }
  }

  private static int _Offset(JxrDecodedImage image, int pixel, int bytesPerPixel)
    => (pixel / image.Width) * image.Stride + (pixel % image.Width) * bytesPerPixel;

  private static int _Integer(JxrDecodedImage image, Layout layout, int pixel, int sample) {
    var bytes = layout.Sample == Sample.U16 ? 2 : 1;
    var at = _Offset(image, pixel, layout.Stored * bytes) + sample * bytes;
    return bytes == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(image.Pixels.AsSpan(at)) : image.Pixels[at];
  }

  private static float _Real(JxrDecodedImage image, Layout layout, int pixel, int sample) {
    var pixels = image.Pixels.AsSpan();
    switch (layout.Sample) {
      case Sample.U8:
      case Sample.U16:
        return _Integer(image, layout, pixel, sample) / (layout.Sample == Sample.U8 ? 255f : 65535f);
      case Sample.Fixed16:
        return BinaryPrimitives.ReadInt16LittleEndian(pixels[(_Offset(image, pixel, layout.Stored * 2) + sample * 2)..]) / 8192f;
      case Sample.Half:
        return (float)BinaryPrimitives.ReadHalfLittleEndian(pixels[(_Offset(image, pixel, layout.Stored * 2) + sample * 2)..]);
      case Sample.Fixed32:
        return BinaryPrimitives.ReadInt32LittleEndian(pixels[(_Offset(image, pixel, layout.Stored * 4) + sample * 4)..]) / 16777216f;
      case Sample.Float32:
        return BinaryPrimitives.ReadSingleLittleEndian(pixels[(_Offset(image, pixel, layout.Stored * 4) + sample * 4)..]);
      case Sample.Rgbe: {
        var at = _Offset(image, pixel, 4);
        var exponent = pixels[at + 3];
        return exponent == 0 ? 0f : MathF.ScaleB(pixels[at + sample], exponent - 136);
      }
      default:
        throw new NotSupportedException($"JPEG XR sample kind {layout.Sample} has no real value.");
    }
  }
}
