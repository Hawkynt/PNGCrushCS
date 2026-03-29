using System;

namespace FileFormat.DjVu.Codec;

/// <summary>
/// Cohen-Daubechies-Feauveau 5/3 wavelet transform using the lifting scheme.
/// The CDF 5/3 is a biorthogonal wavelet used in JPEG 2000 (lossless mode) and DjVu IW44.
///
/// Forward transform (standard CDF 5/3 lifting order):
///   1. Predict: d[n]  = odd[n] - (even[n] + even[n+1] + 1) >> 1
///   2. Update:  s[n]  = even[n] + (d[n-1] + d[n] + 2) >> 2
///
/// Inverse transform (undo in reverse order):
///   1. Undo update:  even[n] -= (odd[n-1] + odd[n] + 2) >> 2
///   2. Undo predict: odd[n]  += (even[n] + even[n+1] + 1) >> 1
///
/// The transform is applied in a dyadic pyramid: each level halves the resolution.
/// Low-pass coefficients sit at even positions, high-pass (detail) at odd positions.
/// </summary>
internal static class Iw44Wavelet {

  /// <summary>
  /// Performs the forward CDF 5/3 wavelet transform on a 2D array of coefficients.
  /// After transformation, the array is organized into wavelet subbands.
  /// </summary>
  /// <param name="data">Coefficient array (modified in place).</param>
  /// <param name="stride">Row stride of the array.</param>
  /// <param name="width">Active width at the current level.</param>
  /// <param name="height">Active height at the current level.</param>
  /// <param name="levels">Number of decomposition levels.</param>
  public static void Forward(int[] data, int stride, int width, int height, int levels) {
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
  /// Performs the inverse CDF 5/3 wavelet transform on a 2D array of coefficients.
  /// Iterates from the coarsest level to the finest, undoing the forward transform.
  /// </summary>
  /// <param name="data">Coefficient array (modified in place).</param>
  /// <param name="stride">Row stride of the array.</param>
  /// <param name="width">Full width of the coefficient array.</param>
  /// <param name="height">Full height of the coefficient array.</param>
  /// <param name="levels">Number of decomposition levels to undo.</param>
  public static void Inverse(int[] data, int stride, int width, int height, int levels) {
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

  /// <summary>Forward 1D CDF 5/3 wavelet transform on each row using lifting.</summary>
  private static void _ForwardRows(int[] data, int stride, int width, int height) {
    var halfW = (width + 1) / 2;
    var temp = new int[width];

    for (var y = 0; y < height; ++y) {
      var rowBase = y * stride;

      for (var i = 0; i < width; ++i)
        temp[i] = data[rowBase + i];

      // Step 1 - Predict (high-pass): d[n] = odd[n] - (even[n] + even[n+1] + 1) >> 1
      for (var i = 0; i < halfW - 1 && 2 * i + 1 < width; ++i) {
        var even0 = temp[2 * i];
        var even1 = temp[2 * (i + 1)];
        temp[2 * i + 1] -= (even0 + even1 + 1) >> 1;
      }
      // Last odd sample boundary: mirror the last even
      if (width > 1 && (width & 1) == 0)
        temp[width - 1] -= temp[width - 2];

      // Step 2 - Update (low-pass): s[n] = even[n] + (d[n-1] + d[n] + 2) >> 2
      for (var i = 0; i < halfW; ++i) {
        var dLeft = (2 * i - 1 >= 0) ? temp[2 * i - 1] : temp[1 < width ? 1 : 0];
        var dRight = (2 * i + 1 < width) ? temp[2 * i + 1] : temp[width - 1];
        temp[2 * i] += (dLeft + dRight + 2) >> 2;
      }

      // De-interleave: low-pass to left half, high-pass to right half
      var deinterleaved = new int[width];
      for (var i = 0; i < halfW; ++i)
        deinterleaved[i] = temp[2 * i];
      for (var i = 0; i < width - halfW; ++i)
        deinterleaved[halfW + i] = temp[2 * i + 1];

      for (var i = 0; i < width; ++i)
        data[rowBase + i] = deinterleaved[i];
    }
  }

  /// <summary>Forward 1D CDF 5/3 wavelet transform on each column.</summary>
  private static void _ForwardColumns(int[] data, int stride, int width, int height) {
    var halfH = (height + 1) / 2;
    var temp = new int[height];

    for (var x = 0; x < width; ++x) {
      for (var i = 0; i < height; ++i)
        temp[i] = data[i * stride + x];

      // Step 1 - Predict (high-pass)
      for (var i = 0; i < halfH - 1 && 2 * i + 1 < height; ++i) {
        var even0 = temp[2 * i];
        var even1 = temp[2 * (i + 1)];
        temp[2 * i + 1] -= (even0 + even1 + 1) >> 1;
      }
      if (height > 1 && (height & 1) == 0)
        temp[height - 1] -= temp[height - 2];

      // Step 2 - Update (low-pass)
      for (var i = 0; i < halfH; ++i) {
        var dLeft = (2 * i - 1 >= 0) ? temp[2 * i - 1] : temp[1 < height ? 1 : 0];
        var dRight = (2 * i + 1 < height) ? temp[2 * i + 1] : temp[height - 1];
        temp[2 * i] += (dLeft + dRight + 2) >> 2;
      }

      // De-interleave: low-pass to top half, high-pass to bottom half
      var deinterleaved = new int[height];
      for (var i = 0; i < halfH; ++i)
        deinterleaved[i] = temp[2 * i];
      for (var i = 0; i < height - halfH; ++i)
        deinterleaved[halfH + i] = temp[2 * i + 1];

      for (var i = 0; i < height; ++i)
        data[i * stride + x] = deinterleaved[i];
    }
  }

  /// <summary>Inverse 1D CDF 5/3 wavelet transform on each row.</summary>
  private static void _InverseRows(int[] data, int stride, int width, int height) {
    var halfW = (width + 1) / 2;

    for (var y = 0; y < height; ++y) {
      var rowBase = y * stride;

      // Re-interleave: even positions from left half, odd from right half
      var temp = new int[width];
      for (var i = 0; i < halfW; ++i)
        temp[2 * i] = data[rowBase + i];
      for (var i = 0; i < width - halfW; ++i)
        temp[2 * i + 1] = data[rowBase + halfW + i];

      // Step 1 - Undo update: even[n] -= (odd[n-1] + odd[n] + 2) >> 2
      for (var i = halfW - 1; i >= 0; --i) {
        var dLeft = (2 * i - 1 >= 0) ? temp[2 * i - 1] : temp[1 < width ? 1 : 0];
        var dRight = (2 * i + 1 < width) ? temp[2 * i + 1] : temp[width - 1];
        temp[2 * i] -= (dLeft + dRight + 2) >> 2;
      }

      // Step 2 - Undo predict: odd[n] += (even[n] + even[n+1] + 1) >> 1
      for (var i = halfW - 2; i >= 0 && 2 * i + 1 < width; --i) {
        var even0 = temp[2 * i];
        var even1 = temp[2 * (i + 1)];
        temp[2 * i + 1] += (even0 + even1 + 1) >> 1;
      }
      if (width > 1 && (width & 1) == 0)
        temp[width - 1] += temp[width - 2];

      for (var i = 0; i < width; ++i)
        data[rowBase + i] = temp[i];
    }
  }

  /// <summary>Inverse 1D CDF 5/3 wavelet transform on each column.</summary>
  private static void _InverseColumns(int[] data, int stride, int width, int height) {
    var halfH = (height + 1) / 2;

    for (var x = 0; x < width; ++x) {
      // Re-interleave
      var temp = new int[height];
      for (var i = 0; i < halfH; ++i)
        temp[2 * i] = data[i * stride + x];
      for (var i = 0; i < height - halfH; ++i)
        temp[2 * i + 1] = data[(halfH + i) * stride + x];

      // Step 1 - Undo update
      for (var i = halfH - 1; i >= 0; --i) {
        var dLeft = (2 * i - 1 >= 0) ? temp[2 * i - 1] : temp[1 < height ? 1 : 0];
        var dRight = (2 * i + 1 < height) ? temp[2 * i + 1] : temp[height - 1];
        temp[2 * i] -= (dLeft + dRight + 2) >> 2;
      }

      // Step 2 - Undo predict
      for (var i = halfH - 2; i >= 0 && 2 * i + 1 < height; --i) {
        var even0 = temp[2 * i];
        var even1 = temp[2 * (i + 1)];
        temp[2 * i + 1] += (even0 + even1 + 1) >> 1;
      }
      if (height > 1 && (height & 1) == 0)
        temp[height - 1] += temp[height - 2];

      for (var i = 0; i < height; ++i)
        data[i * stride + x] = temp[i];
    }
  }
}
