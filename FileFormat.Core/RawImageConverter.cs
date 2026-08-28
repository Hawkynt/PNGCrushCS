using System;

namespace FileFormat.Core;

/// <summary>
/// High-level conversion entry point for <see cref="RawImage"/>.
/// </summary>
/// <remarks>
/// <see cref="PixelConverter"/> predates planar and floating-point images and remains the fast packed
/// integer converter. This type is the boundary above it: it handles native YUV and HDR inputs first,
/// then delegates the ordinary packed conversion to <see cref="PixelConverter"/>. Writers should use
/// this converter (normally through <see cref="RawImageExtensions.EnsureFormat"/>) rather than forcing
/// decoders to return RGB8.
/// </remarks>
public static class RawImageConverter {

  /// <summary>Converts an image while preserving its metadata and colour interpretation where meaningful.</summary>
  public static RawImage Convert(RawImage source, PixelFormat target) {
    ArgumentNullException.ThrowIfNull(source);

    if (source.Format == target)
      return source;

    if (RawImage.IsPlanarYuvFormat(source.Format)) {
      var bgra = _PlanarYuvToBgra32(source);
      return target == PixelFormat.Bgra32 ? bgra : PixelConverter.Convert(bgra, target);
    }

    if (RawImage.IsFloatingPointFormat(source.Format)) {
      if (RawImage.IsFloatingPointFormat(target))
        return _FloatToFloat(source, target);

      var bgra = _FloatToBgra32(source);
      return target == PixelFormat.Bgra32 ? bgra : PixelConverter.Convert(bgra, target);
    }

    if (RawImage.IsFloatingPointFormat(target))
      return _IntegerToFloat(source, target);

    if (RawImage.IsPlanarYuvFormat(target))
      throw new NotSupportedException(
        $"Conversion from {source.Format} to planar {target} is not defined yet. Native YUV decoders can emit "
        + "that layout directly; an RGB-to-YUV writer must choose an explicit matrix, signal range and chroma "
        + "siting rather than inheriting an accidental default.");

    return PixelConverter.Convert(source, target);
  }

  private static RawImage _PlanarYuvToBgra32(RawImage source) {
    if (!source.HasEnoughPixelData)
      throw new InvalidOperationException(
        $"A {source.Width}x{source.Height} {source.Format} image needs at least {source.MinimumPixelDataLength} "
        + $"bytes but carries {source.PixelData.LongLength}.");

    var width = source.Width;
    var height = source.Height;
    var bitDepth = RawImage.YuvBitDepth(source.Format);
    var (subsampleX, subsampleY) = RawImage.YuvSubsampling(source.Format);
    var bytesPerSample = bitDepth <= 8 ? 1 : 2;
    var maxCode = (1 << bitDepth) - 1;
    var info = source.ColorInfo;
    var range = info?.Range is RawColorRange.Full ? RawColorRange.Full : RawColorRange.Limited;
    var matrix = info?.Matrix ?? RawMatrixCoefficients.Unspecified;
    if (matrix == RawMatrixCoefficients.Unspecified)
      matrix = RawMatrixCoefficients.Bt601;

    var yPlane = source.GetPlaneData(0);
    var uPlane = source.GetPlaneData(1);
    var vPlane = source.GetPlaneData(2);
    var (chromaWidth, _) = source.GetPlaneDimensions(1);
    var result = new byte[checked(width * height * 4)];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var luma = _ReadSample(yPlane, y * width + x, bytesPerSample);
        var chromaIndex = (y / subsampleY) * chromaWidth + x / subsampleX;
        var cb = _ReadSample(uPlane, chromaIndex, bytesPerSample);
        var cr = _ReadSample(vPlane, chromaIndex, bytesPerSample);

        _YuvToRgb(luma, cb, cr, bitDepth, maxCode, range, matrix, out var r, out var g, out var b);

        var at = (y * width + x) * 4;
        result[at] = b;
        result[at + 1] = g;
        result[at + 2] = r;
        result[at + 3] = 255;
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Bgra32,
      PixelData = result,
      ColorInfo = _RgbColorInfo(source.ColorInfo),
      Metadata = source.Metadata,
    };
  }

  private static void _YuvToRgb(
    int yCode, int cbCode, int crCode, int bitDepth, int maxCode, RawColorRange range,
    RawMatrixCoefficients matrix, out byte r8, out byte g8, out byte b8) {
    var shift = bitDepth - 8;
    double y;
    double cb;
    double cr;

    if (range == RawColorRange.Full) {
      y = yCode / (double)maxCode;
      var center = 1 << (bitDepth - 1);
      cb = (cbCode - center) / (double)maxCode;
      cr = (crCode - center) / (double)maxCode;
    } else {
      var yBlack = 16 << shift;
      var yRange = 219 << shift;
      var cCenter = 128 << shift;
      var cRange = 224 << shift;
      y = (yCode - yBlack) / (double)yRange;
      cb = (cbCode - cCenter) / (double)cRange;
      cr = (crCode - cCenter) / (double)cRange;
    }

    if (matrix == RawMatrixCoefficients.YCgCo) {
      var co = cr;
      var cg = cb;
      r8 = _ToByte(y - cg + co);
      g8 = _ToByte(y + cg);
      b8 = _ToByte(y - cg - co);
      return;
    }

    var (kr, kb) = matrix switch {
      RawMatrixCoefficients.Bt709 => (0.2126, 0.0722),
      RawMatrixCoefficients.Fcc => (0.30, 0.11),
      RawMatrixCoefficients.Smpte240M => (0.212, 0.087),
      RawMatrixCoefficients.Bt2020NonConstantLuminance or RawMatrixCoefficients.Bt2020ConstantLuminance
        => (0.2627, 0.0593),
      _ => (0.299, 0.114),
    };
    var kg = 1.0 - kr - kb;

    var r = y + 2.0 * (1.0 - kr) * cr;
    var b = y + 2.0 * (1.0 - kb) * cb;
    var g = (y - kr * r - kb * b) / kg;

    r8 = _ToByte(r);
    g8 = _ToByte(g);
    b8 = _ToByte(b);
  }

  private static int _ReadSample(ReadOnlySpan<byte> plane, int index, int bytesPerSample) {
    if (bytesPerSample == 1)
      return plane[index];

    var at = index * 2;
    return plane[at] | (plane[at + 1] << 8);
  }

  private static RawImage _FloatToBgra32(RawImage source) {
    if (!source.HasEnoughPixelData)
      throw new InvalidOperationException(
        $"A {source.Width}x{source.Height} {source.Format} image needs at least {source.MinimumPixelDataLength} "
        + $"bytes but carries {source.PixelData.LongLength}.");

    var pixels = checked(source.Width * source.Height);
    var result = new byte[checked(pixels * 4)];
    var channels = _FloatChannelCount(source.Format);
    var bytes = _FloatBytesPerSample(source.Format);

    for (var i = 0; i < pixels; ++i) {
      var baseSample = i * channels;
      float r;
      float g;
      float b;
      float a;

      switch (source.Format) {
        case PixelFormat.GrayF16:
        case PixelFormat.GrayF32:
          r = g = b = _ReadFloat(source.PixelData, baseSample, bytes);
          a = 1f;
          break;
        case PixelFormat.GrayAlphaF16:
        case PixelFormat.GrayAlphaF32:
          r = g = b = _ReadFloat(source.PixelData, baseSample, bytes);
          a = _ReadFloat(source.PixelData, baseSample + 1, bytes);
          break;
        case PixelFormat.RgbF16:
        case PixelFormat.RgbF32:
          r = _ReadFloat(source.PixelData, baseSample, bytes);
          g = _ReadFloat(source.PixelData, baseSample + 1, bytes);
          b = _ReadFloat(source.PixelData, baseSample + 2, bytes);
          a = 1f;
          break;
        case PixelFormat.RgbaF16:
        case PixelFormat.RgbaF32:
          r = _ReadFloat(source.PixelData, baseSample, bytes);
          g = _ReadFloat(source.PixelData, baseSample + 1, bytes);
          b = _ReadFloat(source.PixelData, baseSample + 2, bytes);
          a = _ReadFloat(source.PixelData, baseSample + 3, bytes);
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(source));
      }

      var at = i * 4;
      result[at] = _ToByte(b);
      result[at + 1] = _ToByte(g);
      result[at + 2] = _ToByte(r);
      result[at + 3] = _ToByte(a);
    }

    return new() {
      Width = source.Width,
      Height = source.Height,
      Format = PixelFormat.Bgra32,
      PixelData = result,
      ColorInfo = _RgbColorInfo(source.ColorInfo),
      Metadata = source.Metadata,
    };
  }

  private static RawImage _FloatToFloat(RawImage source, PixelFormat target) {
    var pixels = checked(source.Width * source.Height);
    var targetChannels = _FloatChannelCount(target);
    var targetBytes = _FloatBytesPerSample(target);
    var result = new byte[checked(pixels * targetChannels * targetBytes)];

    var sourceChannels = _FloatChannelCount(source.Format);
    var sourceBytes = _FloatBytesPerSample(source.Format);

    for (var i = 0; i < pixels; ++i) {
      var sourceBase = i * sourceChannels;
      _ReadRgbaFloat(source, sourceBase, sourceBytes, out var r, out var g, out var b, out var a);
      var targetBase = i * targetChannels;
      _WriteFloatTarget(result, target, targetBase, targetBytes, r, g, b, a);
    }

    return new() {
      Width = source.Width,
      Height = source.Height,
      Format = target,
      PixelData = result,
      ColorInfo = source.ColorInfo,
      Metadata = source.Metadata,
    };
  }

  private static RawImage _IntegerToFloat(RawImage source, PixelFormat target) {
    // Rgba64 is used as the integer hub rather than BGRA32 so a 16-bit source is not needlessly
    // narrowed before becoming floating point. PixelConverter already has direct 16-bit routes.
    var rgba = source.Format == PixelFormat.Rgba64 ? source : PixelConverter.Convert(source, PixelFormat.Rgba64);
    var pixels = checked(source.Width * source.Height);
    var targetChannels = _FloatChannelCount(target);
    var targetBytes = _FloatBytesPerSample(target);
    var result = new byte[checked(pixels * targetChannels * targetBytes)];

    for (var i = 0; i < pixels; ++i) {
      var at = i * 8;
      var r = ((rgba.PixelData[at] << 8) | rgba.PixelData[at + 1]) / 65535f;
      var g = ((rgba.PixelData[at + 2] << 8) | rgba.PixelData[at + 3]) / 65535f;
      var b = ((rgba.PixelData[at + 4] << 8) | rgba.PixelData[at + 5]) / 65535f;
      var a = ((rgba.PixelData[at + 6] << 8) | rgba.PixelData[at + 7]) / 65535f;
      _WriteFloatTarget(result, target, i * targetChannels, targetBytes, r, g, b, a);
    }

    return new() {
      Width = source.Width,
      Height = source.Height,
      Format = target,
      PixelData = result,
      ColorInfo = source.ColorInfo,
      Metadata = source.Metadata,
    };
  }

  private static void _ReadRgbaFloat(
    RawImage source, int baseSample, int bytesPerSample, out float r, out float g, out float b, out float a) {
    switch (source.Format) {
      case PixelFormat.GrayF16:
      case PixelFormat.GrayF32:
        r = g = b = _ReadFloat(source.PixelData, baseSample, bytesPerSample);
        a = 1f;
        return;
      case PixelFormat.GrayAlphaF16:
      case PixelFormat.GrayAlphaF32:
        r = g = b = _ReadFloat(source.PixelData, baseSample, bytesPerSample);
        a = _ReadFloat(source.PixelData, baseSample + 1, bytesPerSample);
        return;
      case PixelFormat.RgbF16:
      case PixelFormat.RgbF32:
        r = _ReadFloat(source.PixelData, baseSample, bytesPerSample);
        g = _ReadFloat(source.PixelData, baseSample + 1, bytesPerSample);
        b = _ReadFloat(source.PixelData, baseSample + 2, bytesPerSample);
        a = 1f;
        return;
      case PixelFormat.RgbaF16:
      case PixelFormat.RgbaF32:
        r = _ReadFloat(source.PixelData, baseSample, bytesPerSample);
        g = _ReadFloat(source.PixelData, baseSample + 1, bytesPerSample);
        b = _ReadFloat(source.PixelData, baseSample + 2, bytesPerSample);
        a = _ReadFloat(source.PixelData, baseSample + 3, bytesPerSample);
        return;
      default:
        throw new ArgumentOutOfRangeException(nameof(source));
    }
  }

  private static void _WriteFloatTarget(
    byte[] target, PixelFormat format, int baseSample, int bytesPerSample, float r, float g, float b, float a) {
    switch (format) {
      case PixelFormat.GrayF16:
      case PixelFormat.GrayF32:
        _WriteFloat(target, baseSample, bytesPerSample, _Luma(r, g, b));
        return;
      case PixelFormat.GrayAlphaF16:
      case PixelFormat.GrayAlphaF32:
        _WriteFloat(target, baseSample, bytesPerSample, _Luma(r, g, b));
        _WriteFloat(target, baseSample + 1, bytesPerSample, a);
        return;
      case PixelFormat.RgbF16:
      case PixelFormat.RgbF32:
        _WriteFloat(target, baseSample, bytesPerSample, r);
        _WriteFloat(target, baseSample + 1, bytesPerSample, g);
        _WriteFloat(target, baseSample + 2, bytesPerSample, b);
        return;
      case PixelFormat.RgbaF16:
      case PixelFormat.RgbaF32:
        _WriteFloat(target, baseSample, bytesPerSample, r);
        _WriteFloat(target, baseSample + 1, bytesPerSample, g);
        _WriteFloat(target, baseSample + 2, bytesPerSample, b);
        _WriteFloat(target, baseSample + 3, bytesPerSample, a);
        return;
      default:
        throw new ArgumentOutOfRangeException(nameof(format));
    }
  }

  private static float _ReadFloat(byte[] data, int sample, int bytesPerSample) {
    var at = sample * bytesPerSample;
    if (bytesPerSample == 2) {
      var bits = (ushort)(data[at] | (data[at + 1] << 8));
      return (float)BitConverter.UInt16BitsToHalf(bits);
    }

    var value = data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24);
    return BitConverter.Int32BitsToSingle(value);
  }

  private static void _WriteFloat(byte[] data, int sample, int bytesPerSample, float value) {
    var at = sample * bytesPerSample;
    if (bytesPerSample == 2) {
      var bits = BitConverter.HalfToUInt16Bits((Half)value);
      data[at] = (byte)bits;
      data[at + 1] = (byte)(bits >> 8);
      return;
    }

    var bits32 = BitConverter.SingleToInt32Bits(value);
    data[at] = (byte)bits32;
    data[at + 1] = (byte)(bits32 >> 8);
    data[at + 2] = (byte)(bits32 >> 16);
    data[at + 3] = (byte)(bits32 >> 24);
  }

  private static int _FloatChannelCount(PixelFormat format) => format switch {
    PixelFormat.GrayF16 or PixelFormat.GrayF32 => 1,
    PixelFormat.GrayAlphaF16 or PixelFormat.GrayAlphaF32 => 2,
    PixelFormat.RgbF16 or PixelFormat.RgbF32 => 3,
    PixelFormat.RgbaF16 or PixelFormat.RgbaF32 => 4,
    _ => throw new ArgumentOutOfRangeException(nameof(format)),
  };

  private static int _FloatBytesPerSample(PixelFormat format) => format switch {
    PixelFormat.GrayF16 or PixelFormat.GrayAlphaF16 or PixelFormat.RgbF16 or PixelFormat.RgbaF16 => 2,
    PixelFormat.GrayF32 or PixelFormat.GrayAlphaF32 or PixelFormat.RgbF32 or PixelFormat.RgbaF32 => 4,
    _ => throw new ArgumentOutOfRangeException(nameof(format)),
  };

  private static float _Luma(float r, float g, float b) => 0.2126f * r + 0.7152f * g + 0.0722f * b;

  private static byte _ToByte(double value) {
    if (double.IsNaN(value) || value <= 0)
      return 0;
    if (value >= 1)
      return 255;
    return (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);
  }

  private static RawImageColorInfo? _RgbColorInfo(RawImageColorInfo? source) => source == null ? null : source with {
    Range = RawColorRange.Full,
    Matrix = RawMatrixCoefficients.Identity,
    ChromaLocation = RawChromaLocation.Unspecified,
  };
}
