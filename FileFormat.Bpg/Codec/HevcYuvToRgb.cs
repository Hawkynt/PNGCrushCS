using System;

namespace FileFormat.Bpg.Codec;

/// <summary>YCbCr to RGB color space conversion for HEVC decoded frames (BT.601 and BT.709).</summary>
internal static class HevcYuvToRgb {

  /// <summary>Converts decoded YCbCr planes to interleaved RGB24 pixel data.</summary>
  /// <param name="yPlane">Luma plane samples.</param>
  /// <param name="cbPlane">Cb (blue-difference) chroma plane samples.</param>
  /// <param name="crPlane">Cr (red-difference) chroma plane samples.</param>
  /// <param name="yStride">Luma plane stride.</param>
  /// <param name="cStride">Chroma plane stride.</param>
  /// <param name="width">Image width in pixels.</param>
  /// <param name="height">Image height in pixels.</param>
  /// <param name="chromaFormat">Chroma subsampling format.</param>
  /// <param name="colorSpace">Color space for conversion coefficients.</param>
  /// <param name="bitDepth">Bit depth of input samples.</param>
  /// <param name="limitedRange">Whether input uses limited (studio) range.</param>
  /// <returns>RGB24 interleaved pixel data (3 bytes per pixel).</returns>
  public static byte[] Convert(
    int[] yPlane, int[] cbPlane, int[] crPlane,
    int yStride, int cStride,
    int width, int height,
    BpgPixelFormat chromaFormat,
    BpgColorSpace colorSpace,
    int bitDepth,
    bool limitedRange
  ) {
    var rgb = new byte[width * height * 3];
    _GetCoefficients(colorSpace, out var kr, out var kb);

    var maxVal = (1 << bitDepth) - 1;
    var halfRange = 1 << (bitDepth - 1);

    // Chroma subsampling ratios
    int chromaSubX, chromaSubY;
    switch (chromaFormat) {
      case BpgPixelFormat.YCbCr420:
      case BpgPixelFormat.YCbCr420Jpeg:
        chromaSubX = 2;
        chromaSubY = 2;
        break;
      case BpgPixelFormat.YCbCr422:
        chromaSubX = 2;
        chromaSubY = 1;
        break;
      case BpgPixelFormat.YCbCr444:
        chromaSubX = 1;
        chromaSubY = 1;
        break;
      default:
        throw new NotSupportedException($"Chroma format {chromaFormat} not supported for YCbCr conversion.");
    }

    // Pre-compute chroma width/height
    var chromaWidth = (width + chromaSubX - 1) / chromaSubX;
    var chromaHeight = (height + chromaSubY - 1) / chromaSubY;

    for (var y = 0; y < height; ++y) {
      var cy = Math.Min(y / chromaSubY, chromaHeight - 1);
      for (var x = 0; x < width; ++x) {
        var cx = Math.Min(x / chromaSubX, chromaWidth - 1);

        var yIdx = y * yStride + x;
        var cIdx = cy * cStride + cx;

        var yVal = yPlane[yIdx];
        var cbVal = cbPlane.Length > cIdx ? cbPlane[cIdx] : halfRange;
        var crVal = crPlane.Length > cIdx ? crPlane[cIdx] : halfRange;

        double rD, gD, bD;

        if (limitedRange) {
          // Limited range: Y [16..235], Cb/Cr [16..240] for 8-bit
          var yScale = 1 << (bitDepth - 8);
          var yNorm = (yVal - 16 * yScale) / (double)(219 * yScale);
          var cbNorm = (cbVal - halfRange) / (double)(224 * yScale);
          var crNorm = (crVal - halfRange) / (double)(224 * yScale);

          rD = yNorm + (2.0 - 2.0 * kr) * crNorm;
          gD = yNorm - (2.0 * kb * (1.0 - kb) / (1.0 - kr - kb)) * cbNorm - (2.0 * kr * (1.0 - kr) / (1.0 - kr - kb)) * crNorm;
          bD = yNorm + (2.0 - 2.0 * kb) * cbNorm;
        } else {
          // Full range
          var yNorm = yVal / (double)maxVal;
          var cbNorm = (cbVal - halfRange) / (double)maxVal;
          var crNorm = (crVal - halfRange) / (double)maxVal;

          rD = yNorm + (2.0 - 2.0 * kr) * crNorm;
          gD = yNorm - (2.0 * kb * (1.0 - kb) / (1.0 - kr - kb)) * cbNorm - (2.0 * kr * (1.0 - kr) / (1.0 - kr - kb)) * crNorm;
          bD = yNorm + (2.0 - 2.0 * kb) * cbNorm;
        }

        var rgbIdx = (y * width + x) * 3;
        rgb[rgbIdx] = _ClampToByte(rD * 255.0);
        rgb[rgbIdx + 1] = _ClampToByte(gD * 255.0);
        rgb[rgbIdx + 2] = _ClampToByte(bD * 255.0);
      }
    }

    return rgb;
  }

  /// <summary>Converts a grayscale (luma-only) plane to Gray8 pixel data.</summary>
  public static byte[] ConvertGrayscale(int[] yPlane, int yStride, int width, int height, int bitDepth, bool limitedRange) {
    var gray = new byte[width * height];
    var maxVal = (1 << bitDepth) - 1;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var yVal = yPlane[y * yStride + x];
        double norm;

        if (limitedRange) {
          var yScale = 1 << (bitDepth - 8);
          norm = (yVal - 16 * yScale) / (double)(219 * yScale);
        } else {
          norm = yVal / (double)maxVal;
        }

        gray[y * width + x] = _ClampToByte(norm * 255.0);
      }

    return gray;
  }

  /// <summary>Converts decoded YCbCr planes to RGBA32 pixel data (with alpha plane).</summary>
  public static byte[] ConvertWithAlpha(
    int[] yPlane, int[] cbPlane, int[] crPlane, int[] alphaPlane,
    int yStride, int cStride, int alphaStride,
    int width, int height,
    BpgPixelFormat chromaFormat,
    BpgColorSpace colorSpace,
    int bitDepth,
    bool limitedRange
  ) {
    var rgbData = Convert(yPlane, cbPlane, crPlane, yStride, cStride, width, height, chromaFormat, colorSpace, bitDepth, limitedRange);
    var rgba = new byte[width * height * 4];
    var maxVal = (1 << bitDepth) - 1;

    for (var i = 0; i < width * height; ++i) {
      rgba[i * 4] = rgbData[i * 3];
      rgba[i * 4 + 1] = rgbData[i * 3 + 1];
      rgba[i * 4 + 2] = rgbData[i * 3 + 2];

      var alphaVal = i < alphaPlane.Length ? alphaPlane[i] : maxVal;
      rgba[i * 4 + 3] = (byte)Math.Clamp(alphaVal * 255 / maxVal, 0, 255);
    }

    return rgba;
  }

  private static void _GetCoefficients(BpgColorSpace colorSpace, out double kr, out double kb) {
    switch (colorSpace) {
      case BpgColorSpace.YCbCrBT601:
        kr = 0.299;
        kb = 0.114;
        break;
      case BpgColorSpace.YCbCrBT709:
        kr = 0.2126;
        kb = 0.0722;
        break;
      case BpgColorSpace.YCbCrBT2020:
      case BpgColorSpace.YCbCrBT2020NCL:
        kr = 0.2627;
        kb = 0.0593;
        break;
      case BpgColorSpace.Rgb:
        // Direct RGB storage, no conversion needed (identity)
        kr = 0.0;
        kb = 0.0;
        break;
      default:
        // Default to BT.709
        kr = 0.2126;
        kb = 0.0722;
        break;
    }
  }

  private static byte _ClampToByte(double val) => (byte)Math.Clamp((int)(val + 0.5), 0, 255);
}
