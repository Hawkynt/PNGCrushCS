using System;

namespace FileFormat.Codecs.H264;

/// <summary>
/// The inverse scan, the dequantisation and the inverse transforms of ITU-T H.264, clause 8.5.
/// </summary>
/// <remarks>
/// H.264's transform is not a discrete cosine transform but an integer approximation of one, and the
/// difference matters here in a way it does not for MPEG-1 or JPEG. Those standards specify their
/// inverse transform as a formula with an accuracy bound, so two conforming decoders may differ by a
/// level and both be right. H.264 specifies its inverse transform as exact integer arithmetic
/// (clause 8.5.12.2), so two conforming decoders agree on every sample of every block — which is why
/// the comparison against a reference decoder here is for exact equality rather than for a bound.
/// <para/>
/// The scaling that would ordinarily be part of the transform has been moved into the quantiser: the
/// forward transform's basis vectors have unequal norms, and rather than correct for that in the
/// transform the standard folds the correction into the dequantisation factors
/// (<see cref="_NORM_ADJUST"/>, the matrix of equation 8-315). So dequantisation here is not one
/// number per block but one per coefficient position.
/// </remarks>
internal static class H264Transform {

  /// <summary>
  /// The zig-zag scan of clause 8.5.6, Table 8-13: which of a 4x4 block's sixteen positions each
  /// scan index stands for, in raster order within the block.
  /// </summary>
  internal static readonly int[] ZigZagScan4x4 = [
    0, 1, 4, 8,
    5, 2, 3, 6,
    9, 12, 13, 10,
    7, 11, 14, 15,
  ];

  /// <summary>
  /// The quantiser's per-position correction, equation 8-315: the matrix <c>v</c> whose rows are the
  /// six values <c>QP % 6</c> takes and whose three columns are the three kinds of position.
  /// </summary>
  private static readonly int[,] _NORM_ADJUST = {
    { 10, 16, 13 },
    { 11, 18, 14 },
    { 13, 20, 16 },
    { 14, 23, 18 },
    { 16, 25, 20 },
    { 18, 29, 23 },
  };

  /// <summary>
  /// Table 8-15: the chroma quantisation parameter each luma one maps to, for indices 30 and above.
  /// </summary>
  /// <remarks>
  /// Below 30 chroma is quantised exactly as luma is; above it chroma is quantised progressively more
  /// finely than luma, which is the standard buying back some of the colour a coarse quantiser would
  /// otherwise throw away. The table stops at 51 because that is the coarsest quantiser 8-bit samples
  /// have.
  /// </remarks>
  private static readonly int[] _CHROMA_QP = [
    29, 30, 31, 32, 32, 33, 34, 34, 35, 35, 36, 36, 37, 37, 37, 38, 38, 38, 39, 39, 39, 39,
  ];

  /// <summary>
  /// <c>LevelScale4x4( m, i, j )</c> with the flat weighting matrix — equation 8-313.
  /// </summary>
  /// <remarks>
  /// Flat because scaling lists are refused: without them <c>weightScale4x4</c> is 16 everywhere
  /// (clause 8.5.9), so this is sixteen times the norm adjustment and nothing else.
  /// </remarks>
  internal static int LevelScale(int m, int row, int column) {
    var kind = (row & 1) == 0
      ? (column & 1) == 0 ? 0 : 2
      : (column & 1) == 0 ? 2 : 1;

    return 16 * _NORM_ADJUST[m, kind];
  }

  /// <summary>The chroma quantisation parameter for a luma one — clause 8.5.8, Table 8-15.</summary>
  internal static int ChromaQp(int qpi) {
    if (qpi < 0)
      return 0;

    return qpi < 30 ? qpi : qpi > 51 ? _CHROMA_QP[^1] : _CHROMA_QP[qpi - 30];
  }

  /// <summary>
  /// Dequantises and inverse-transforms one 4x4 residual block, adding the result to a prediction.
  /// </summary>
  /// <param name="levels">The sixteen coefficient levels in scan order.</param>
  /// <param name="qp">The quantisation parameter for this block's component.</param>
  /// <param name="hasSeparateDc">
  /// Whether position zero has already been dequantised elsewhere — true for an Intra_16x16 luma
  /// block and for every chroma block, whose DC coefficients go through their own transform first
  /// (clause 8.5.12.1, equation 8-335).
  /// </param>
  /// <param name="dc">That already-dequantised DC value, used when <paramref name="hasSeparateDc"/>.</param>
  /// <param name="residual">Receives the sixteen residual samples in raster order.</param>
  internal static void DecodeBlock(ReadOnlySpan<int> levels, int qp, bool hasSeparateDc, int dc, Span<int> residual) {
    Span<int> d = stackalloc int[16];
    _Scale(levels, qp, hasSeparateDc, dc, d);
    InverseTransform4x4(d, residual);
  }

  /// <summary>
  /// The inverse transform of clause 8.5.12.2: rows then columns, with the final rounding shift.
  /// </summary>
  /// <remarks>
  /// The butterflies are equations 8-338 to 8-341 and their column counterparts. The two right shifts
  /// inside them are the whole of what makes this an integer transform rather than a scaled one, and
  /// they are arithmetic shifts of signed values — writing them as a division by two would round
  /// negative coefficients the other way and put every block with a negative odd coefficient one
  /// level out.
  /// </remarks>
  internal static void InverseTransform4x4(ReadOnlySpan<int> d, Span<int> residual) {
    Span<int> f = stackalloc int[16];

    for (var i = 0; i < 4; ++i) {
      var row = i << 2;
      var e0 = d[row] + d[row + 2];
      var e1 = d[row] - d[row + 2];
      var e2 = (d[row + 1] >> 1) - d[row + 3];
      var e3 = d[row + 1] + (d[row + 3] >> 1);

      f[row] = e0 + e3;
      f[row + 1] = e1 + e2;
      f[row + 2] = e1 - e2;
      f[row + 3] = e0 - e3;
    }

    for (var j = 0; j < 4; ++j) {
      var g0 = f[j] + f[8 + j];
      var g1 = f[j] - f[8 + j];
      var g2 = (f[4 + j] >> 1) - f[12 + j];
      var g3 = f[4 + j] + (f[12 + j] >> 1);

      // The +32 and >>6 of equation 8-354: the transform's gain is 64, taken out once at the end
      // rather than twice at half strength, which is what keeps the intermediate values exact.
      residual[j] = (g0 + g3 + 32) >> 6;
      residual[4 + j] = (g1 + g2 + 32) >> 6;
      residual[8 + j] = (g1 - g2 + 32) >> 6;
      residual[12 + j] = (g0 - g3 + 32) >> 6;
    }
  }

  /// <summary>
  /// The luma DC transform of an Intra_16x16 macroblock: an inverse Hadamard, then scaling
  /// (clause 8.5.10).
  /// </summary>
  /// <remarks>
  /// An Intra_16x16 macroblock predicts the whole macroblock at once, so what is left in the sixteen
  /// blocks' DC coefficients is one smooth surface rather than sixteen unrelated numbers. Transforming
  /// them together and coding the result is what exploits that — and it is why the DC of such a block
  /// is dequantised here rather than in <see cref="_Scale"/>: by the time the block's own transform
  /// runs, position zero already holds a value that has been through this.
  /// </remarks>
  internal static void DecodeLumaDc(ReadOnlySpan<int> levels, int qp, Span<int> dc) {
    Span<int> c = stackalloc int[16];
    for (var scan = 0; scan < 16; ++scan)
      c[ZigZagScan4x4[scan]] = levels[scan];

    Span<int> f = stackalloc int[16];
    _InverseHadamard4x4(c, f);

    var scale = LevelScale(qp % 6, 0, 0);
    var shift = qp / 6;
    if (shift >= 6) {
      var by = shift - 6;
      for (var i = 0; i < 16; ++i)
        dc[i] = f[i] * scale << by;

      return;
    }

    var rounding = 1 << (5 - shift);
    for (var i = 0; i < 16; ++i)
      dc[i] = (f[i] * scale + rounding) >> (6 - shift);
  }

  /// <summary>
  /// The 2x2 chroma DC transform of 4:2:0 and its scaling (clauses 8.5.11.1 and 8.5.11.2).
  /// </summary>
  /// <remarks>
  /// The four DC values are in raster order, which for a 2x2 block is also the coding order — the
  /// chroma DC block has no zig-zag of its own (clause 8.5.11.1 takes <c>c</c> as transmitted).
  /// </remarks>
  internal static void DecodeChromaDc(ReadOnlySpan<int> levels, int qp, Span<int> dc) {
    // [1 1; 1 -1] * c * [1 1; 1 -1], equation 8-324.
    var f0 = levels[0] + levels[1] + levels[2] + levels[3];
    var f1 = levels[0] - levels[1] + levels[2] - levels[3];
    var f2 = levels[0] + levels[1] - levels[2] - levels[3];
    var f3 = levels[0] - levels[1] - levels[2] + levels[3];

    // Equation 8-326: a left shift by qP/6 and a right shift by five, in that order, with no
    // rounding term at all — unlike every other scaling in clause 8.5.
    var scale = LevelScale(qp % 6, 0, 0);
    var shift = qp / 6;
    dc[0] = (f0 * scale << shift) >> 5;
    dc[1] = (f1 * scale << shift) >> 5;
    dc[2] = (f2 * scale << shift) >> 5;
    dc[3] = (f3 * scale << shift) >> 5;
  }

  /// <summary>The 4x4 inverse Hadamard transform of equation 8-320.</summary>
  private static void _InverseHadamard4x4(ReadOnlySpan<int> c, Span<int> f) {
    Span<int> intermediate = stackalloc int[16];

    for (var i = 0; i < 4; ++i) {
      var row = i << 2;
      var a0 = c[row] + c[row + 1] + c[row + 2] + c[row + 3];
      var a1 = c[row] + c[row + 1] - c[row + 2] - c[row + 3];
      var a2 = c[row] - c[row + 1] - c[row + 2] + c[row + 3];
      var a3 = c[row] - c[row + 1] + c[row + 2] - c[row + 3];

      intermediate[row] = a0;
      intermediate[row + 1] = a1;
      intermediate[row + 2] = a2;
      intermediate[row + 3] = a3;
    }

    for (var j = 0; j < 4; ++j) {
      var b0 = intermediate[j] + intermediate[4 + j] + intermediate[8 + j] + intermediate[12 + j];
      var b1 = intermediate[j] + intermediate[4 + j] - intermediate[8 + j] - intermediate[12 + j];
      var b2 = intermediate[j] - intermediate[4 + j] - intermediate[8 + j] + intermediate[12 + j];
      var b3 = intermediate[j] - intermediate[4 + j] + intermediate[8 + j] - intermediate[12 + j];

      f[j] = b0;
      f[4 + j] = b1;
      f[8 + j] = b2;
      f[12 + j] = b3;
    }
  }

  /// <summary>Dequantisation of a residual 4x4 block — clause 8.5.12.1.</summary>
  private static void _Scale(ReadOnlySpan<int> levels, int qp, bool hasSeparateDc, int dc, Span<int> d) {
    var m = qp % 6;
    var shift = qp / 6;

    for (var scan = 0; scan < 16; ++scan) {
      var position = ZigZagScan4x4[scan];
      var level = levels[scan];

      if (position == 0 && hasSeparateDc) {
        d[0] = dc;
        continue;
      }

      if (level == 0) {
        d[position] = 0;
        continue;
      }

      var scaled = level * LevelScale(m, position >> 2, position & 3);

      // Equations 8-336 and 8-337. Above QP 24 the dequantised value is larger than the level and the
      // shift goes the other way, so there is nothing to round.
      d[position] = shift >= 4 ? scaled << (shift - 4) : (scaled + (1 << (3 - shift))) >> (4 - shift);
    }
  }
}
