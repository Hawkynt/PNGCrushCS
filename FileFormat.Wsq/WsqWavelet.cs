using System;

namespace FileFormat.Wsq;

/// <summary>Cohen-Daubechies-Feauveau 9/7 biorthogonal wavelet transform for WSQ using the lifting scheme.</summary>
internal static class WsqWavelet {

  /// <summary>Number of decomposition levels in WSQ.</summary>
  public const int NUM_LEVELS = 5;

  /// <summary>Total number of subbands: 1 LL + 3 per level.</summary>
  public const int NUM_SUBBANDS = 1 + 3 * NUM_LEVELS; // 16

  // CDF 9/7 lifting coefficients
  private const double _ALPHA = -1.586134342;
  private const double _BETA = -0.05298011854;
  private const double _GAMMA = 0.8829110762;
  private const double _DELTA = 0.4435068522;
  private const double _K = 1.1496043988602418;
  private const double _K_INV = 1.0 / _K;

  /// <summary>Describes the location and size of a subband within the coefficient array.</summary>
  public readonly record struct SubbandInfo(int X, int Y, int Width, int Height);

  /// <summary>Computes subband layout for the given image dimensions.</summary>
  public static SubbandInfo[] ComputeSubbandLayout(int width, int height) {
    var subbands = new SubbandInfo[NUM_SUBBANDS];
    var widths = new int[NUM_LEVELS + 1];
    var heights = new int[NUM_LEVELS + 1];
    widths[0] = width;
    heights[0] = height;

    for (var level = 0; level < NUM_LEVELS; ++level) {
      widths[level + 1] = (widths[level] + 1) / 2;
      heights[level + 1] = (heights[level] + 1) / 2;
    }

    // Subband 0: LL at deepest level
    subbands[0] = new(0, 0, widths[NUM_LEVELS], heights[NUM_LEVELS]);

    for (var level = NUM_LEVELS - 1; level >= 0; --level) {
      var idx = 1 + (NUM_LEVELS - 1 - level) * 3;
      var llW = widths[level + 1];
      var llH = heights[level + 1];
      var fullW = widths[level];
      var fullH = heights[level];

      // HL (top-right)
      subbands[idx] = new(llW, 0, fullW - llW, llH);
      // LH (bottom-left)
      subbands[idx + 1] = new(0, llH, llW, fullH - llH);
      // HH (bottom-right)
      subbands[idx + 2] = new(llW, llH, fullW - llW, fullH - llH);
    }

    return subbands;
  }

  /// <summary>Forward 2D DWT: decomposes pixel data into wavelet coefficients.</summary>
  public static double[] Forward2D(byte[] pixels, int width, int height) {
    var coeffs = new double[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      coeffs[i] = pixels[i];

    var w = width;
    var h = height;

    for (var level = 0; level < NUM_LEVELS; ++level) {
      _Forward2DLevel(coeffs, width, w, h);
      w = (w + 1) / 2;
      h = (h + 1) / 2;
    }

    return coeffs;
  }

  /// <summary>Inverse 2D DWT: reconstructs pixel data from wavelet coefficients.</summary>
  public static byte[] Inverse2D(double[] coeffs, int width, int height) {
    var result = new double[width * height];
    Array.Copy(coeffs, result, coeffs.Length);

    var widths = new int[NUM_LEVELS + 1];
    var heights = new int[NUM_LEVELS + 1];
    widths[0] = width;
    heights[0] = height;
    for (var level = 0; level < NUM_LEVELS; ++level) {
      widths[level + 1] = (widths[level] + 1) / 2;
      heights[level + 1] = (heights[level] + 1) / 2;
    }

    for (var level = NUM_LEVELS - 1; level >= 0; --level)
      _Inverse2DLevel(result, width, widths[level], heights[level]);

    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)Math.Clamp(Math.Round(result[i]), 0, 255);

    return pixels;
  }

  private static void _Forward2DLevel(double[] data, int stride, int w, int h) {
    var row = new double[w];

    // Transform rows
    for (var y = 0; y < h; ++y) {
      for (var x = 0; x < w; ++x)
        row[x] = data[y * stride + x];

      _Forward1DLifting(row, w);

      // Deinterleave: even samples (low) then odd samples (high)
      var loLen = (w + 1) / 2;
      var hiLen = w / 2;
      for (var x = 0; x < loLen; ++x)
        data[y * stride + x] = row[x * 2];
      for (var x = 0; x < hiLen; ++x)
        data[y * stride + loLen + x] = row[x * 2 + 1];
    }

    // Transform columns
    var col = new double[h];
    var loH = (h + 1) / 2;
    var hiH = h / 2;

    for (var x = 0; x < w; ++x) {
      for (var y = 0; y < h; ++y)
        col[y] = data[y * stride + x];

      _Forward1DLifting(col, h);

      // Deinterleave
      for (var y = 0; y < loH; ++y)
        data[y * stride + x] = col[y * 2];
      for (var y = 0; y < hiH; ++y)
        data[(loH + y) * stride + x] = col[y * 2 + 1];
    }
  }

  private static void _Inverse2DLevel(double[] data, int stride, int w, int h) {
    var loH = (h + 1) / 2;
    var hiH = h / 2;
    var loW = (w + 1) / 2;
    var hiW = w / 2;

    // Inverse columns
    var col = new double[h];
    for (var x = 0; x < w; ++x) {
      // Interleave: even from low, odd from high
      for (var y = 0; y < loH; ++y)
        col[y * 2] = data[y * stride + x];
      for (var y = 0; y < hiH; ++y)
        col[y * 2 + 1] = data[(loH + y) * stride + x];

      _Inverse1DLifting(col, h);

      for (var y = 0; y < h; ++y)
        data[y * stride + x] = col[y];
    }

    // Inverse rows
    var row = new double[w];
    for (var y = 0; y < h; ++y) {
      // Interleave
      for (var x = 0; x < loW; ++x)
        row[x * 2] = data[y * stride + x];
      for (var x = 0; x < hiW; ++x)
        row[x * 2 + 1] = data[y * stride + loW + x];

      _Inverse1DLifting(row, w);

      for (var x = 0; x < w; ++x)
        data[y * stride + x] = row[x];
    }
  }

  /// <summary>1D forward DWT using CDF 9/7 lifting scheme.</summary>
  internal static void _Forward1DLifting(double[] s, int n) {
    if (n < 2)
      return;

    // Step 1: Predict (alpha)
    for (var i = 1; i < n - 1; i += 2)
      s[i] += _ALPHA * (s[i - 1] + s[i + 1]);
    if (n % 2 == 0)
      s[n - 1] += 2 * _ALPHA * s[n - 2];

    // Step 2: Update (beta)
    s[0] += 2 * _BETA * s[1];
    for (var i = 2; i < n - 1; i += 2)
      s[i] += _BETA * (s[i - 1] + s[i + 1]);
    if (n % 2 != 0 && n > 1)
      s[n - 1] += 2 * _BETA * s[n - 2];

    // Step 3: Predict (gamma)
    for (var i = 1; i < n - 1; i += 2)
      s[i] += _GAMMA * (s[i - 1] + s[i + 1]);
    if (n % 2 == 0)
      s[n - 1] += 2 * _GAMMA * s[n - 2];

    // Step 4: Update (delta)
    s[0] += 2 * _DELTA * s[1];
    for (var i = 2; i < n - 1; i += 2)
      s[i] += _DELTA * (s[i - 1] + s[i + 1]);
    if (n % 2 != 0 && n > 1)
      s[n - 1] += 2 * _DELTA * s[n - 2];

    // Step 5: Scaling
    for (var i = 0; i < n; i += 2)
      s[i] *= _K_INV;
    for (var i = 1; i < n; i += 2)
      s[i] *= _K;
  }

  /// <summary>1D inverse DWT using CDF 9/7 lifting scheme (reverse of forward).</summary>
  internal static void _Inverse1DLifting(double[] s, int n) {
    if (n < 2)
      return;

    // Step 5 inverse: Undo scaling
    for (var i = 0; i < n; i += 2)
      s[i] *= _K;
    for (var i = 1; i < n; i += 2)
      s[i] *= _K_INV;

    // Step 4 inverse: Undo update (delta)
    s[0] -= 2 * _DELTA * s[1];
    for (var i = 2; i < n - 1; i += 2)
      s[i] -= _DELTA * (s[i - 1] + s[i + 1]);
    if (n % 2 != 0 && n > 1)
      s[n - 1] -= 2 * _DELTA * s[n - 2];

    // Step 3 inverse: Undo predict (gamma)
    for (var i = 1; i < n - 1; i += 2)
      s[i] -= _GAMMA * (s[i - 1] + s[i + 1]);
    if (n % 2 == 0)
      s[n - 1] -= 2 * _GAMMA * s[n - 2];

    // Step 2 inverse: Undo update (beta)
    s[0] -= 2 * _BETA * s[1];
    for (var i = 2; i < n - 1; i += 2)
      s[i] -= _BETA * (s[i - 1] + s[i + 1]);
    if (n % 2 != 0 && n > 1)
      s[n - 1] -= 2 * _BETA * s[n - 2];

    // Step 1 inverse: Undo predict (alpha)
    for (var i = 1; i < n - 1; i += 2)
      s[i] -= _ALPHA * (s[i - 1] + s[i + 1]);
    if (n % 2 == 0)
      s[n - 1] -= 2 * _ALPHA * s[n - 2];
  }

  /// <summary>1D forward DWT wrapper that returns separated low/high subbands.</summary>
  internal static void _Forward1D(double[] input, int length, out double[] low, out double[] high) {
    var s = new double[length];
    Array.Copy(input, s, length);
    _Forward1DLifting(s, length);

    var loLen = (length + 1) / 2;
    var hiLen = length / 2;
    low = new double[loLen];
    high = new double[hiLen];

    for (var i = 0; i < loLen; ++i)
      low[i] = s[i * 2];
    for (var i = 0; i < hiLen; ++i)
      high[i] = s[i * 2 + 1];
  }

  /// <summary>1D inverse DWT wrapper that takes separated low/high subbands.</summary>
  internal static void _Inverse1D(double[] low, double[] high, int length, double[] output) {
    // Interleave
    for (var i = 0; i < low.Length; ++i)
      output[i * 2] = low[i];
    for (var i = 0; i < high.Length; ++i)
      output[i * 2 + 1] = high[i];

    _Inverse1DLifting(output, length);
  }

  /// <summary>Mirror-symmetric boundary extension.</summary>
  internal static double _MirrorIndex(double[] data, int idx, int length) {
    if (length <= 1)
      return data[0];

    if (idx < 0)
      idx = -idx;
    if (idx >= length)
      idx = 2 * (length - 1) - idx;

    idx = Math.Clamp(idx, 0, length - 1);
    return data[idx];
  }
}
