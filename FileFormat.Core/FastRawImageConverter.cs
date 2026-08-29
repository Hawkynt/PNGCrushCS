using System;
using System.Runtime.Intrinsics;

namespace FileFormat.Core;

/// <summary>
/// Fast conversion boundary used by image writers. It adds the missing RGB↔planar-YUV routes and
/// batches the common 8-bit YUV→RGB arithmetic with SIMD while retaining scalar fallbacks for less
/// common matrices and higher sample depths.
/// </summary>
public static class FastRawImageConverter {

  /// <summary>Whether the runtime can use the four-pixel vectorized YUV path.</summary>
  public static bool IsSimdAccelerated => Vector128.IsHardwareAccelerated;

  /// <summary>
  /// Converts an image to <paramref name="target"/>. For planar YUV targets, a conventional colour
  /// interpretation is chosen when the source does not already carry one; use the overload accepting
  /// <paramref name="targetColorInfo"/> when a container specifies an exact matrix/range/siting.
  /// </summary>
  public static RawImage Convert(RawImage source, PixelFormat target)
    => Convert(source, target, null);

  /// <summary>
  /// Converts an image to <paramref name="target"/> with an explicit target colour interpretation.
  /// The explicit colour info matters for RGB→YUV because the same YUV sample values mean different
  /// colours under BT.601, BT.709, BT.2020, full-range, and studio-range conventions.
  /// </summary>
  public static RawImage Convert(RawImage source, PixelFormat target, RawImageColorInfo? targetColorInfo) {
    ArgumentNullException.ThrowIfNull(source);

    if (source.Format == target && (targetColorInfo == null || source.ColorInfo == targetColorInfo))
      return source;

    if (RawImage.IsPlanarYuvFormat(source.Format)) {
      var bgra = _PlanarYuvToBgra32(source);
      if (RawImage.IsPlanarYuvFormat(target))
        return _BgraToPlanarYuv(bgra, target, _ResolveYuvColorInfo(bgra, targetColorInfo));
      return target == PixelFormat.Bgra32 ? bgra : RawImageConverter.Convert(bgra, target);
    }

    if (RawImage.IsPlanarYuvFormat(target)) {
      var bgra = source.Format == PixelFormat.Bgra32
        ? source
        : RawImageConverter.Convert(source, PixelFormat.Bgra32);
      return _BgraToPlanarYuv(bgra, target, _ResolveYuvColorInfo(source, targetColorInfo));
    }

    return RawImageConverter.Convert(source, target);
  }

  private static RawImage _PlanarYuvToBgra32(RawImage source) {
    if (!source.HasEnoughPixelData)
      throw new InvalidOperationException(
        $"A {source.Width}x{source.Height} {source.Format} image needs at least {source.MinimumPixelDataLength} "
        + $"bytes but carries {source.PixelData.LongLength}.");

    var bitDepth = RawImage.YuvBitDepth(source.Format);
    if (bitDepth == 8 && _TryGet8BitCoefficients(source.ColorInfo, out var coefficients))
      return _PlanarYuv8ToBgra32(source, coefficients);

    return _PlanarYuvGenericToBgra32(source);
  }

  private static RawImage _PlanarYuv8ToBgra32(RawImage source, Yuv8DecodeCoefficients coefficients) {
    var width = source.Width;
    var height = source.Height;
    var (subsampleX, subsampleY) = RawImage.YuvSubsampling(source.Format);
    var yPlane = source.GetPlaneData(0);
    var uPlane = source.GetPlaneData(1);
    var vPlane = source.GetPlaneData(2);
    var (chromaWidth, _) = source.GetPlaneDimensions(1);
    var result = new byte[checked(width * height * 4)];

    for (var y = 0; y < height; ++y) {
      var x = 0;
      if (Vector128.IsHardwareAccelerated) {
        var yBase = y * width;
        var chromaRow = y / subsampleY * chromaWidth;
        var cy = Vector128.Create(coefficients.CY);
        var rCr = Vector128.Create(coefficients.RCr);
        var gCb = Vector128.Create(coefficients.GCb);
        var gCr = Vector128.Create(coefficients.GCr);
        var bCb = Vector128.Create(coefficients.BCb);
        var rounding = Vector128.Create(128);
        var yOffset = Vector128.Create(coefficients.YOffset);
        var cOffset = Vector128.Create(coefficients.COffset);

        for (; x + 4 <= width; x += 4) {
          var yv = Vector128.Create(
            (int)yPlane[yBase + x],
            (int)yPlane[yBase + x + 1],
            (int)yPlane[yBase + x + 2],
            (int)yPlane[yBase + x + 3]);
          var uv = Vector128.Create(
            (int)uPlane[chromaRow + x / subsampleX],
            (int)uPlane[chromaRow + (x + 1) / subsampleX],
            (int)uPlane[chromaRow + (x + 2) / subsampleX],
            (int)uPlane[chromaRow + (x + 3) / subsampleX]);
          var vv = Vector128.Create(
            (int)vPlane[chromaRow + x / subsampleX],
            (int)vPlane[chromaRow + (x + 1) / subsampleX],
            (int)vPlane[chromaRow + (x + 2) / subsampleX],
            (int)vPlane[chromaRow + (x + 3) / subsampleX]);

          var c = Vector128.Subtract(yv, yOffset);
          var d = Vector128.Subtract(uv, cOffset);
          var e = Vector128.Subtract(vv, cOffset);
          var luma = Vector128.Multiply(c, cy);
          var r = Vector128.ShiftRightArithmetic(
            Vector128.Add(Vector128.Add(luma, Vector128.Multiply(e, rCr)), rounding), 8);
          var g = Vector128.ShiftRightArithmetic(
            Vector128.Add(
              Vector128.Add(Vector128.Add(luma, Vector128.Multiply(d, gCb)), Vector128.Multiply(e, gCr)),
              rounding), 8);
          var b = Vector128.ShiftRightArithmetic(
            Vector128.Add(Vector128.Add(luma, Vector128.Multiply(d, bCb)), rounding), 8);

          var dst = (yBase + x) * 4;
          for (var lane = 0; lane < 4; ++lane) {
            result[dst + lane * 4] = _ClampByte(b.GetElement(lane));
            result[dst + lane * 4 + 1] = _ClampByte(g.GetElement(lane));
            result[dst + lane * 4 + 2] = _ClampByte(r.GetElement(lane));
            result[dst + lane * 4 + 3] = 255;
          }
        }
      }

      for (; x < width; ++x) {
        var luma = yPlane[y * width + x] - coefficients.YOffset;
        var chroma = y / subsampleY * chromaWidth + x / subsampleX;
        var cb = uPlane[chroma] - coefficients.COffset;
        var cr = vPlane[chroma] - coefficients.COffset;
        var common = coefficients.CY * luma;
        var r = (common + coefficients.RCr * cr + 128) >> 8;
        var g = (common + coefficients.GCb * cb + coefficients.GCr * cr + 128) >> 8;
        var b = (common + coefficients.BCb * cb + 128) >> 8;
        var dst = (y * width + x) * 4;
        result[dst] = _ClampByte(b);
        result[dst + 1] = _ClampByte(g);
        result[dst + 2] = _ClampByte(r);
        result[dst + 3] = 255;
      }
    }

    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Bgra32,
      PixelData = result,
      ColorInfo = _RgbColorInfo(source.ColorInfo),
      Metadata = source.Metadata,
    };
  }

  private static RawImage _PlanarYuvGenericToBgra32(RawImage source) {
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
        var chromaIndex = y / subsampleY * chromaWidth + x / subsampleX;
        var cb = _ReadSample(uPlane, chromaIndex, bytesPerSample);
        var cr = _ReadSample(vPlane, chromaIndex, bytesPerSample);
        _YuvToRgb(luma, cb, cr, bitDepth, maxCode, range, matrix, out var r, out var g, out var b);
        var at = (y * width + x) * 4;
        result[at] = b;
        result[at + 1] = g;
        result[at + 2] = r;
        result[at + 3] = 255;
      }

    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Bgra32,
      PixelData = result,
      ColorInfo = _RgbColorInfo(source.ColorInfo),
      Metadata = source.Metadata,
    };
  }

  private static RawImage _BgraToPlanarYuv(RawImage bgra, PixelFormat target, RawImageColorInfo info) {
    if (bgra.Format != PixelFormat.Bgra32)
      throw new ArgumentException("BGRA32 input is required.", nameof(bgra));
    if (!bgra.HasEnoughPixelData)
      throw new InvalidOperationException("The BGRA source buffer is shorter than its declared dimensions.");

    var width = bgra.Width;
    var height = bgra.Height;
    var bitDepth = RawImage.YuvBitDepth(target);
    var (subsampleX, subsampleY) = RawImage.YuvSubsampling(target);
    var bytesPerSample = bitDepth <= 8 ? 1 : 2;
    var chromaWidth = (width + subsampleX - 1) / subsampleX;
    var chromaHeight = (height + subsampleY - 1) / subsampleY;
    var ySamples = checked(width * height);
    var chromaSamples = checked(chromaWidth * chromaHeight);
    var result = new byte[checked((ySamples + chromaSamples * 2) * bytesPerSample)];
    var uOffset = ySamples * bytesPerSample;
    var vOffset = uOffset + chromaSamples * bytesPerSample;

    if (bitDepth == 8 && _TryGet8BitEncodeCoefficients(info, out var coefficients)) {
      _EncodeLuma8(bgra.PixelData, result, width, height, coefficients);
      _EncodeChroma8(bgra.PixelData, result, uOffset, vOffset, width, height, chromaWidth, subsampleX, subsampleY, coefficients);
    } else {
      _EncodeGeneric(bgra.PixelData, result, uOffset, vOffset, width, height, chromaWidth, subsampleX, subsampleY, bitDepth, info);
    }

    return new RawImage {
      Width = width,
      Height = height,
      Format = target,
      PixelData = result,
      ColorInfo = info,
      Metadata = bgra.Metadata,
    };
  }

  private static void _EncodeLuma8(byte[] bgra, byte[] yPlane, int width, int height, Yuv8EncodeCoefficients c) {
    var pixels = checked(width * height);
    var i = 0;

    // Four pixels per batch. Channel extraction is scalar because BGRA is interleaved, while the
    // multiply/add pipeline is vectorized; this avoids temporary arrays and keeps the hot arithmetic
    // in SIMD registers on x64/arm64.
    if (Vector128.IsHardwareAccelerated) {
      var wr = Vector128.Create(c.YR);
      var wg = Vector128.Create(c.YG);
      var wb = Vector128.Create(c.YB);
      var round = Vector128.Create(128);
      var offset = Vector128.Create(c.YOffset);
      for (; i + 4 <= pixels; i += 4) {
        var p = i * 4;
        var r = Vector128.Create((int)bgra[p + 2], (int)bgra[p + 6], (int)bgra[p + 10], (int)bgra[p + 14]);
        var g = Vector128.Create((int)bgra[p + 1], (int)bgra[p + 5], (int)bgra[p + 9], (int)bgra[p + 13]);
        var b = Vector128.Create((int)bgra[p], (int)bgra[p + 4], (int)bgra[p + 8], (int)bgra[p + 12]);
        var sum = Vector128.Add(
          Vector128.Add(Vector128.Multiply(r, wr), Vector128.Multiply(g, wg)),
          Vector128.Multiply(b, wb));
        var y = Vector128.Add(Vector128.ShiftRightArithmetic(Vector128.Add(sum, round), 8), offset);
        yPlane[i] = _ClampByte(y.GetElement(0));
        yPlane[i + 1] = _ClampByte(y.GetElement(1));
        yPlane[i + 2] = _ClampByte(y.GetElement(2));
        yPlane[i + 3] = _ClampByte(y.GetElement(3));
      }
    }

    for (; i < pixels; ++i) {
      var p = i * 4;
      yPlane[i] = _ClampByte(((bgra[p + 2] * c.YR + bgra[p + 1] * c.YG + bgra[p] * c.YB + 128) >> 8) + c.YOffset);
    }
  }

  private static void _EncodeChroma8(
    byte[] bgra, byte[] output, int uOffset, int vOffset, int width, int height, int chromaWidth,
    int subsampleX, int subsampleY, Yuv8EncodeCoefficients c) {
    for (var cy = 0; cy < (height + subsampleY - 1) / subsampleY; ++cy)
      for (var cx = 0; cx < chromaWidth; ++cx) {
        var startX = cx * subsampleX;
        var startY = cy * subsampleY;
        var endX = Math.Min(width, startX + subsampleX);
        var endY = Math.Min(height, startY + subsampleY);
        var sumR = 0;
        var sumG = 0;
        var sumB = 0;
        var count = 0;
        for (var y = startY; y < endY; ++y)
          for (var x = startX; x < endX; ++x) {
            var p = (y * width + x) * 4;
            sumB += bgra[p];
            sumG += bgra[p + 1];
            sumR += bgra[p + 2];
            ++count;
          }

        var r = (sumR + count / 2) / count;
        var g = (sumG + count / 2) / count;
        var b = (sumB + count / 2) / count;
        var chroma = cy * chromaWidth + cx;
        output[uOffset + chroma] = _ClampByte(((r * c.CbR + g * c.CbG + b * c.CbB + 128) >> 8) + 128);
        output[vOffset + chroma] = _ClampByte(((r * c.CrR + g * c.CrG + b * c.CrB + 128) >> 8) + 128);
      }
  }

  private static void _EncodeGeneric(
    byte[] bgra, byte[] output, int uOffset, int vOffset, int width, int height, int chromaWidth,
    int subsampleX, int subsampleY, int bitDepth, RawImageColorInfo info) {
    var maxCode = (1 << bitDepth) - 1;
    var shift = bitDepth - 8;
    var matrix = info.Matrix == RawMatrixCoefficients.Unspecified ? RawMatrixCoefficients.Bt709 : info.Matrix;
    var range = info.Range == RawColorRange.Full ? RawColorRange.Full : RawColorRange.Limited;
    var (kr, kb) = _MatrixWeights(matrix);
    var kg = 1.0 - kr - kb;
    var bytesPerSample = bitDepth <= 8 ? 1 : 2;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var p = (y * width + x) * 4;
        var r = bgra[p + 2] / 255.0;
        var g = bgra[p + 1] / 255.0;
        var b = bgra[p] / 255.0;
        var luma = kr * r + kg * g + kb * b;
        var yCode = range == RawColorRange.Full
          ? (int)Math.Round(luma * maxCode)
          : (16 << shift) + (int)Math.Round(luma * (219 << shift));
        _WriteSample(output, y * width + x, bytesPerSample, Math.Clamp(yCode, 0, maxCode));
      }

    var chromaHeight = (height + subsampleY - 1) / subsampleY;
    for (var cy = 0; cy < chromaHeight; ++cy)
      for (var cx = 0; cx < chromaWidth; ++cx) {
        var startX = cx * subsampleX;
        var startY = cy * subsampleY;
        var endX = Math.Min(width, startX + subsampleX);
        var endY = Math.Min(height, startY + subsampleY);
        double sumR = 0, sumG = 0, sumB = 0;
        var count = 0;
        for (var y = startY; y < endY; ++y)
          for (var x = startX; x < endX; ++x) {
            var p = (y * width + x) * 4;
            sumR += bgra[p + 2] / 255.0;
            sumG += bgra[p + 1] / 255.0;
            sumB += bgra[p] / 255.0;
            ++count;
          }

        var r = sumR / count;
        var g = sumG / count;
        var b = sumB / count;
        var luma = kr * r + kg * g + kb * b;
        var cb = (b - luma) / (2.0 * (1.0 - kb));
        var cr = (r - luma) / (2.0 * (1.0 - kr));
        int cbCode;
        int crCode;
        if (range == RawColorRange.Full) {
          var center = 1 << (bitDepth - 1);
          cbCode = center + (int)Math.Round(cb * maxCode);
          crCode = center + (int)Math.Round(cr * maxCode);
        } else {
          var center = 128 << shift;
          var chromaRange = 224 << shift;
          cbCode = center + (int)Math.Round(cb * chromaRange);
          crCode = center + (int)Math.Round(cr * chromaRange);
        }

        var chroma = cy * chromaWidth + cx;
        _WriteSample(output, uOffset / bytesPerSample + chroma, bytesPerSample, Math.Clamp(cbCode, 0, maxCode));
        _WriteSample(output, vOffset / bytesPerSample + chroma, bytesPerSample, Math.Clamp(crCode, 0, maxCode));
      }
  }

  private static RawImageColorInfo _ResolveYuvColorInfo(RawImage source, RawImageColorInfo? requested) {
    if (requested != null) {
      var requestedMatrix = requested.Matrix is RawMatrixCoefficients.Unspecified or RawMatrixCoefficients.Identity
        ? _DefaultMatrix(source.Width, source.Height)
        : requested.Matrix;
      var requestedRange = requested.Range == RawColorRange.Unspecified ? RawColorRange.Limited : requested.Range;
      return requested with {
        Matrix = requestedMatrix,
        Range = requestedRange,
        ChromaLocation = requested.ChromaLocation == RawChromaLocation.Unspecified ? RawChromaLocation.Left : requested.ChromaLocation,
      };
    }

    var inherited = source.ColorInfo;
    var matrixFromSource = inherited?.Matrix;
    var matrix = matrixFromSource is not null and not RawMatrixCoefficients.Unspecified and not RawMatrixCoefficients.Identity
      ? matrixFromSource.Value
      : _DefaultMatrix(source.Width, source.Height);
    var range = inherited?.Range == RawColorRange.Full ? RawColorRange.Full : RawColorRange.Limited;
    return new RawImageColorInfo {
      Range = range,
      Primaries = inherited?.Primaries ?? RawColorPrimaries.Unspecified,
      Transfer = inherited?.Transfer ?? RawTransferCharacteristic.Unspecified,
      Matrix = matrix,
      ChromaLocation = RawChromaLocation.Left,
    };
  }

  private static RawMatrixCoefficients _DefaultMatrix(int width, int height)
    => width >= 720 || height >= 576 ? RawMatrixCoefficients.Bt709 : RawMatrixCoefficients.Bt601;

  private static bool _TryGet8BitCoefficients(RawImageColorInfo? info, out Yuv8DecodeCoefficients coefficients) {
    var range = info?.Range == RawColorRange.Full ? RawColorRange.Full : RawColorRange.Limited;
    var matrix = info?.Matrix ?? RawMatrixCoefficients.Unspecified;
    if (matrix == RawMatrixCoefficients.Unspecified)
      matrix = RawMatrixCoefficients.Bt601;

    coefficients = (range, matrix) switch {
      (RawColorRange.Limited, RawMatrixCoefficients.Bt601) => new(16, 128, 298, 409, -100, -208, 516),
      (RawColorRange.Limited, RawMatrixCoefficients.Bt709) => new(16, 128, 298, 459, -55, -136, 541),
      (RawColorRange.Limited, RawMatrixCoefficients.Bt2020NonConstantLuminance) => new(16, 128, 298, 430, -48, -167, 548),
      (RawColorRange.Full, RawMatrixCoefficients.Bt601) => new(0, 128, 256, 359, -88, -183, 454),
      (RawColorRange.Full, RawMatrixCoefficients.Bt709) => new(0, 128, 256, 403, -48, -120, 475),
      (RawColorRange.Full, RawMatrixCoefficients.Bt2020NonConstantLuminance) => new(0, 128, 256, 377, -42, -146, 482),
      _ => default,
    };
    return coefficients.CY != 0;
  }

  private static bool _TryGet8BitEncodeCoefficients(RawImageColorInfo info, out Yuv8EncodeCoefficients coefficients) {
    var range = info.Range == RawColorRange.Full ? RawColorRange.Full : RawColorRange.Limited;
    var matrix = info.Matrix == RawMatrixCoefficients.Unspecified ? RawMatrixCoefficients.Bt709 : info.Matrix;
    coefficients = (range, matrix) switch {
      (RawColorRange.Limited, RawMatrixCoefficients.Bt601) => new(66, 129, 25, 16, -38, -74, 112, 112, -94, -18),
      (RawColorRange.Limited, RawMatrixCoefficients.Bt709) => new(47, 157, 16, 16, -26, -87, 112, 112, -102, -10),
      (RawColorRange.Limited, RawMatrixCoefficients.Bt2020NonConstantLuminance) => new(58, 149, 13, 16, -32, -82, 114, 114, -104, -10),
      (RawColorRange.Full, RawMatrixCoefficients.Bt601) => new(77, 150, 29, 0, -43, -85, 128, 128, -107, -21),
      // 54 + 183 + 19 is 256, not the 54 + 183 + 18 that rounding each weight on its own produces.
      // A luma triple summing to 255 loses a step on every neutral grey — 192 came back 191 — and
      // both siblings below already sum to 256. The spare step goes to blue because blue carries the
      // largest discarded remainder (0.483 against red's 0.426), which is the smallest total error
      // available.
      (RawColorRange.Full, RawMatrixCoefficients.Bt709) => new(54, 183, 19, 0, -29, -99, 128, 128, -116, -12),
      (RawColorRange.Full, RawMatrixCoefficients.Bt2020NonConstantLuminance) => new(67, 174, 15, 0, -36, -92, 128, 128, -118, -10),
      _ => default,
    };
    return coefficients.YR != 0;
  }

  private static (double Kr, double Kb) _MatrixWeights(RawMatrixCoefficients matrix) => matrix switch {
    RawMatrixCoefficients.Bt709 => (0.2126, 0.0722),
    RawMatrixCoefficients.Fcc => (0.30, 0.11),
    RawMatrixCoefficients.Smpte240M => (0.212, 0.087),
    RawMatrixCoefficients.Bt2020NonConstantLuminance or RawMatrixCoefficients.Bt2020ConstantLuminance => (0.2627, 0.0593),
    _ => (0.299, 0.114),
  };

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
      y = (yCode - (16 << shift)) / (double)(219 << shift);
      cb = (cbCode - (128 << shift)) / (double)(224 << shift);
      cr = (crCode - (128 << shift)) / (double)(224 << shift);
    }

    if (matrix == RawMatrixCoefficients.YCgCo) {
      r8 = _ToByte(y - cb + cr);
      g8 = _ToByte(y + cb);
      b8 = _ToByte(y - cb - cr);
      return;
    }

    var (kr, kb) = _MatrixWeights(matrix);
    var kg = 1.0 - kr - kb;
    var r = y + 2.0 * (1.0 - kr) * cr;
    var b = y + 2.0 * (1.0 - kb) * cb;
    var g = (y - kr * r - kb * b) / kg;
    r8 = _ToByte(r);
    g8 = _ToByte(g);
    b8 = _ToByte(b);
  }

  private static RawImageColorInfo? _RgbColorInfo(RawImageColorInfo? source) => source == null ? null : source with {
    Range = RawColorRange.Full,
    Matrix = RawMatrixCoefficients.Identity,
    ChromaLocation = RawChromaLocation.Unspecified,
  };

  private static int _ReadSample(ReadOnlySpan<byte> plane, int index, int bytesPerSample) {
    if (bytesPerSample == 1)
      return plane[index];
    var at = index * 2;
    return plane[at] | plane[at + 1] << 8;
  }

  private static void _WriteSample(byte[] data, int sampleIndex, int bytesPerSample, int value) {
    if (bytesPerSample == 1) {
      data[sampleIndex] = (byte)value;
      return;
    }
    var at = sampleIndex * 2;
    data[at] = (byte)value;
    data[at + 1] = (byte)(value >> 8);
  }

  private static byte _ClampByte(int value) => (byte)Math.Clamp(value, 0, 255);

  private static byte _ToByte(double value) {
    if (double.IsNaN(value) || value <= 0)
      return 0;
    if (value >= 1)
      return 255;
    return (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);
  }

  private readonly record struct Yuv8DecodeCoefficients(
    int YOffset, int COffset, int CY, int RCr, int GCb, int GCr, int BCb);

  private readonly record struct Yuv8EncodeCoefficients(
    int YR, int YG, int YB, int YOffset,
    int CbR, int CbG, int CbB,
    int CrR, int CrG, int CrB);
}
