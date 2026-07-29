using System;

namespace FileFormat.Jpeg2000;

/// <summary>LeGall 5/3 reversible integer wavelet transform for lossless JPEG 2000 coding.</summary>
internal static class Jp2Wavelet {

  /// <summary>Performs a 1D forward LeGall 5/3 wavelet transform in-place.</summary>
  /// <param name="data">Input signal, length must be >= 2.</param>
  /// <param name="length">Number of samples to transform.</param>
  /// <param name="low">Output low-frequency (scaling) coefficients.</param>
  /// <param name="high">Output high-frequency (detail) coefficients.</param>
  internal static void Forward1D(int[] data, int length, int[] low, int[] high) {
    var halfLen = (length + 1) / 2;
    var highLen = length / 2;

    // Lifting step 1: predict (high-pass)
    for (var n = 0; n < highLen; ++n) {
      var left = data[2 * n];
      var right = 2 * n + 2 < length ? data[2 * n + 2] : data[2 * n]; // mirror at boundary
      high[n] = data[2 * n + 1] - ((left + right) >> 1);
    }

    // Lifting step 2: update (low-pass)
    for (var n = 0; n < halfLen; ++n) {
      var dLeft = n > 0 ? high[n - 1] : high[0]; // mirror at boundary
      var dRight = n < highLen ? high[n] : high[highLen - 1]; // mirror at boundary
      low[n] = data[2 * n] + ((dLeft + dRight + 2) >> 2);
    }
  }

  /// <summary>Performs a 1D inverse LeGall 5/3 wavelet transform.</summary>
  /// <param name="low">Low-frequency (scaling) coefficients.</param>
  /// <param name="high">High-frequency (detail) coefficients.</param>
  /// <param name="length">Original signal length.</param>
  /// <param name="output">Reconstructed signal.</param>
  internal static void Inverse1D(int[] low, int[] high, int length, int[] output) {
    var halfLen = (length + 1) / 2;
    var highLen = length / 2;

    // Undo update: recover even samples
    var even = new int[halfLen];
    for (var n = 0; n < halfLen; ++n) {
      var dLeft = n > 0 ? high[n - 1] : high[0];
      var dRight = n < highLen ? high[n] : (highLen > 0 ? high[highLen - 1] : 0);
      even[n] = low[n] - ((dLeft + dRight + 2) >> 2);
    }

    // Undo predict: recover odd samples
    for (var n = 0; n < halfLen; ++n)
      output[2 * n] = even[n];

    for (var n = 0; n < highLen; ++n) {
      var left = even[n];
      var right = n + 1 < halfLen ? even[n + 1] : even[halfLen - 1];
      output[2 * n + 1] = high[n] + ((left + right) >> 1);
    }
  }

  /// <summary>Performs a 2D forward wavelet transform on the given component plane.</summary>
  /// <param name="plane">2D array of pixel/coefficient values [height, width].</param>
  /// <param name="width">Active width.</param>
  /// <param name="height">Active height.</param>
  internal static void Forward2D(int[,] plane, int width, int height) {
    // Rows
    var rowBuf = new int[width];
    var rowLow = new int[(width + 1) / 2];
    var rowHigh = new int[width / 2];
    for (var y = 0; y < height; ++y) {
      for (var x = 0; x < width; ++x)
        rowBuf[x] = plane[y, x];

      if (width >= 2) {
        Forward1D(rowBuf, width, rowLow, rowHigh);
        var lowLen = (width + 1) / 2;
        var highLen = width / 2;
        for (var x = 0; x < lowLen; ++x)
          plane[y, x] = rowLow[x];
        for (var x = 0; x < highLen; ++x)
          plane[y, lowLen + x] = rowHigh[x];
      }
    }

    // Columns
    var colBuf = new int[height];
    var colLow = new int[(height + 1) / 2];
    var colHigh = new int[height / 2];
    for (var x = 0; x < width; ++x) {
      for (var y = 0; y < height; ++y)
        colBuf[y] = plane[y, x];

      if (height >= 2) {
        Forward1D(colBuf, height, colLow, colHigh);
        var lowLen = (height + 1) / 2;
        var highLen = height / 2;
        for (var y = 0; y < lowLen; ++y)
          plane[y, x] = colLow[y];
        for (var y = 0; y < highLen; ++y)
          plane[lowLen + y, x] = colHigh[y];
      }
    }
  }

  /// <summary>Performs a 2D inverse wavelet transform on the given component plane.</summary>
  /// <param name="plane">2D array of coefficient values [height, width].</param>
  /// <param name="width">Active width.</param>
  /// <param name="height">Active height.</param>
  internal static void Inverse2D(int[,] plane, int width, int height) {
    // Columns (inverse of what was done last in forward)
    var colLow = new int[(height + 1) / 2];
    var colHigh = new int[height / 2];
    var colOut = new int[height];
    for (var x = 0; x < width; ++x) {
      if (height >= 2) {
        var lowLen = (height + 1) / 2;
        var highLen = height / 2;
        for (var y = 0; y < lowLen; ++y)
          colLow[y] = plane[y, x];
        for (var y = 0; y < highLen; ++y)
          colHigh[y] = plane[lowLen + y, x];

        Inverse1D(colLow, colHigh, height, colOut);
        for (var y = 0; y < height; ++y)
          plane[y, x] = colOut[y];
      }
    }

    // Rows
    var rowLow = new int[(width + 1) / 2];
    var rowHigh = new int[width / 2];
    var rowOut = new int[width];
    for (var y = 0; y < height; ++y) {
      if (width >= 2) {
        var lowLen = (width + 1) / 2;
        var highLen = width / 2;
        for (var x = 0; x < lowLen; ++x)
          rowLow[x] = plane[y, x];
        for (var x = 0; x < highLen; ++x)
          rowHigh[x] = plane[y, lowLen + x];

        Inverse1D(rowLow, rowHigh, width, rowOut);
        for (var x = 0; x < width; ++x)
          plane[y, x] = rowOut[x];
      }
    }
  }

  /// <summary>Performs multi-level 2D forward wavelet decomposition.</summary>
  /// <param name="plane">2D array of pixel values.</param>
  /// <param name="width">Image width.</param>
  /// <param name="height">Image height.</param>
  /// <param name="levels">Number of decomposition levels.</param>
  internal static void ForwardMultiLevel(int[,] plane, int width, int height, int levels) {
    var w = width;
    var h = height;
    for (var level = 0; level < levels; ++level) {
      if (w < 2 || h < 2)
        break;
      Forward2D(plane, w, h);
      w = (w + 1) / 2;
      h = (h + 1) / 2;
    }
  }

  /// <summary>Performs multi-level 2D inverse wavelet reconstruction.</summary>
  /// <param name="plane">2D array of coefficient values.</param>
  /// <param name="width">Image width.</param>
  /// <param name="height">Image height.</param>
  /// <param name="levels">Number of decomposition levels.</param>
  internal static void InverseMultiLevel(int[,] plane, int width, int height, int levels) {
    // Compute the dimensions at each level
    var widths = new int[levels + 1];
    var heights = new int[levels + 1];
    widths[0] = width;
    heights[0] = height;
    for (var i = 1; i <= levels; ++i) {
      widths[i] = (widths[i - 1] + 1) / 2;
      heights[i] = (heights[i - 1] + 1) / 2;
    }

    // Reconstruct from the deepest level back
    for (var level = levels - 1; level >= 0; --level)
      Inverse2D(plane, widths[level], heights[level]);
  }
}
