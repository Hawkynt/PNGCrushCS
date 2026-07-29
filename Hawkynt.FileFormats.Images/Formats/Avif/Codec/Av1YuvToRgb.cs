using System;
using System.Runtime.CompilerServices;

namespace FileFormat.Avif.Codec;

/// <summary>Converts YCbCr planes to RGB pixel data with support for BT.601, BT.709, and BT.2020 color matrices.
/// Handles both full-range and limited-range (studio swing) inputs.</summary>
internal static class Av1YuvToRgb {

  /// <summary>Converts YCbCr 4:2:0 planes to an interleaved RGB24 byte array.</summary>
  public static byte[] ConvertYuv420ToRgb(
    short[] yPlane, int yStride,
    short[] uPlane, int uStride,
    short[] vPlane, int vStride,
    int width, int height,
    int bitDepth,
    Av1MatrixCoefficients matrix,
    bool fullRange
  ) {
    var rgb = new byte[width * height * 3];
    var maxVal = (1 << bitDepth) - 1;
    var shift = bitDepth - 8;

    _GetColorMatrix(matrix, out var kr, out var kb);
    var kg = 1.0 - kr - kb;

    for (var y = 0; y < height; ++y) {
      var uvY = y >> 1;
      for (var x = 0; x < width; ++x) {
        var uvX = x >> 1;

        var yVal = yPlane[y * yStride + x];
        var uVal = uPlane[uvY * uStride + uvX];
        var vVal = vPlane[uvY * vStride + uvX];

        _YuvToRgb(yVal, uVal, vVal, bitDepth, fullRange, kr, kb, kg, out var r, out var g, out var b);

        var dstIdx = (y * width + x) * 3;
        rgb[dstIdx] = (byte)Math.Clamp(r >> shift, 0, 255);
        rgb[dstIdx + 1] = (byte)Math.Clamp(g >> shift, 0, 255);
        rgb[dstIdx + 2] = (byte)Math.Clamp(b >> shift, 0, 255);
      }
    }

    return rgb;
  }

  /// <summary>Converts YCbCr 4:4:4 planes to an interleaved RGB24 byte array.</summary>
  public static byte[] ConvertYuv444ToRgb(
    short[] yPlane, int yStride,
    short[] uPlane, int uStride,
    short[] vPlane, int vStride,
    int width, int height,
    int bitDepth,
    Av1MatrixCoefficients matrix,
    bool fullRange
  ) {
    var rgb = new byte[width * height * 3];
    var shift = bitDepth - 8;

    _GetColorMatrix(matrix, out var kr, out var kb);
    var kg = 1.0 - kr - kb;

    for (var y = 0; y < height; ++y) {
      for (var x = 0; x < width; ++x) {
        var yVal = yPlane[y * yStride + x];
        var uVal = uPlane[y * uStride + x];
        var vVal = vPlane[y * vStride + x];

        _YuvToRgb(yVal, uVal, vVal, bitDepth, fullRange, kr, kb, kg, out var r, out var g, out var b);

        var dstIdx = (y * width + x) * 3;
        rgb[dstIdx] = (byte)Math.Clamp(r >> shift, 0, 255);
        rgb[dstIdx + 1] = (byte)Math.Clamp(g >> shift, 0, 255);
        rgb[dstIdx + 2] = (byte)Math.Clamp(b >> shift, 0, 255);
      }
    }

    return rgb;
  }

  /// <summary>Converts monochrome Y plane to an interleaved RGB24 byte array.</summary>
  public static byte[] ConvertMonoToRgb(
    short[] yPlane, int yStride,
    int width, int height,
    int bitDepth,
    bool fullRange
  ) {
    var rgb = new byte[width * height * 3];
    var maxVal = (1 << bitDepth) - 1;
    var shift = bitDepth - 8;

    for (var y = 0; y < height; ++y) {
      for (var x = 0; x < width; ++x) {
        var yVal = yPlane[y * yStride + x];
        int gray;
        if (fullRange) {
          gray = yVal >> shift;
        } else {
          gray = ((yVal - (16 << (bitDepth - 8))) * 255 + (219 << (bitDepth - 9))) / (219 << (bitDepth - 8));
        }
        gray = Math.Clamp(gray, 0, 255);

        var dstIdx = (y * width + x) * 3;
        rgb[dstIdx] = (byte)gray;
        rgb[dstIdx + 1] = (byte)gray;
        rgb[dstIdx + 2] = (byte)gray;
      }
    }

    return rgb;
  }

  /// <summary>Converts identity matrix (RGB stored as YUV) to RGB24.</summary>
  public static byte[] ConvertIdentityToRgb(
    short[] gPlane, int gStride,
    short[] bPlane, int bStride,
    short[] rPlane, int rStride,
    int width, int height,
    int bitDepth
  ) {
    var rgb = new byte[width * height * 3];
    var shift = bitDepth - 8;

    for (var y = 0; y < height; ++y) {
      for (var x = 0; x < width; ++x) {
        var dstIdx = (y * width + x) * 3;
        rgb[dstIdx] = (byte)Math.Clamp(rPlane[y * rStride + x] >> shift, 0, 255);
        rgb[dstIdx + 1] = (byte)Math.Clamp(gPlane[y * gStride + x] >> shift, 0, 255);
        rgb[dstIdx + 2] = (byte)Math.Clamp(bPlane[y * bStride + x] >> shift, 0, 255);
      }
    }

    return rgb;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void _YuvToRgb(int yVal, int uVal, int vVal, int bitDepth, bool fullRange,
    double kr, double kb, double kg, out int r, out int g, out int b) {

    var mid = 1 << (bitDepth - 1);
    var maxVal = (1 << bitDepth) - 1;

    double yNorm, cbNorm, crNorm;

    if (fullRange) {
      yNorm = yVal;
      cbNorm = uVal - mid;
      crNorm = vVal - mid;
    } else {
      // Limited range: Y [16, 235], CbCr [16, 240] scaled to bit depth
      var yOffset = 16 << (bitDepth - 8);
      var cOffset = 128 << (bitDepth - 8);
      var yRange = 219.0 * (1 << (bitDepth - 8));
      var cRange = 224.0 * (1 << (bitDepth - 8));

      yNorm = (yVal - yOffset) * maxVal / yRange;
      cbNorm = (uVal - cOffset) * maxVal / cRange;
      crNorm = (vVal - cOffset) * maxVal / cRange;
    }

    // BT.xxx conversion:
    // R = Y + (2 - 2*kr) * Cr
    // G = Y - (2*kb*(1-kb)/kg) * Cb - (2*kr*(1-kr)/kg) * Cr
    // B = Y + (2 - 2*kb) * Cb
    r = (int)Math.Round(yNorm + (2 - 2 * kr) * crNorm);
    g = (int)Math.Round(yNorm - (2 * kb * (1 - kb) / kg) * cbNorm - (2 * kr * (1 - kr) / kg) * crNorm);
    b = (int)Math.Round(yNorm + (2 - 2 * kb) * cbNorm);

    r = Math.Clamp(r, 0, maxVal);
    g = Math.Clamp(g, 0, maxVal);
    b = Math.Clamp(b, 0, maxVal);
  }

  private static void _GetColorMatrix(Av1MatrixCoefficients mc, out double kr, out double kb) {
    switch (mc) {
      case Av1MatrixCoefficients.Bt709:
        kr = 0.2126; kb = 0.0722; break;
      case Av1MatrixCoefficients.Bt470Bg:
      case Av1MatrixCoefficients.Bt601:
        kr = 0.299; kb = 0.114; break;
      case Av1MatrixCoefficients.Bt2020Ncl:
      case Av1MatrixCoefficients.Bt2020Cl:
        kr = 0.2627; kb = 0.0593; break;
      case Av1MatrixCoefficients.Smpte240:
        kr = 0.212; kb = 0.087; break;
      default:
        // Default to BT.601
        kr = 0.299; kb = 0.114; break;
    }
  }
}
