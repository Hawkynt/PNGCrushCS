using System;
using System.IO;

namespace FileFormat.Codecs.H264;

/// <summary>High-profile 8x8 inverse quantisation and integer inverse transform (H.264 clause 8.5.13).</summary>
internal static class H264Transform8x8 {

  private static readonly int[,] _NORM_ADJUST = {
    { 20, 18, 32, 19, 25, 24 },
    { 22, 19, 35, 21, 28, 26 },
    { 26, 23, 42, 24, 33, 31 },
    { 28, 25, 45, 26, 35, 33 },
    { 32, 28, 51, 30, 40, 38 },
    { 36, 32, 58, 34, 46, 43 },
  };

  // The six norm-adjustment classes of equation 8-317, laid out over one 4x4 period.
  private static readonly byte[] _NORM_CLASS = [
    0, 3, 4, 3,
    3, 1, 5, 1,
    4, 5, 2, 5,
    3, 1, 5, 1,
  ];

  internal static int LevelScale(int m, int row, int column, int weightScale) {
    if ((uint)m >= 6)
      throw new ArgumentOutOfRangeException(nameof(m));
    if ((uint)row >= 8)
      throw new ArgumentOutOfRangeException(nameof(row));
    if ((uint)column >= 8)
      throw new ArgumentOutOfRangeException(nameof(column));

    var kind = _NORM_CLASS[((row & 3) << 2) | (column & 3)];
    return weightScale * _NORM_ADJUST[m, kind];
  }

  /// <summary>Inverse-scans, scales, and inverse-transforms one 8x8 luma residual block.</summary>
  internal static void DecodeBlock(
    ReadOnlySpan<int> levels,
    int qp,
    ReadOnlySpan<byte> scalingList,
    Span<int> residual) {
    if (levels.Length < 64)
      throw new ArgumentException("An H.264 8x8 residual block needs 64 coefficient levels.", nameof(levels));
    if (scalingList.Length < 64)
      throw new ArgumentException("An H.264 8x8 scaling list needs 64 entries.", nameof(scalingList));
    if (residual.Length < 64)
      throw new ArgumentException("An H.264 8x8 residual output needs 64 entries.", nameof(residual));
    if (qp is < 0 or > 51)
      throw new ArgumentOutOfRangeException(nameof(qp));

    Span<int> scaled = stackalloc int[64];
    _Scale(levels, qp, scalingList, scaled);
    InverseTransform(scaled, residual);
  }

  private static void _Scale(
    ReadOnlySpan<int> levels,
    int qp,
    ReadOnlySpan<byte> scalingList,
    Span<int> scaled) {
    var quotient = qp / 6;
    var remainder = qp % 6;

    for (var scan = 0; scan < 64; ++scan) {
      var position = H264ScalingLists.ZigZagScan8x8[scan];
      var level = levels[scan];
      if (level == 0) {
        scaled[position] = 0;
        continue;
      }

      var row = position >> 3;
      var column = position & 7;
      var product = level * LevelScale(remainder, row, column, scalingList[position]);
      scaled[position] = qp >= 36
        ? product << (quotient - 6)
        : (product + (1 << (5 - quotient))) >> (6 - quotient);
    }
  }

  /// <summary>The exact separable 8x8 integer inverse transform, including the final +32 / 64 rounding.</summary>
  internal static void InverseTransform(ReadOnlySpan<int> input, Span<int> residual) {
    if (input.Length < 64)
      throw new ArgumentException("An H.264 8x8 transform needs 64 coefficients.", nameof(input));
    if (residual.Length < 64)
      throw new ArgumentException("An H.264 8x8 transform needs 64 output samples.", nameof(residual));

    Span<int> tmp = stackalloc int[64];

    // H.264 8.5.13.2 first transforms each horizontal row. The one-dimensional butterfly contains
    // arithmetic right shifts, so exchanging the row and column passes is not bit-exact for negative
    // odd coefficients even though the corresponding real-valued separable transform would commute.
    for (var row = 0; row < 8; ++row)
      _Transform1D(input.Slice(row << 3, 8), tmp.Slice(row << 3, 8), round: false);

    // The second pass transforms each vertical column and performs the normative +32 / 64 rounding.
    Span<int> column = stackalloc int[8];
    Span<int> transformed = stackalloc int[8];
    for (var x = 0; x < 8; ++x) {
      for (var y = 0; y < 8; ++y)
        column[y] = tmp[(y << 3) + x];

      _Transform1D(column, transformed, round: true);
      for (var y = 0; y < 8; ++y)
        residual[(y << 3) + x] = transformed[y];
    }
  }

  private static void _Transform1D(ReadOnlySpan<int> source, Span<int> target, bool round) {
    var a0 = source[0] + source[4];
    var a2 = source[0] - source[4];
    var a4 = (source[2] >> 1) - source[6];
    var a6 = source[2] + (source[6] >> 1);

    var b0 = a0 + a6;
    var b2 = a2 + a4;
    var b4 = a2 - a4;
    var b6 = a0 - a6;

    var a1 = -source[3] + source[5] - source[7] - (source[7] >> 1);
    var a3 = source[1] + source[7] - source[3] - (source[3] >> 1);
    var a5 = -source[1] + source[7] + source[5] + (source[5] >> 1);
    var a7 = source[3] + source[5] + source[1] + (source[1] >> 1);

    var b1 = (a7 >> 2) + a1;
    var b3 = a3 + (a5 >> 2);
    var b5 = (a3 >> 2) - a5;
    var b7 = a7 - (a1 >> 2);

    Span<int> values = stackalloc int[8] {
      b0 + b7,
      b2 + b5,
      b4 + b3,
      b6 + b1,
      b6 - b1,
      b4 - b3,
      b2 - b5,
      b0 - b7,
    };

    if (!round) {
      values.CopyTo(target);
      return;
    }

    for (var i = 0; i < 8; ++i)
      target[i] = (values[i] + 32) >> 6;
  }
}
