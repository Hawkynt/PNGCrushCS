using System;

namespace FileFormat.CameraRaw;

/// <summary>Bayer CFA demosaicing algorithms.</summary>
internal static class BayerDemosaic {

  // Color channel indices
  private const int _RED = 0;
  private const int _GREEN = 1;
  private const int _BLUE = 2;

  /// <summary>Demosaic a Bayer CFA image using bilinear interpolation.</summary>
  /// <param name="raw">Single-channel Bayer data (one byte per pixel, 8-bit or scaled to 8-bit).</param>
  /// <param name="width">Image width in pixels.</param>
  /// <param name="height">Image height in pixels.</param>
  /// <param name="pattern">Bayer CFA pattern.</param>
  /// <returns>RGB24 pixel data (3 bytes per pixel, row-major).</returns>
  public static byte[] Bilinear(byte[] raw, int width, int height, BayerPattern pattern) {
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var outIdx = (y * width + x) * 3;
        var color = GetColorAt(x, y, pattern);

        switch (color) {
          case _RED:
            rgb[outIdx] = _SampleClamped(raw, x, y, width, height);
            rgb[outIdx + 1] = _BilinearGreenAtRb(raw, x, y, width, height);
            rgb[outIdx + 2] = _BilinearDiagonal(raw, x, y, width, height);
            break;
          case _GREEN: {
            var isGreenOnRedRow = _IsGreenOnRedRow(x, y, pattern);
            if (isGreenOnRedRow) {
              rgb[outIdx] = _BilinearHorizontal(raw, x, y, width, height);
              rgb[outIdx + 1] = _SampleClamped(raw, x, y, width, height);
              rgb[outIdx + 2] = _BilinearVertical(raw, x, y, width, height);
            } else {
              rgb[outIdx] = _BilinearVertical(raw, x, y, width, height);
              rgb[outIdx + 1] = _SampleClamped(raw, x, y, width, height);
              rgb[outIdx + 2] = _BilinearHorizontal(raw, x, y, width, height);
            }

            break;
          }
          case _BLUE:
            rgb[outIdx] = _BilinearDiagonal(raw, x, y, width, height);
            rgb[outIdx + 1] = _BilinearGreenAtRb(raw, x, y, width, height);
            rgb[outIdx + 2] = _SampleClamped(raw, x, y, width, height);
            break;
        }
      }

    return rgb;
  }

  /// <summary>Demosaic using gradient-corrected interpolation for higher quality.</summary>
  /// <param name="raw">Single-channel Bayer data (one byte per pixel).</param>
  /// <param name="width">Image width in pixels.</param>
  /// <param name="height">Image height in pixels.</param>
  /// <param name="pattern">Bayer CFA pattern.</param>
  /// <returns>RGB24 pixel data (3 bytes per pixel, row-major).</returns>
  public static byte[] Ahd(byte[] raw, int width, int height, BayerPattern pattern) {
    // Step 1: Interpolate G at R/B positions using gradient-corrected bilinear.
    //         At G positions the value is known.
    var green = new float[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var idx = y * width + x;
        if (GetColorAt(x, y, pattern) == _GREEN) {
          green[idx] = raw[idx];
          continue;
        }

        // Horizontal gradient: |G_left - G_right| + |C_center - C_left2 + C_center - C_right2| (second derivative of known channel)
        var gL = _SampleClampedF(raw, x - 1, y, width, height);
        var gR = _SampleClampedF(raw, x + 1, y, width, height);
        var gU = _SampleClampedF(raw, x, y - 1, width, height);
        var gD = _SampleClampedF(raw, x, y + 1, width, height);

        var c = (float)raw[idx];
        var cL2 = _SampleClampedF(raw, x - 2, y, width, height);
        var cR2 = _SampleClampedF(raw, x + 2, y, width, height);
        var cU2 = _SampleClampedF(raw, x, y - 2, width, height);
        var cD2 = _SampleClampedF(raw, x, y + 2, width, height);

        var hGrad = MathF.Abs(gL - gR) + MathF.Abs(2 * c - cL2 - cR2);
        var vGrad = MathF.Abs(gU - gD) + MathF.Abs(2 * c - cU2 - cD2);

        // Gradient-corrected interpolation with second-derivative correction
        var hVal = (gL + gR) * 0.5f + (2 * c - cL2 - cR2) * 0.25f;
        var vVal = (gU + gD) * 0.5f + (2 * c - cU2 - cD2) * 0.25f;

        if (hGrad < vGrad)
          green[idx] = hVal;
        else if (vGrad < hGrad)
          green[idx] = vVal;
        else
          green[idx] = (hVal + vVal) * 0.5f;
      }

    // Step 2: Interpolate R and B using color-difference (R-G, B-G) interpolation.
    //         The difference R-G and B-G is smoother than R/B alone, producing fewer artifacts.
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var idx = y * width + x;
        var outIdx = idx * 3;
        var color = GetColorAt(x, y, pattern);
        var g = green[idx];

        float r, b;
        switch (color) {
          case _RED:
            r = raw[idx];
            b = _InterpolateColorDifference(raw, green, x, y, width, height, _BLUE, pattern);
            break;
          case _GREEN: {
            r = _InterpolateColorDifference(raw, green, x, y, width, height, _RED, pattern);
            b = _InterpolateColorDifference(raw, green, x, y, width, height, _BLUE, pattern);
            break;
          }
          default: // BLUE
            r = _InterpolateColorDifference(raw, green, x, y, width, height, _RED, pattern);
            b = raw[idx];
            break;
        }

        rgb[outIdx] = _Clamp(r);
        rgb[outIdx + 1] = _Clamp(g);
        rgb[outIdx + 2] = _Clamp(b);
      }

    return rgb;
  }

  /// <summary>Get the color channel (0=Red, 1=Green, 2=Blue) at position (x, y) for a given pattern.</summary>
  internal static int GetColorAt(int x, int y, BayerPattern pattern) {
    var px = x & 1;
    var py = y & 1;
    return pattern switch {
      // RGGB: (0,0)=R (1,0)=G (0,1)=G (1,1)=B
      BayerPattern.RGGB => (py, px) switch { (0, 0) => _RED, (0, 1) => _GREEN, (1, 0) => _GREEN, _ => _BLUE },
      // BGGR: (0,0)=B (1,0)=G (0,1)=G (1,1)=R
      BayerPattern.BGGR => (py, px) switch { (0, 0) => _BLUE, (0, 1) => _GREEN, (1, 0) => _GREEN, _ => _RED },
      // GRBG: (0,0)=G (1,0)=R (0,1)=B (1,1)=G
      BayerPattern.GRBG => (py, px) switch { (0, 0) => _GREEN, (0, 1) => _RED, (1, 0) => _BLUE, _ => _GREEN },
      // GBRG: (0,0)=G (1,0)=B (0,1)=R (1,1)=G
      BayerPattern.GBRG => (py, px) switch { (0, 0) => _GREEN, (0, 1) => _BLUE, (1, 0) => _RED, _ => _GREEN },
      _ => _GREEN
    };
  }

  private static bool _IsGreenOnRedRow(int x, int y, BayerPattern pattern) =>
    pattern switch {
      // RGGB: green on red row is at (1,0), i.e. y%2==0
      BayerPattern.RGGB => (y & 1) == 0,
      // BGGR: green on blue row at y%2==0, so green on red row at y%2==1
      BayerPattern.BGGR => (y & 1) == 1,
      // GRBG: row 0 has G,R so green on red row at y%2==0
      BayerPattern.GRBG => (y & 1) == 0,
      // GBRG: row 0 has G,B so green on red row at y%2==1
      BayerPattern.GBRG => (y & 1) == 1,
      _ => true
    };

  private static byte _Clamp(float v) => (byte)(v < 0f ? 0 : v > 255f ? 255 : (int)(v + 0.5f));

  private static byte _SampleClamped(byte[] raw, int x, int y, int width, int height) {
    x = Math.Clamp(x, 0, width - 1);
    y = Math.Clamp(y, 0, height - 1);
    return raw[y * width + x];
  }

  private static float _SampleClampedF(byte[] raw, int x, int y, int width, int height) {
    x = Math.Clamp(x, 0, width - 1);
    y = Math.Clamp(y, 0, height - 1);
    return raw[y * width + x];
  }

  /// <summary>Average of 4 cardinal neighbors (up/down/left/right) for green at R/B positions.</summary>
  private static byte _BilinearGreenAtRb(byte[] raw, int x, int y, int width, int height) {
    var sum =
      _SampleClamped(raw, x - 1, y, width, height)
      + _SampleClamped(raw, x + 1, y, width, height)
      + _SampleClamped(raw, x, y - 1, width, height)
      + _SampleClamped(raw, x, y + 1, width, height);
    return (byte)((sum + 2) / 4);
  }

  /// <summary>Average of 4 diagonal neighbors for R at B or B at R positions.</summary>
  private static byte _BilinearDiagonal(byte[] raw, int x, int y, int width, int height) {
    var sum =
      _SampleClamped(raw, x - 1, y - 1, width, height)
      + _SampleClamped(raw, x + 1, y - 1, width, height)
      + _SampleClamped(raw, x - 1, y + 1, width, height)
      + _SampleClamped(raw, x + 1, y + 1, width, height);
    return (byte)((sum + 2) / 4);
  }

  /// <summary>Average of left and right neighbors.</summary>
  private static byte _BilinearHorizontal(byte[] raw, int x, int y, int width, int height) {
    var sum = _SampleClamped(raw, x - 1, y, width, height) + _SampleClamped(raw, x + 1, y, width, height);
    return (byte)((sum + 1) / 2);
  }

  /// <summary>Average of up and down neighbors.</summary>
  private static byte _BilinearVertical(byte[] raw, int x, int y, int width, int height) {
    var sum = _SampleClamped(raw, x, y - 1, width, height) + _SampleClamped(raw, x, y + 1, width, height);
    return (byte)((sum + 1) / 2);
  }

  /// <summary>Interpolate a missing color channel at (x,y) using color-difference method with the green channel.</summary>
  private static float _InterpolateColorDifference(byte[] raw, float[] green, int x, int y, int width, int height, int targetColor, BayerPattern pattern) {
    var g = green[y * width + x];
    var myColor = GetColorAt(x, y, pattern);

    if (myColor == targetColor)
      return raw[y * width + x];

    // Collect (raw - green) differences from neighboring pixels that have the target color
    float sum = 0;
    var count = 0;

    if (myColor == _GREEN) {
      // At a green pixel, the target color (R or B) is at specific neighbors
      // Check which direction has the target color
      // For R or B, the neighbors are either horizontal or vertical depending on which green this is
      var isGreenOnRedRow = _IsGreenOnRedRow(x, y, pattern);
      if ((targetColor == _RED && isGreenOnRedRow) || (targetColor == _BLUE && !isGreenOnRedRow)) {
        // Target is on this row, so left/right neighbors have it
        _AccumulateDiff(raw, green, x - 1, y, width, height, targetColor, pattern, ref sum, ref count);
        _AccumulateDiff(raw, green, x + 1, y, width, height, targetColor, pattern, ref sum, ref count);
      } else {
        // Target is on columns above/below
        _AccumulateDiff(raw, green, x, y - 1, width, height, targetColor, pattern, ref sum, ref count);
        _AccumulateDiff(raw, green, x, y + 1, width, height, targetColor, pattern, ref sum, ref count);
      }
    } else {
      // At R or B, the other (B or R) is at diagonal positions
      _AccumulateDiff(raw, green, x - 1, y - 1, width, height, targetColor, pattern, ref sum, ref count);
      _AccumulateDiff(raw, green, x + 1, y - 1, width, height, targetColor, pattern, ref sum, ref count);
      _AccumulateDiff(raw, green, x - 1, y + 1, width, height, targetColor, pattern, ref sum, ref count);
      _AccumulateDiff(raw, green, x + 1, y + 1, width, height, targetColor, pattern, ref sum, ref count);
    }

    if (count == 0)
      return g;

    return g + sum / count;
  }

  private static void _AccumulateDiff(byte[] raw, float[] green, int nx, int ny, int width, int height, int targetColor, BayerPattern pattern, ref float sum, ref int count) {
    var cx = Math.Clamp(nx, 0, width - 1);
    var cy = Math.Clamp(ny, 0, height - 1);
    if (GetColorAt(cx, cy, pattern) != targetColor)
      return;

    var idx = cy * width + cx;
    sum += raw[idx] - green[idx];
    ++count;
  }

  // --- 16-bit ushort[] overloads for high-precision CFA data ---

  /// <summary>Demosaic a Bayer CFA image from 16-bit ushort data using AHD, then scale to 8-bit RGB24.</summary>
  /// <param name="raw">Single-channel Bayer data (one ushort per pixel).</param>
  /// <param name="width">Image width in pixels.</param>
  /// <param name="height">Image height in pixels.</param>
  /// <param name="pattern">Bayer CFA pattern.</param>
  /// <param name="maxValue">Maximum sample value (e.g., 4095 for 12-bit, 16383 for 14-bit, 65535 for 16-bit).</param>
  /// <returns>RGB24 pixel data (3 bytes per pixel, row-major).</returns>
  public static byte[] AhdUInt16(ushort[] raw, int width, int height, BayerPattern pattern, int maxValue) {
    // Step 1: Interpolate G at R/B positions using gradient-corrected bilinear.
    var green = new float[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var idx = y * width + x;
        if (GetColorAt(x, y, pattern) == _GREEN) {
          green[idx] = raw[idx];
          continue;
        }

        var gL = _SampleClampedF16(raw, x - 1, y, width, height);
        var gR = _SampleClampedF16(raw, x + 1, y, width, height);
        var gU = _SampleClampedF16(raw, x, y - 1, width, height);
        var gD = _SampleClampedF16(raw, x, y + 1, width, height);

        var c = (float)raw[idx];
        var cL2 = _SampleClampedF16(raw, x - 2, y, width, height);
        var cR2 = _SampleClampedF16(raw, x + 2, y, width, height);
        var cU2 = _SampleClampedF16(raw, x, y - 2, width, height);
        var cD2 = _SampleClampedF16(raw, x, y + 2, width, height);

        var hGrad = MathF.Abs(gL - gR) + MathF.Abs(2 * c - cL2 - cR2);
        var vGrad = MathF.Abs(gU - gD) + MathF.Abs(2 * c - cU2 - cD2);

        var hVal = (gL + gR) * 0.5f + (2 * c - cL2 - cR2) * 0.25f;
        var vVal = (gU + gD) * 0.5f + (2 * c - cU2 - cD2) * 0.25f;

        if (hGrad < vGrad)
          green[idx] = hVal;
        else if (vGrad < hGrad)
          green[idx] = vVal;
        else
          green[idx] = (hVal + vVal) * 0.5f;
      }

    // Step 2: Interpolate R and B, then scale to 8-bit.
    var scale = maxValue > 0 ? 255.0f / maxValue : 1.0f;
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var idx = y * width + x;
        var outIdx = idx * 3;
        var color = GetColorAt(x, y, pattern);
        var g = green[idx];

        float r, b;
        switch (color) {
          case _RED:
            r = raw[idx];
            b = _InterpolateColorDifference16(raw, green, x, y, width, height, _BLUE, pattern);
            break;
          case _GREEN:
            r = _InterpolateColorDifference16(raw, green, x, y, width, height, _RED, pattern);
            b = _InterpolateColorDifference16(raw, green, x, y, width, height, _BLUE, pattern);
            break;
          default:
            r = _InterpolateColorDifference16(raw, green, x, y, width, height, _RED, pattern);
            b = raw[idx];
            break;
        }

        rgb[outIdx] = _ClampScaled(r, scale);
        rgb[outIdx + 1] = _ClampScaled(g, scale);
        rgb[outIdx + 2] = _ClampScaled(b, scale);
      }

    return rgb;
  }

  private static byte _ClampScaled(float v, float scale) {
    var scaled = v * scale;
    return (byte)(scaled < 0f ? 0 : scaled > 255f ? 255 : (int)(scaled + 0.5f));
  }

  private static float _SampleClampedF16(ushort[] raw, int x, int y, int width, int height) {
    x = Math.Clamp(x, 0, width - 1);
    y = Math.Clamp(y, 0, height - 1);
    return raw[y * width + x];
  }

  private static float _InterpolateColorDifference16(ushort[] raw, float[] green, int x, int y, int width, int height, int targetColor, BayerPattern pattern) {
    var g = green[y * width + x];
    var myColor = GetColorAt(x, y, pattern);

    if (myColor == targetColor)
      return raw[y * width + x];

    float sum = 0;
    var count = 0;

    if (myColor == _GREEN) {
      var isGreenOnRedRow = _IsGreenOnRedRow(x, y, pattern);
      if ((targetColor == _RED && isGreenOnRedRow) || (targetColor == _BLUE && !isGreenOnRedRow)) {
        _AccumulateDiff16(raw, green, x - 1, y, width, height, targetColor, pattern, ref sum, ref count);
        _AccumulateDiff16(raw, green, x + 1, y, width, height, targetColor, pattern, ref sum, ref count);
      } else {
        _AccumulateDiff16(raw, green, x, y - 1, width, height, targetColor, pattern, ref sum, ref count);
        _AccumulateDiff16(raw, green, x, y + 1, width, height, targetColor, pattern, ref sum, ref count);
      }
    } else {
      _AccumulateDiff16(raw, green, x - 1, y - 1, width, height, targetColor, pattern, ref sum, ref count);
      _AccumulateDiff16(raw, green, x + 1, y - 1, width, height, targetColor, pattern, ref sum, ref count);
      _AccumulateDiff16(raw, green, x - 1, y + 1, width, height, targetColor, pattern, ref sum, ref count);
      _AccumulateDiff16(raw, green, x + 1, y + 1, width, height, targetColor, pattern, ref sum, ref count);
    }

    if (count == 0)
      return g;

    return g + sum / count;
  }

  private static void _AccumulateDiff16(ushort[] raw, float[] green, int nx, int ny, int width, int height, int targetColor, BayerPattern pattern, ref float sum, ref int count) {
    var cx = Math.Clamp(nx, 0, width - 1);
    var cy = Math.Clamp(ny, 0, height - 1);
    if (GetColorAt(cx, cy, pattern) != targetColor)
      return;

    var idx = cy * width + cx;
    sum += raw[idx] - green[idx];
    ++count;
  }
}
