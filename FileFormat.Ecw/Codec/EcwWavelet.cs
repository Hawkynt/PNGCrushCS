using System;

namespace FileFormat.Ecw.Codec;

/// <summary>
/// CDF 9/7 biorthogonal wavelet transform using the lifting scheme.
/// This is the same irreversible wavelet used by JPEG 2000 and ECW.
/// Lifting steps: predict(-1.586134), update(-0.05298), predict(0.882911), update(0.443507), scale(1.149604).
/// </summary>
internal static class EcwWavelet {

  // CDF 9/7 lifting coefficients (Cohen-Daubechies-Feauveau)
  private const double _ALPHA = -1.5861343420693648;
  private const double _BETA = -0.0529801185718856;
  private const double _GAMMA = 0.8829110755411875;
  private const double _DELTA = 0.4435068520439540;
  private const double _K = 1.1496043988602418;
  private const double _K_INV = 1.0 / _K;

  /// <summary>
  /// Performs the forward 2D CDF 9/7 wavelet transform in-place for the given number of decomposition levels.
  /// Data is stored in row-major order with the given stride (which may exceed the active width due to padding).
  /// </summary>
  public static void Forward2D(double[] data, int stride, int width, int height, int levels) {
    for (var level = 0; level < levels; ++level) {
      var w = width >> level;
      var h = height >> level;
      if (w <= 1 && h <= 1)
        break;

      if (w > 1)
        _ForwardRows(data, stride, w, h);
      if (h > 1)
        _ForwardColumns(data, stride, w, h);
    }
  }

  /// <summary>
  /// Performs the inverse 2D CDF 9/7 wavelet transform in-place for the given number of decomposition levels.
  /// </summary>
  public static void Inverse2D(double[] data, int stride, int width, int height, int levels) {
    for (var level = levels - 1; level >= 0; --level) {
      var w = width >> level;
      var h = height >> level;
      if (w <= 1 && h <= 1)
        continue;

      if (h > 1)
        _InverseColumns(data, stride, w, h);
      if (w > 1)
        _InverseRows(data, stride, w, h);
    }
  }

  /// <summary>
  /// Performs the forward 1D CDF 9/7 transform using the lifting scheme.
  /// Input is in interleaved order; output has even samples (low-pass) in the first half
  /// and odd samples (high-pass) in the second half.
  /// </summary>
  public static void Forward1D(double[] s, int n) {
    if (n < 2)
      return;

    // Step 1: predict (alpha) - update odd samples
    for (var i = 0; i < n - 1; i += 2) {
      var left = s[i];
      var right = i + 2 < n ? s[i + 2] : s[i];
      s[i + 1] += _ALPHA * (left + right);
    }

    // Step 2: update (beta) - update even samples
    for (var i = 0; i < n; i += 2) {
      var left = i - 1 >= 0 ? s[i - 1] : s[1];
      var right = i + 1 < n ? s[i + 1] : s[n - 1];
      s[i] += _BETA * (left + right);
    }

    // Step 3: predict (gamma) - update odd samples
    for (var i = 0; i < n - 1; i += 2) {
      var left = s[i];
      var right = i + 2 < n ? s[i + 2] : s[i];
      s[i + 1] += _GAMMA * (left + right);
    }

    // Step 4: update (delta) - update even samples
    for (var i = 0; i < n; i += 2) {
      var left = i - 1 >= 0 ? s[i - 1] : s[1];
      var right = i + 1 < n ? s[i + 1] : s[n - 1];
      s[i] += _DELTA * (left + right);
    }

    // Scaling
    for (var i = 0; i < n; i += 2)
      s[i] *= _K;
    for (var i = 1; i < n; i += 2)
      s[i] *= _K_INV;

    // De-interleave: even to first half (low), odd to second half (high)
    var half = (n + 1) / 2;
    var temp = new double[n];
    for (var i = 0; i < half; ++i)
      temp[i] = s[2 * i];
    for (var i = 0; i < n / 2; ++i)
      temp[half + i] = s[2 * i + 1];
    Array.Copy(temp, s, n);
  }

  /// <summary>
  /// Performs the inverse 1D CDF 9/7 transform using the lifting scheme.
  /// Input has low-pass in the first half and high-pass in the second half;
  /// output is in interleaved order.
  /// </summary>
  public static void Inverse1D(double[] s, int n) {
    if (n < 2)
      return;

    var half = (n + 1) / 2;

    // Re-interleave: first half (low) to even, second half (high) to odd
    var temp = new double[n];
    for (var i = 0; i < half; ++i)
      temp[2 * i] = s[i];
    for (var i = 0; i < n / 2; ++i)
      temp[2 * i + 1] = s[half + i];
    Array.Copy(temp, s, n);

    // Undo scaling
    for (var i = 0; i < n; i += 2)
      s[i] *= _K_INV;
    for (var i = 1; i < n; i += 2)
      s[i] *= _K;

    // Undo step 4 (delta)
    for (var i = 0; i < n; i += 2) {
      var left = i - 1 >= 0 ? s[i - 1] : s[1];
      var right = i + 1 < n ? s[i + 1] : s[n - 1];
      s[i] -= _DELTA * (left + right);
    }

    // Undo step 3 (gamma)
    for (var i = 0; i < n - 1; i += 2) {
      var left = s[i];
      var right = i + 2 < n ? s[i + 2] : s[i];
      s[i + 1] -= _GAMMA * (left + right);
    }

    // Undo step 2 (beta)
    for (var i = 0; i < n; i += 2) {
      var left = i - 1 >= 0 ? s[i - 1] : s[1];
      var right = i + 1 < n ? s[i + 1] : s[n - 1];
      s[i] -= _BETA * (left + right);
    }

    // Undo step 1 (alpha)
    for (var i = 0; i < n - 1; i += 2) {
      var left = s[i];
      var right = i + 2 < n ? s[i + 2] : s[i];
      s[i + 1] -= _ALPHA * (left + right);
    }
  }

  /// <summary>Pads a dimension up to the next multiple of 2^levels.</summary>
  public static int PadToDecompositionBlock(int size, int levels) {
    var blockSize = 1 << levels;
    return (size + blockSize - 1) & ~(blockSize - 1);
  }

  private static void _ForwardRows(double[] data, int stride, int width, int height) {
    var row = new double[width];
    for (var y = 0; y < height; ++y) {
      var offset = y * stride;
      for (var i = 0; i < width; ++i)
        row[i] = data[offset + i];

      Forward1D(row, width);

      for (var i = 0; i < width; ++i)
        data[offset + i] = row[i];
    }
  }

  private static void _ForwardColumns(double[] data, int stride, int width, int height) {
    var col = new double[height];
    for (var x = 0; x < width; ++x) {
      for (var i = 0; i < height; ++i)
        col[i] = data[i * stride + x];

      Forward1D(col, height);

      for (var i = 0; i < height; ++i)
        data[i * stride + x] = col[i];
    }
  }

  private static void _InverseRows(double[] data, int stride, int width, int height) {
    var row = new double[width];
    for (var y = 0; y < height; ++y) {
      var offset = y * stride;
      for (var i = 0; i < width; ++i)
        row[i] = data[offset + i];

      Inverse1D(row, width);

      for (var i = 0; i < width; ++i)
        data[offset + i] = row[i];
    }
  }

  private static void _InverseColumns(double[] data, int stride, int width, int height) {
    var col = new double[height];
    for (var x = 0; x < width; ++x) {
      for (var i = 0; i < height; ++i)
        col[i] = data[i * stride + x];

      Inverse1D(col, height);

      for (var i = 0; i < height; ++i)
        data[i * stride + x] = col[i];
    }
  }
}
