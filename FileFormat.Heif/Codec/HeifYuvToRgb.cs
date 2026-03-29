using System;
using System.Runtime.CompilerServices;

namespace FileFormat.Heif.Codec;

/// <summary>Converts YCbCr planes to RGB24 pixel data for HEVC-decoded HEIF images.
/// Supports BT.601, BT.709, and BT.2020 color matrices with full/limited range.</summary>
internal static class HeifYuvToRgb {

  /// <summary>Converts YCbCr 4:2:0 planes to interleaved RGB24.</summary>
  public static byte[] ConvertYuv420ToRgb(
    short[] yPlane, int yStride,
    short[] uPlane, int uStride,
    short[] vPlane, int vStride,
    int width, int height,
    int bitDepth,
    int matrixCoeffs,
    bool fullRange
  ) {
    var rgb = new byte[width * height * 3];
    var shift = bitDepth - 8;
    _GetColorMatrix(matrixCoeffs, out var kr, out var kb);
    var kg = 1.0 - kr - kb;
    var maxVal = (1 << bitDepth) - 1;

    for (var y = 0; y < height; ++y) {
      var uvY = y >> 1;
      for (var x = 0; x < width; ++x) {
        var uvX = x >> 1;
        var yVal = yPlane[y * yStride + x];
        var uVal = uPlane[uvY * uStride + uvX];
        var vVal = vPlane[uvY * vStride + uvX];

        _YuvToRgb(yVal, uVal, vVal, bitDepth, fullRange, kr, kb, kg, maxVal, out var r, out var g, out var b);

        var idx = (y * width + x) * 3;
        rgb[idx] = (byte)Math.Clamp(r >> shift, 0, 255);
        rgb[idx + 1] = (byte)Math.Clamp(g >> shift, 0, 255);
        rgb[idx + 2] = (byte)Math.Clamp(b >> shift, 0, 255);
      }
    }
    return rgb;
  }

  /// <summary>Converts YCbCr 4:4:4 planes to interleaved RGB24.</summary>
  public static byte[] ConvertYuv444ToRgb(
    short[] yPlane, int yStride,
    short[] uPlane, int uStride,
    short[] vPlane, int vStride,
    int width, int height,
    int bitDepth,
    int matrixCoeffs,
    bool fullRange
  ) {
    var rgb = new byte[width * height * 3];
    var shift = bitDepth - 8;
    _GetColorMatrix(matrixCoeffs, out var kr, out var kb);
    var kg = 1.0 - kr - kb;
    var maxVal = (1 << bitDepth) - 1;

    for (var y = 0; y < height; ++y) {
      for (var x = 0; x < width; ++x) {
        var yVal = yPlane[y * yStride + x];
        var uVal = uPlane[y * uStride + x];
        var vVal = vPlane[y * vStride + x];

        _YuvToRgb(yVal, uVal, vVal, bitDepth, fullRange, kr, kb, kg, maxVal, out var r, out var g, out var b);

        var idx = (y * width + x) * 3;
        rgb[idx] = (byte)Math.Clamp(r >> shift, 0, 255);
        rgb[idx + 1] = (byte)Math.Clamp(g >> shift, 0, 255);
        rgb[idx + 2] = (byte)Math.Clamp(b >> shift, 0, 255);
      }
    }
    return rgb;
  }

  /// <summary>Converts monochrome Y plane to RGB24.</summary>
  public static byte[] ConvertMonoToRgb(
    short[] yPlane, int yStride,
    int width, int height,
    int bitDepth,
    bool fullRange
  ) {
    var rgb = new byte[width * height * 3];
    var shift = bitDepth - 8;

    for (var y = 0; y < height; ++y) {
      for (var x = 0; x < width; ++x) {
        var yVal = yPlane[y * yStride + x];
        int gray;
        if (fullRange)
          gray = yVal >> shift;
        else
          gray = ((yVal - (16 << (bitDepth - 8))) * 255 + (219 << (bitDepth - 9))) / (219 << (bitDepth - 8));

        gray = Math.Clamp(gray, 0, 255);
        var idx = (y * width + x) * 3;
        rgb[idx] = (byte)gray;
        rgb[idx + 1] = (byte)gray;
        rgb[idx + 2] = (byte)gray;
      }
    }
    return rgb;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void _YuvToRgb(int yVal, int uVal, int vVal, int bitDepth, bool fullRange,
    double kr, double kb, double kg, int maxVal, out int r, out int g, out int b) {

    var mid = 1 << (bitDepth - 1);
    double yNorm, cbNorm, crNorm;

    if (fullRange) {
      yNorm = yVal;
      cbNorm = uVal - mid;
      crNorm = vVal - mid;
    } else {
      var yOffset = 16 << (bitDepth - 8);
      var cOffset = 128 << (bitDepth - 8);
      var yRange = 219.0 * (1 << (bitDepth - 8));
      var cRange = 224.0 * (1 << (bitDepth - 8));

      yNorm = (yVal - yOffset) * maxVal / yRange;
      cbNorm = (uVal - cOffset) * maxVal / cRange;
      crNorm = (vVal - cOffset) * maxVal / cRange;
    }

    r = (int)Math.Round(yNorm + (2 - 2 * kr) * crNorm);
    g = (int)Math.Round(yNorm - (2 * kb * (1 - kb) / kg) * cbNorm - (2 * kr * (1 - kr) / kg) * crNorm);
    b = (int)Math.Round(yNorm + (2 - 2 * kb) * cbNorm);

    r = Math.Clamp(r, 0, maxVal);
    g = Math.Clamp(g, 0, maxVal);
    b = Math.Clamp(b, 0, maxVal);
  }

  private static void _GetColorMatrix(int mc, out double kr, out double kb) {
    switch (mc) {
      case 1: // BT.709
        kr = 0.2126; kb = 0.0722; break;
      case 5: // BT.470BG
      case 6: // BT.601
        kr = 0.299; kb = 0.114; break;
      case 9:  // BT.2020 NCL
      case 10: // BT.2020 CL
        kr = 0.2627; kb = 0.0593; break;
      default:
        kr = 0.299; kb = 0.114; break; // Default BT.601
    }
  }
}
