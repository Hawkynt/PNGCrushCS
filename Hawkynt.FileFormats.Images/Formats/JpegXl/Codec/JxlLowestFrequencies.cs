using System;
using System.Collections.Generic;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// The lowest coefficients of a transform, from the DC values of the blocks it covers.
/// </summary>
/// <remarks>
/// A transform covering more than one block does not carry those blocks' DC
/// values directly. It carries a small transform of them — one coefficient per
/// covered block, sitting at the front of the block in scan order — and the
/// picture only comes back if they are put there. Setting the single lowest
/// coefficient and leaving the rest is what makes a large transform reconstruct
/// from one DC instead of all of them.
///
/// <para>Rather than restate the specification's forward transform and its
/// scaling tables, the map is measured from the inverse transform this decoder
/// already has and already agrees with libjxl on: feed it each lowest
/// coefficient on its own, see what average each covered block ends up with,
/// and that is the matrix taking coefficients to block averages. Inverting it
/// once per shape gives the matrix taking the DC values back to coefficients.
/// The two cannot disagree, because one is derived from the other.</para>
/// </remarks>
internal static class JxlLowestFrequencies {

  private const int _BlockDim = 8;

  /// <summary>
  /// The largest transform this is built for. Beyond it the matrix is bigger
  /// than the saving, and no encoder met here reaches that far.
  /// </summary>
  private const int _MaxCoveredBlocks = 64;

  private static readonly Dictionary<JxlAcStrategyType, float[][]?> _Cache = new();

  /// <summary>
  /// The matrix taking a transform's covered-block DC values to its lowest
  /// coefficients, or null where the shape is beyond what is built.
  /// </summary>
  public static float[][]? DcToCoefficients(JxlAcStrategyType strategy) {
    lock (_Cache) {
      if (_Cache.TryGetValue(strategy, out var cached))
        return cached;

      var matrix = _Build(strategy);
      _Cache[strategy] = matrix;
      return matrix;
    }
  }

  private static float[][]? _Build(JxlAcStrategyType strategy) {
    var covered = JxlAcStrategyGeometry.CoveredBlocks(strategy);
    if (covered <= 1 || covered > _MaxCoveredBlocks)
      return null;

    var (blockW, blockH) = JxlVarDctIdct.BlockSize(strategy);
    var area = blockW * blockH;
    var blocksX = blockW / _BlockDim;
    var blocksY = blockH / _BlockDim;
    if (blocksX * blocksY != covered)
      return null;

    // Square shapes only. Measured against libjxl, drawing a rectangular
    // transform once from its origin is far worse than drawing it at each block
    // it covers — the two disagree about which way round the transform's rows
    // and columns go, and drawing it once spreads that over the whole rectangle
    // instead of leaving it in one block.
    if (blockW != blockH)
      return null;

    var order = JxlNaturalCoeffOrder.For(strategy);
    var forward = new float[covered][];
    var coefficients = new float[area];
    var spatial = new float[area];

    for (var i = 0; i < covered; ++i) {
      Array.Clear(coefficients);
      coefficients[order[i]] = 1f;
      JxlVarDctIdct.InverseAcStrategy(strategy, coefficients, spatial);

      var column = new float[covered];
      for (var byIndex = 0; byIndex < blocksY; ++byIndex)
      for (var bxIndex = 0; bxIndex < blocksX; ++bxIndex) {
        var sum = 0f;
        for (var y = 0; y < _BlockDim; ++y)
        for (var x = 0; x < _BlockDim; ++x)
          sum += spatial[(byIndex * _BlockDim + y) * blockW + bxIndex * _BlockDim + x];

        column[byIndex * blocksX + bxIndex] = sum / (_BlockDim * _BlockDim);
      }

      forward[i] = column;
    }

    // forward[i][j] is what coefficient i does to block j; the matrix wanted is
    // the other way round and inverted.
    var matrix = new float[covered][];
    for (var j = 0; j < covered; ++j) {
      matrix[j] = new float[covered];
      for (var i = 0; i < covered; ++i)
        matrix[j][i] = forward[i][j];
    }

    return _Invert(matrix);
  }

  /// <summary>Gauss-Jordan with partial pivoting; null when the matrix is singular.</summary>
  private static float[][]? _Invert(float[][] matrix) {
    var n = matrix.Length;
    var work = new double[n][];
    var inverse = new double[n][];
    for (var i = 0; i < n; ++i) {
      work[i] = new double[n];
      inverse[i] = new double[n];
      for (var j = 0; j < n; ++j)
        work[i][j] = matrix[i][j];
      inverse[i][i] = 1.0;
    }

    for (var column = 0; column < n; ++column) {
      var pivot = column;
      for (var row = column + 1; row < n; ++row)
        if (Math.Abs(work[row][column]) > Math.Abs(work[pivot][column]))
          pivot = row;

      if (Math.Abs(work[pivot][column]) < 1e-9)
        return null;

      (work[column], work[pivot]) = (work[pivot], work[column]);
      (inverse[column], inverse[pivot]) = (inverse[pivot], inverse[column]);

      var scale = 1.0 / work[column][column];
      for (var j = 0; j < n; ++j) {
        work[column][j] *= scale;
        inverse[column][j] *= scale;
      }

      for (var row = 0; row < n; ++row) {
        if (row == column)
          continue;

        var factor = work[row][column];
        if (factor == 0.0)
          continue;

        for (var j = 0; j < n; ++j) {
          work[row][j] -= factor * work[column][j];
          inverse[row][j] -= factor * inverse[column][j];
        }
      }
    }

    var result = new float[n][];
    for (var i = 0; i < n; ++i) {
      result[i] = new float[n];
      for (var j = 0; j < n; ++j)
        result[i][j] = (float)inverse[i][j];
    }

    return result;
  }
}
