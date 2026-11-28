using System;
using System.Collections.Generic;
using FileFormat.Jpeg;
using FileFormat.Jpeg2000;
using FileFormat.Pdf.Document;

namespace FileFormat.Pdf.Rendering;

/// <summary>A decoded image XObject: its colour, and where it has one, its alpha.</summary>
internal sealed class PdfRasterImage {

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>Three bytes a pixel of display RGB, or null for a stencil mask.</summary>
  public byte[]? Rgb { get; init; }

  /// <summary>
  /// How opaque each pixel is: the stencil of an image mask, a colour-key mask, or a soft mask.
  /// Its own size, because /Mask and /SMask images need not match the image they mask.
  /// </summary>
  public byte[]? Alpha { get; init; }

  public int AlphaWidth { get; init; }

  public int AlphaHeight { get; init; }

  /// <summary>Whether this is a stencil: paint the fill colour wherever <see cref="Alpha"/> says.</summary>
  public bool IsStencil { get; init; }
}

/// <summary>
/// Decodes image XObjects and inline images (PDF 32000-1 §8.9) to display RGB plus alpha.
/// </summary>
/// <remarks>
/// Samples are unpacked at any of the permitted depths, mapped through <c>/Decode</c>, and turned to
/// RGB by the colour space, with a lookup table where the space and depth make that cheap. The
/// image filters reuse the codecs this package already has — its managed JPEG decoder for DCT, its
/// JPEG 2000 decoder for JPX — with the PDF-specific parts handled here: the Adobe colour
/// transform marker and <c>/ColorTransform</c> for JPEG, YCCK, and the Decode array a JPEG's CMYK
/// planes are read through.
/// </remarks>
internal static class PdfImageDecoder {

  /// <summary>The largest image this will decode, in pixels, so a forged size cannot exhaust memory.</summary>
  private const long _MaxPixels = 1L << 27;

  /// <summary>Decodes an image from its dictionary and encoded bytes.</summary>
  /// <param name="inline">Whether this is an inline image, whose keys may be abbreviated.</param>
  public static PdfRasterImage Decode(PdfDocument document, PdfDict dict, byte[] encoded, PdfDict? resources, bool inline, bool forSoftMask = false, string? intent = null) {
    var width = document.Integer(dict, inline ? _Key(dict, "W", "Width") : "Width");
    var height = document.Integer(dict, inline ? _Key(dict, "H", "Height") : "Height");
    if (width <= 0 || height <= 0)
      throw new NotSupportedException($"An image of {width}x{height} pixels has nothing to draw.");
    if ((long)width * height > _MaxPixels)
      throw new NotSupportedException($"An image of {width}x{height} pixels is larger than this will decode.");

    var isMask = document.Get(dict, inline ? _Key(dict, "IM", "ImageMask") : "ImageMask") is true;
    var bpc = isMask ? 1 : document.Integer(dict, inline ? _Key(dict, "BPC", "BitsPerComponent") : "BitsPerComponent", 0);
    var decodeArray = document.Numbers(dict.Raw(inline ? _Key(dict, "D", "Decode") : "Decode"));

    var bytes = PdfFilters.Decode(document, dict, encoded, out var imageFilter, out var imageParms, inline);

    // The colour space; an image filter can supply it when the dictionary does not.
    var csValue = dict.Raw(inline ? _Key(dict, "CS", "ColorSpace") : "ColorSpace");
    PdfColorModel? space = null;
    if (!isMask && csValue != null)
      space = (inline ? _InlineSpace(document, csValue, resources) : PdfColorModel.Parse(document, csValue, resources)).ForIntent(intent);

    int components;
    switch (imageFilter) {
      case "DCTDecode": {
        var planes = _DecodeJpeg(document, bytes, imageParms, out var jpegWidth, out var jpegHeight);
        components = planes.Length;
        bytes = _Interleave(planes, jpegWidth, jpegHeight, width, height);
        bpc = 8;
        space ??= components switch { 1 => PdfColorModel.DeviceGray, 4 => PdfColorModel.DeviceCmyk, _ => PdfColorModel.DeviceRgb };
        break;
      }
      case "JPXDecode": {
        var jpx = Jpeg2000Reader.FromBytes(bytes);
        components = jpx.ComponentCount;
        bytes = _Resize(jpx.PixelData, components, jpx.Width, jpx.Height, width, height);
        bpc = 8;
        if (space == null || space.Components != components)
          space = components switch { 1 => PdfColorModel.DeviceGray, 4 => PdfColorModel.DeviceCmyk, _ => PdfColorModel.DeviceRgb };
        // A JPX image's own bit depth is what /Decode is measured against, but the decoder has
        // already scaled it to eight bits.
        break;
      }
      case "CCITTFaxDecode":
        bytes = PdfBilevelFilters.DecodeCcitt(document, bytes, imageParms, width, height);
        bpc = 1;
        components = 1;
        break;
      case "JBIG2Decode":
        bytes = PdfBilevelFilters.DecodeJbig2(document, bytes, imageParms, width, height);
        bpc = 1;
        components = 1;
        break;
      default:
        components = isMask ? 1 : space?.Components ?? throw new NotSupportedException("An image has no /ColorSpace.");
        break;
    }

    if (isMask)
      return _Stencil(bytes, width, height, decodeArray);

    space ??= PdfColorModel.DeviceGray;
    if (bpc is not (1 or 2 or 4 or 8 or 16))
      throw new NotSupportedException($"An image with {bpc} bits per component is not supported.");
    if (space.IsPattern)
      throw new NotSupportedException("An image in the Pattern colour space is not valid.");

    var rgb = new byte[width * height * 3];
    var raw = _Unpack(bytes, width, height, components, bpc);
    _ToRgb(space, raw, width * height, components, bpc, decodeArray, rgb);

    // Masks.
    byte[]? alpha = null;
    int alphaWidth = width, alphaHeight = height;
    if (!forSoftMask && !inline && document.Stream(dict.Raw("SMask")) is { } smaskStream) {
      var soft = Decode(document, smaskStream.Dictionary, smaskStream.Data, resources, false, true);
      alpha = _Gray(soft);
      alphaWidth = soft.Width;
      alphaHeight = soft.Height;
      if (document.Numbers(smaskStream.Dictionary.Raw("Matte")) is { } matte && alphaWidth == width && alphaHeight == height)
        _Unmatte(space, matte, rgb, alpha);
    } else if (!forSoftMask && document.Get(dict, inline ? "Mask" : "Mask") is { } mask) {
      if (mask is PdfStreamObject maskStream) {
        var stencil = Decode(document, maskStream.Dictionary, maskStream.Data, resources, false, true);
        alpha = stencil.Alpha;
        alphaWidth = stencil.Width;
        alphaHeight = stencil.Height;
      } else if (document.Numbers(mask) is { } ranges && ranges.Length >= 2 * components) {
        alpha = new byte[width * height];
        for (var p = 0; p < width * height; ++p) {
          var inside = true;
          for (var c = 0; c < components && inside; ++c) {
            var v = raw[p * components + c];
            inside = v >= ranges[2 * c] && v <= ranges[2 * c + 1];
          }

          alpha[p] = inside ? (byte)0 : (byte)255;
        }
      }
    }

    return new PdfRasterImage { Width = width, Height = height, Rgb = rgb, Alpha = alpha, AlphaWidth = alphaWidth, AlphaHeight = alphaHeight };
  }

  /// <summary>The luminosity of a decoded image as one byte a pixel, which is what a soft-mask image is.</summary>
  private static byte[] _Gray(PdfRasterImage image) {
    var gray = new byte[image.Width * image.Height];
    if (image.Rgb == null) {
      if (image.Alpha != null)
        Array.Copy(image.Alpha, gray, Math.Min(gray.Length, image.Alpha.Length));
      return gray;
    }

    for (var i = 0; i < gray.Length; ++i)
      gray[i] = image.Rgb[i * 3];
    return gray;
  }

  private static void _Unmatte(PdfColorModel space, double[] matte, byte[] rgb, byte[] alpha) {
    // §11.6.5.3: the colour was premultiplied against the matte; undo it.
    Span<double> m = stackalloc double[3];
    var comps = new double[Math.Max(space.Components, matte.Length)];
    Array.Copy(matte, comps, Math.Min(matte.Length, comps.Length));
    space.ToRgb(comps, m);
    for (var i = 0; i < alpha.Length; ++i) {
      var a = alpha[i] / 255.0;
      if (a <= 0)
        continue;
      for (var c = 0; c < 3; ++c) {
        var v = (rgb[i * 3 + c] / 255.0 - m[c]) / a + m[c];
        rgb[i * 3 + c] = (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);
      }
    }
  }

  private static string _Key(PdfDict dict, string abbreviation, string full) => dict.ContainsKey(abbreviation) ? abbreviation : full;

  private static PdfColorModel _InlineSpace(PdfDocument document, object value, PdfDict? resources) {
    // Inline images abbreviate the device spaces and may name an Indexed space inline.
    if (value is List<object?> { Count: > 0 } list && list[0] is "I" or "Indexed")
      return PdfColorModel.Parse(document, value, resources);

    return value switch {
      "G" => PdfColorModel.DeviceGray,
      "RGB" => PdfColorModel.DeviceRgb,
      "CMYK" => PdfColorModel.DeviceCmyk,
      _ => PdfColorModel.Parse(document, value, resources),
    };
  }

  private static PdfRasterImage _Stencil(byte[] bytes, int width, int height, double[]? decode) {
    // Sample 0 paints and 1 leaves alone, unless /Decode [1 0] turns that round.
    var paintWhenSet = decode is { Length: >= 2 } && decode[0] > decode[1];
    var stride = (width + 7) / 8;
    var alpha = new byte[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = y * stride + (x >> 3);
        var bit = at < bytes.Length && ((bytes[at] >> (7 - (x & 7))) & 1) != 0;
        alpha[y * width + x] = bit == paintWhenSet ? (byte)255 : (byte)0;
      }

    return new PdfRasterImage { Width = width, Height = height, Alpha = alpha, AlphaWidth = width, AlphaHeight = height, IsStencil = true };
  }

  /// <summary>Unpacks samples to one integer each, rows padded to whole bytes as §8.9.3 says.</summary>
  private static int[] _Unpack(byte[] data, int width, int height, int components, int bpc) {
    var samples = new int[width * height * components];
    var stride = (int)(((long)width * components * bpc + 7) / 8);
    var perRow = width * components;
    for (var y = 0; y < height; ++y) {
      var rowStart = y * stride;
      var outStart = y * perRow;
      switch (bpc) {
        case 8:
          for (var i = 0; i < perRow; ++i) {
            var at = rowStart + i;
            samples[outStart + i] = at < data.Length ? data[at] : 0;
          }
          break;
        case 16:
          for (var i = 0; i < perRow; ++i) {
            var at = rowStart + 2 * i;
            samples[outStart + i] = at + 1 < data.Length ? (data[at] << 8) | data[at + 1] : 0;
          }
          break;
        default: {
          var mask = (1 << bpc) - 1;
          for (var i = 0; i < perRow; ++i) {
            var bit = i * bpc;
            var at = rowStart + (bit >> 3);
            samples[outStart + i] = at < data.Length ? (data[at] >> (8 - bpc - (bit & 7))) & mask : 0;
          }
          break;
        }
      }
    }

    return samples;
  }

  private static void _ToRgb(PdfColorModel space, int[] raw, int pixels, int components, int bpc, double[]? decode, byte[] rgb) {
    var max = (1 << bpc) - 1;
    var dmin = new double[components];
    var dmax = new double[components];
    for (var c = 0; c < components; ++c) {
      var (lo, hi) = space.DefaultDecode(c, bpc);
      if (decode != null && decode.Length >= 2 * c + 2) {
        lo = decode[2 * c];
        hi = decode[2 * c + 1];
      }

      dmin[c] = lo;
      dmax[c] = hi;
    }

    Span<double> values = stackalloc double[components];
    Span<double> out3 = stackalloc double[3];

    // One component of at most eight bits: every possible sample through the space once.
    if (components == 1 && bpc <= 8) {
      var table = new byte[(max + 1) * 3];
      for (var v = 0; v <= max; ++v) {
        values[0] = dmin[0] + v * (dmax[0] - dmin[0]) / max;
        space.ToRgb(values, out3);
        table[v * 3] = _Byte(out3[0]);
        table[v * 3 + 1] = _Byte(out3[1]);
        table[v * 3 + 2] = _Byte(out3[2]);
      }

      for (var p = 0; p < pixels; ++p) {
        var t = raw[p] * 3;
        rgb[p * 3] = table[t];
        rgb[p * 3 + 1] = table[t + 1];
        rgb[p * 3 + 2] = table[t + 2];
      }

      return;
    }

    // Otherwise each distinct sample combination is converted once.
    var cache = new Dictionary<long, int>();
    var cacheable = components * bpc <= 40;
    for (var p = 0; p < pixels; ++p) {
      long key = 0;
      if (cacheable) {
        for (var c = 0; c < components; ++c)
          key = (key << bpc) | (uint)raw[p * components + c];
        if (cache.TryGetValue(key, out var packed)) {
          rgb[p * 3] = (byte)(packed >> 16);
          rgb[p * 3 + 1] = (byte)(packed >> 8);
          rgb[p * 3 + 2] = (byte)packed;
          continue;
        }
      }

      for (var c = 0; c < components; ++c)
        values[c] = dmin[c] + raw[p * components + c] * (dmax[c] - dmin[c]) / max;
      space.ToRgb(values, out3);
      var r = _Byte(out3[0]);
      var g = _Byte(out3[1]);
      var b = _Byte(out3[2]);
      rgb[p * 3] = r;
      rgb[p * 3 + 1] = g;
      rgb[p * 3 + 2] = b;
      if (cacheable && cache.Count < 1 << 20)
        cache[key] = (r << 16) | (g << 8) | b;
    }
  }

  private static byte _Byte(double v) => (byte)Math.Clamp((int)(v * 255 + 0.5), 0, 255);

  private static byte[][] _DecodeJpeg(PdfDocument document, byte[] data, PdfDict? parms, out int width, out int height) {
    var planes = JpegManagedDecoder.DecodeToPlanes(data);
    width = planes.Width;
    height = planes.Height;
    var p = planes.Planes;
    var adobe = _AdobeTransform(data);
    var colorTransform = PdfValues.Integer(document.Get(parms, "ColorTransform"), -1);

    if (p.Length == 3) {
      // YCbCr unless an Adobe marker or /ColorTransform 0 says the planes are RGB already.
      var transform = colorTransform >= 0 ? colorTransform != 0 : adobe != 0;
      if (transform) {
        var rgb = JpegColorConverter.YCbCrToRgb(p[0], p[1], p[2], width, height);
        var r = new byte[width * height];
        var g = new byte[width * height];
        var b = new byte[width * height];
        for (var i = 0; i < r.Length; ++i) {
          r[i] = rgb[i * 3];
          g[i] = rgb[i * 3 + 1];
          b[i] = rgb[i * 3 + 2];
        }

        return [r, g, b];
      }
    } else if (p.Length == 4 && (colorTransform > 0 || (colorTransform < 0 && adobe == 2))) {
      // YCCK: the first three planes are YCbCr of the inverted CMY.
      var count = width * height;
      for (var i = 0; i < count; ++i) {
        var luma = p[0][i];
        var cb = p[1][i] - 128;
        var cr = p[2][i] - 128;
        p[0][i] = (byte)(255 - Math.Clamp(luma + ((91881 * cr + 32768) >> 16), 0, 255));
        p[1][i] = (byte)(255 - Math.Clamp(luma - ((22554 * cb + 46802 * cr + 32768) >> 16), 0, 255));
        p[2][i] = (byte)(255 - Math.Clamp(luma + ((116130 * cb + 32768) >> 16), 0, 255));
      }
    }

    return p;
  }

  /// <summary>The Adobe APP14 transform byte, or -1 when the file has no Adobe marker.</summary>
  private static int _AdobeTransform(byte[] data) {
    var pos = 2;
    while (pos + 4 <= data.Length) {
      if (data[pos] != 0xFF) {
        ++pos;
        continue;
      }

      var marker = data[pos + 1];
      if (marker is 0xD8 or 0x01 or >= 0xD0 and <= 0xD7 or 0xFF) {
        pos += marker == 0xFF ? 1 : 2;
        continue;
      }

      if (marker == 0xDA)
        break;

      var length = (data[pos + 2] << 8) | data[pos + 3];
      if (marker == 0xEE && length >= 12 && pos + 4 + 11 < data.Length && data[pos + 4] == 'A' && data[pos + 5] == 'd' && data[pos + 6] == 'o' && data[pos + 7] == 'b' && data[pos + 8] == 'e')
        return data[pos + 4 + 11];

      pos += 2 + length;
    }

    return -1;
  }

  private static byte[] _Interleave(byte[][] planes, int sourceWidth, int sourceHeight, int width, int height) {
    var n = planes.Length;
    var output = new byte[width * height * n];
    for (var y = 0; y < height; ++y) {
      var sy = Math.Min(sourceHeight - 1, (int)((long)y * sourceHeight / height));
      for (var x = 0; x < width; ++x) {
        var sx = Math.Min(sourceWidth - 1, (int)((long)x * sourceWidth / width));
        for (var c = 0; c < n; ++c)
          output[(y * width + x) * n + c] = planes[c][sy * sourceWidth + sx];
      }
    }

    return output;
  }

  private static byte[] _Resize(byte[] interleaved, int n, int sourceWidth, int sourceHeight, int width, int height) {
    if (sourceWidth == width && sourceHeight == height)
      return interleaved;

    var output = new byte[width * height * n];
    for (var y = 0; y < height; ++y) {
      var sy = Math.Min(sourceHeight - 1, (int)((long)y * sourceHeight / height));
      for (var x = 0; x < width; ++x) {
        var sx = Math.Min(sourceWidth - 1, (int)((long)x * sourceWidth / width));
        for (var c = 0; c < n; ++c)
          output[(y * width + x) * n + c] = interleaved[(sy * sourceWidth + sx) * n + c];
      }
    }

    return output;
  }
}
