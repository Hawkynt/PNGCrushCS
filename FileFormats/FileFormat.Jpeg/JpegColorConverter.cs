using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace FileFormat.Jpeg;

/// <summary>YCbCr/RGB color conversion and chroma subsampling/upsampling.</summary>
internal static class JpegColorConverter {

  /// <summary>Converts an RGB image to component planes (Y,Cb,Cr or just Y for grayscale).</summary>
  public static byte[][] RgbToYCbCr(byte[] rgb, int width, int height) {
    var y = new byte[width * height];
    var cb = new byte[width * height];
    var cr = new byte[width * height];

    for (var i = 0; i < width * height; ++i) {
      var r = rgb[i * 3];
      var g = rgb[i * 3 + 1];
      var b = rgb[i * 3 + 2];

      // ITU-R BT.601: Y = 0.299R + 0.587G + 0.114B
      y[i] = (byte)Math.Clamp((19595 * r + 38470 * g + 7471 * b + 32768) >> 16, 0, 255);
      cb[i] = (byte)Math.Clamp(128 + ((-11056 * r - 21712 * g + 32768 * b + 32768) >> 16), 0, 255);
      cr[i] = (byte)Math.Clamp(128 + ((32768 * r - 27440 * g - 5328 * b + 32768) >> 16), 0, 255);
    }

    return [y, cb, cr];
  }

  /// <summary>Extracts grayscale plane from RGB data (all channels should be equal for true grayscale).</summary>
  public static byte[] RgbToGrayscale(byte[] rgb, int width, int height) {
    var y = new byte[width * height];
    for (var i = 0; i < width * height; ++i)
      y[i] = rgb[i * 3];
    return y;
  }

  /// <summary>Converts grayscale Y plane to packed RGB24 (R=G=B=Y).</summary>
  public static byte[] GrayscaleToRgb(byte[] y, int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      rgb[i * 3] = y[i];
      rgb[i * 3 + 1] = y[i];
      rgb[i * 3 + 2] = y[i];
    }

    return rgb;
  }

  /// <summary>Converts YCbCr component planes back to packed RGB24.</summary>
  public static byte[] YCbCrToRgb(byte[] yPlane, byte[] cbPlane, byte[] crPlane, int width, int height) {
    var pixelCount = width * height;
    var rgb = new byte[pixelCount * 3];

    var i = 0;

    // SIMD path: process 8 pixels at a time using SSE2
    if (Sse2.IsSupported && pixelCount >= 8) {
      var bias128 = Vector128.Create((short)128);
      var zero = Vector128<short>.Zero;
      var max255 = Vector128.Create((short)255);

      // Fixed-point coefficients (Q16)
      var crToR = Vector128.Create((short)((91881 + 128) >> 8));   // ≈359
      var cbToG = Vector128.Create((short)((22554 + 128) >> 8));   // ≈88
      var crToG = Vector128.Create((short)((46802 + 128) >> 8));   // ≈183
      var cbToB = Vector128.Create((short)((116130 + 128) >> 8));  // ≈454

      for (; i <= pixelCount - 8; i += 8) {
        // Load 8 bytes and widen to 16-bit
        var yVec = Sse2.UnpackLow(Vector128.Create(yPlane[i], yPlane[i + 1], yPlane[i + 2], yPlane[i + 3], yPlane[i + 4], yPlane[i + 5], yPlane[i + 6], yPlane[i + 7], 0, 0, 0, 0, 0, 0, 0, 0).AsByte(), Vector128<byte>.Zero).AsInt16();
        var cbVec = Sse2.Subtract(Sse2.UnpackLow(Vector128.Create(cbPlane[i], cbPlane[i + 1], cbPlane[i + 2], cbPlane[i + 3], cbPlane[i + 4], cbPlane[i + 5], cbPlane[i + 6], cbPlane[i + 7], 0, 0, 0, 0, 0, 0, 0, 0).AsByte(), Vector128<byte>.Zero).AsInt16(), bias128);
        var crVec = Sse2.Subtract(Sse2.UnpackLow(Vector128.Create(crPlane[i], crPlane[i + 1], crPlane[i + 2], crPlane[i + 3], crPlane[i + 4], crPlane[i + 5], crPlane[i + 6], crPlane[i + 7], 0, 0, 0, 0, 0, 0, 0, 0).AsByte(), Vector128<byte>.Zero).AsInt16(), bias128);

        // R = Y + (91881 * Cr >> 16) ≈ Y + (Cr * 359 >> 8)
        var rVec = Sse2.Add(yVec, Sse2.ShiftRightArithmetic(Sse2.MultiplyHigh(crVec, crToR), 0));
        rVec = Sse2.Add(yVec, Sse2.ShiftRightArithmetic(Sse2.MultiplyLow(crVec, crToR), 8));

        // G = Y - ((22554 * Cb + 46802 * Cr) >> 16) ≈ Y - ((Cb * 88 + Cr * 183) >> 8)
        var gVec = Sse2.Subtract(yVec, Sse2.ShiftRightArithmetic(Sse2.Add(Sse2.MultiplyLow(cbVec, cbToG), Sse2.MultiplyLow(crVec, crToG)), 8));

        // B = Y + (116130 * Cb >> 16) ≈ Y + (Cb * 454 >> 8)
        var bVec = Sse2.Add(yVec, Sse2.ShiftRightArithmetic(Sse2.MultiplyLow(cbVec, cbToB), 8));

        // Clamp to [0, 255]
        rVec = Sse2.Max(zero, Sse2.Min(max255, rVec));
        gVec = Sse2.Max(zero, Sse2.Min(max255, gVec));
        bVec = Sse2.Max(zero, Sse2.Min(max255, bVec));

        // Interleave and store as RGB24
        var offset = i * 3;
        for (var j = 0; j < 8; ++j) {
          rgb[offset + j * 3] = (byte)rVec.GetElement(j);
          rgb[offset + j * 3 + 1] = (byte)gVec.GetElement(j);
          rgb[offset + j * 3 + 2] = (byte)bVec.GetElement(j);
        }
      }
    }

    // Scalar fallback for remaining pixels
    for (; i < pixelCount; ++i) {
      var yVal = yPlane[i];
      var cbVal = cbPlane[i] - 128;
      var crVal = crPlane[i] - 128;

      var r = yVal + ((91881 * crVal + 32768) >> 16);
      var g = yVal - ((22554 * cbVal + 46802 * crVal + 32768) >> 16);
      var b = yVal + ((116130 * cbVal + 32768) >> 16);

      rgb[i * 3] = (byte)Math.Clamp(r, 0, 255);
      rgb[i * 3 + 1] = (byte)Math.Clamp(g, 0, 255);
      rgb[i * 3 + 2] = (byte)Math.Clamp(b, 0, 255);
    }

    return rgb;
  }

  /// <summary>Downsamples a component plane by the given factors (box filter average).</summary>
  public static byte[] Downsample(byte[] plane, int width, int height, int hFactor, int vFactor) {
    if (hFactor == 1 && vFactor == 1)
      return (byte[])plane.Clone();

    var outWidth = (width + hFactor - 1) / hFactor;
    var outHeight = (height + vFactor - 1) / vFactor;
    var result = new byte[outWidth * outHeight];

    for (var oy = 0; oy < outHeight; ++oy)
      for (var ox = 0; ox < outWidth; ++ox) {
        var sum = 0;
        var count = 0;
        for (var dy = 0; dy < vFactor; ++dy)
          for (var dx = 0; dx < hFactor; ++dx) {
            var sx = ox * hFactor + dx;
            var sy = oy * vFactor + dy;
            if (sx < width && sy < height) {
              sum += plane[sy * width + sx];
              ++count;
            }
          }

        result[oy * outWidth + ox] = (byte)(count > 0 ? (sum + count / 2) / count : 0);
      }

    return result;
  }

  /// <summary>Upsamples a component plane by the given factors (nearest-neighbor).</summary>
  public static byte[] Upsample(byte[] plane, int inWidth, int inHeight, int outWidth, int outHeight) {
    if (inWidth == outWidth && inHeight == outHeight)
      return (byte[])plane.Clone();

    var result = new byte[outWidth * outHeight];

    // Fast path: 2x horizontal upsampling (4:2:2 → 4:4:4)
    if (outWidth == inWidth * 2 && outHeight == inHeight) {
      for (var y = 0; y < inHeight; ++y) {
        var srcOff = y * inWidth;
        var dstOff = y * outWidth;
        for (var x = 0; x < inWidth; ++x) {
          var v = plane[srcOff + x];
          result[dstOff + x * 2] = v;
          result[dstOff + x * 2 + 1] = v;
        }
      }
      return result;
    }

    // Fast path: 2x both (4:2:0 → 4:4:4)
    if (outWidth == inWidth * 2 && outHeight == inHeight * 2) {
      for (var y = 0; y < inHeight; ++y) {
        var srcOff = y * inWidth;
        var dstOff1 = (y * 2) * outWidth;
        var dstOff2 = (y * 2 + 1) * outWidth;
        for (var x = 0; x < inWidth; ++x) {
          var v = plane[srcOff + x];
          result[dstOff1 + x * 2] = v;
          result[dstOff1 + x * 2 + 1] = v;
          result[dstOff2 + x * 2] = v;
          result[dstOff2 + x * 2 + 1] = v;
        }
      }
      return result;
    }

    // General case
    for (var oy = 0; oy < outHeight; ++oy) {
      var sy = Math.Min(oy * inHeight / outHeight, inHeight - 1);
      for (var ox = 0; ox < outWidth; ++ox) {
        var sx = Math.Min(ox * inWidth / outWidth, inWidth - 1);
        result[oy * outWidth + ox] = plane[sy * inWidth + sx];
      }
    }

    return result;
  }

  /// <summary>Gets the chroma H/V sampling factors for a given subsampling mode.</summary>
  public static (int hFactor, int vFactor) GetChromaFactors(JpegSubsampling subsampling) => subsampling switch {
    JpegSubsampling.Chroma444 => (1, 1),
    JpegSubsampling.Chroma422 => (2, 1),
    JpegSubsampling.Chroma420 => (2, 2),
    _ => (1, 1)
  };
}
