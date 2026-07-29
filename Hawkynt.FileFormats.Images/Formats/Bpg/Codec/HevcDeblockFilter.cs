using System;

namespace FileFormat.Bpg.Codec;

/// <summary>HEVC deblocking filter for block boundaries: strong and weak filtering decisions based on QP.</summary>
internal static class HevcDeblockFilter {

  /// <summary>Applies deblocking filter to an entire frame plane.</summary>
  /// <param name="samples">Sample buffer (modified in place).</param>
  /// <param name="stride">Row stride.</param>
  /// <param name="width">Frame width in samples.</param>
  /// <param name="height">Frame height in samples.</param>
  /// <param name="qp">Quantization parameter.</param>
  /// <param name="bitDepth">Bit depth (8 or 10).</param>
  /// <param name="minCuSize">Minimum coding unit size (determines grid spacing for boundary detection).</param>
  public static void Apply(int[] samples, int stride, int width, int height, int qp, int bitDepth, int minCuSize) {
    var blockSize = Math.Max(minCuSize, 8);

    // Vertical edges (filter horizontally)
    for (var y = 0; y < height; y += 4)
      for (var x = blockSize; x < width; x += blockSize)
        _FilterEdge(samples, stride, x, y, width, height, qp, bitDepth, isVertical: true);

    // Horizontal edges (filter vertically)
    for (var x = 0; x < width; x += 4)
      for (var y = blockSize; y < height; y += blockSize)
        _FilterEdge(samples, stride, x, y, width, height, qp, bitDepth, isVertical: false);
  }

  private static void _FilterEdge(int[] samples, int stride, int x, int y, int width, int height, int qp, int bitDepth, bool isVertical) {
    // Boundary strength estimation (simplified for I-slices: always strong at CU boundaries)
    var bs = 2; // I-slice: all intra-coded, boundary strength is typically 2

    if (bs == 0)
      return;

    var beta = _GetBeta(qp, bitDepth);
    var tc = _GetTc(qp, bitDepth, bs);

    if (tc == 0)
      return;

    // Filter 4 sample rows/columns at the boundary
    for (var i = 0; i < 4; ++i) {
      int px, py;
      if (isVertical) {
        px = x;
        py = y + i;
      } else {
        px = x + i;
        py = y;
      }

      if (px >= width || py >= height || px < 0 || py < 0)
        continue;

      // Get samples p0..p3 (on left/above side) and q0..q3 (on right/below side)
      var p = new int[4];
      var q = new int[4];

      var valid = true;
      for (var j = 0; j < 4; ++j) {
        int pIdx, qIdx;
        if (isVertical) {
          var pxP = px - 1 - j;
          var pxQ = px + j;
          if (pxP < 0 || pxQ >= width) { valid = false; break; }
          pIdx = py * stride + pxP;
          qIdx = py * stride + pxQ;
        } else {
          var pyP = py - 1 - j;
          var pyQ = py + j;
          if (pyP < 0 || pyQ >= height) { valid = false; break; }
          pIdx = pyP * stride + px;
          qIdx = pyQ * stride + px;
        }

        if (pIdx < 0 || pIdx >= samples.Length || qIdx < 0 || qIdx >= samples.Length) { valid = false; break; }
        p[j] = samples[pIdx];
        q[j] = samples[qIdx];
      }

      if (!valid)
        continue;

      // Check deblocking condition
      var dp0 = Math.Abs(p[2] - 2 * p[1] + p[0]);
      var dq0 = Math.Abs(q[2] - 2 * q[1] + q[0]);
      var d = dp0 + dq0;

      if (d >= beta)
        continue;

      // Decide strong vs weak filtering
      var useStrong = _IsStrongFilter(p, q, beta, tc);

      var maxVal = (1 << bitDepth) - 1;

      if (useStrong) {
        // Strong filter: modifies p0, p1, p2, q0, q1, q2
        var p0New = Math.Clamp((p[2] + 2 * p[1] + 2 * p[0] + 2 * q[0] + q[1] + 4) >> 3, 0, maxVal);
        var p1New = Math.Clamp((p[2] + p[1] + p[0] + q[0] + 2) >> 2, 0, maxVal);
        var p2New = Math.Clamp((2 * p[3] + 3 * p[2] + p[1] + p[0] + q[0] + 4) >> 3, 0, maxVal);
        var q0New = Math.Clamp((p[1] + 2 * p[0] + 2 * q[0] + 2 * q[1] + q[2] + 4) >> 3, 0, maxVal);
        var q1New = Math.Clamp((p[0] + q[0] + q[1] + q[2] + 2) >> 2, 0, maxVal);
        var q2New = Math.Clamp((p[0] + q[0] + q[1] + 3 * q[2] + 2 * q[3] + 4) >> 3, 0, maxVal);

        _WriteSample(samples, stride, px, py, -1, isVertical, p0New, width, height);
        _WriteSample(samples, stride, px, py, -2, isVertical, p1New, width, height);
        _WriteSample(samples, stride, px, py, -3, isVertical, p2New, width, height);
        _WriteSample(samples, stride, px, py, 0, isVertical, q0New, width, height);
        _WriteSample(samples, stride, px, py, 1, isVertical, q1New, width, height);
        _WriteSample(samples, stride, px, py, 2, isVertical, q2New, width, height);
      } else {
        // Weak filter: modifies p0 and q0 (and optionally p1, q1)
        var delta = Math.Clamp((9 * (q[0] - p[0]) - 3 * (q[1] - p[1]) + 8) >> 4, -tc, tc);
        var p0New = Math.Clamp(p[0] + delta, 0, maxVal);
        var q0New = Math.Clamp(q[0] - delta, 0, maxVal);

        _WriteSample(samples, stride, px, py, -1, isVertical, p0New, width, height);
        _WriteSample(samples, stride, px, py, 0, isVertical, q0New, width, height);

        // Conditional modification of p1 and q1
        if (dp0 < (beta + (beta >> 1)) >> 3) {
          var deltaP = Math.Clamp((((p[2] + p[0] + 1) >> 1) - p[1] + delta) >> 1, -(tc >> 1), tc >> 1);
          var p1New = Math.Clamp(p[1] + deltaP, 0, maxVal);
          _WriteSample(samples, stride, px, py, -2, isVertical, p1New, width, height);
        }

        if (dq0 < (beta + (beta >> 1)) >> 3) {
          var deltaQ = Math.Clamp((((q[2] + q[0] + 1) >> 1) - q[1] - delta) >> 1, -(tc >> 1), tc >> 1);
          var q1New = Math.Clamp(q[1] + deltaQ, 0, maxVal);
          _WriteSample(samples, stride, px, py, 1, isVertical, q1New, width, height);
        }
      }
    }
  }

  private static void _WriteSample(int[] samples, int stride, int px, int py, int offset, bool isVertical, int value, int width, int height) {
    int sx, sy;
    if (isVertical) {
      sx = px + offset;
      sy = py;
    } else {
      sx = px;
      sy = py + offset;
    }

    if (sx < 0 || sx >= width || sy < 0 || sy >= height)
      return;

    samples[sy * stride + sx] = value;
  }

  private static bool _IsStrongFilter(int[] p, int[] q, int beta, int tc) {
    // Strong filter condition: samples are smooth enough
    var dp = Math.Abs(p[3] - p[0]);
    var dq = Math.Abs(q[3] - q[0]);
    var pqDiff = Math.Abs(p[0] - q[0]);

    return dp < (beta >> 3) && dq < (beta >> 3) && pqDiff < ((5 * tc + 1) >> 1);
  }

  // Beta table (Table 8-16 in HEVC spec, indexed by QP clamped to 0..51)
  private static readonly int[] _BetaTable = [
     0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,
     6,  7,  8,  9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 20, 22, 24,
    26, 28, 30, 32, 34, 36, 38, 40, 42, 44, 46, 48, 50, 52, 54, 56,
    58, 60, 62, 64,
  ];

  // Tc table (Table 8-17 in HEVC spec)
  private static readonly int[] _TcTable = [
     0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,  0,
     0,  0,  1,  1,  1,  1,  1,  1,  1,  1,  1,  2,  2,  2,  2,  3,
     3,  3,  3,  4,  4,  4,  5,  5,  6,  6,  7,  8,  9, 10, 11, 13,
    14, 16, 18, 20,
  ];

  private static int _GetBeta(int qp, int bitDepth) {
    var idx = Math.Clamp(qp + (bitDepth - 8) * 6, 0, _BetaTable.Length - 1);
    return _BetaTable[idx] << (bitDepth - 8);
  }

  private static int _GetTc(int qp, int bitDepth, int bs) {
    var tcOffset = bs > 1 ? 0 : 0;
    var idx = Math.Clamp(qp + tcOffset + (bitDepth - 8) * 6, 0, _TcTable.Length - 1);
    return _TcTable[idx] << (bitDepth - 8);
  }
}
