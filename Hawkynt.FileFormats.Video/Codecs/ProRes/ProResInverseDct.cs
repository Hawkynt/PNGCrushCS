using System;

namespace FileFormat.Codecs.ProRes;

/// <summary>
/// The inverse discrete cosine transform of RDD 36:2022, 7.4, evaluated as the specification
/// defines it.
/// </summary>
/// <remarks>
/// RDD 36 does not specify an algorithm for this and says so: 7.4 allows either a fixed-point or a
/// floating-point implementation and requires only that it pass the accuracy qualification of
/// Annex A, which is the IEEE Std 1180-1990 procedure. Every decoder therefore has its own
/// transform and no two agree in the last bit.
/// <para/>
/// So this evaluates the defining sum in double precision rather than reproducing anyone's fast
/// integer approximation, exactly as <c>MpegInverseDct</c> does in this library and for the same
/// reason: it is the exact transform to within double rounding, which is the most defensible thing
/// to be when the reference decoder it is measured against is using an approximation of the same
/// thing. ProRes being intra only, the difference cannot accumulate — it is bounded within each
/// block of each frame and never predicted from.
/// <para/>
/// Measured, it is the whole of the disagreement with ffmpeg and it is one level. Across every
/// profile, both encoders, progressive and interlaced, and sizes from 40x24 to 1280x718, no sample
/// of any plane differs by more than one at the coded depth and no difference other than one ever
/// occurs — between 0.4 and 2 per cent of samples at ten bits, and around 7 per cent at twelve,
/// where the same absolute error spans four times as many quantisation levels.
/// <para/>
/// The transform takes and returns doubles because 7.3 requires at least two fraction bits of the
/// dequantised coefficients to survive into it — they are always multiples of one eighth — and 7.4
/// asks that the fraction bits of the result survive out of it into the sample conversion. Rounding
/// at either boundary would be a second quantisation the format does not have.
/// </remarks>
internal static class ProResInverseDct {

  /// <summary>
  /// <c>C(u)/2 * cos((2x+1)u&#960;/16)</c>, indexed <c>[u * 8 + x]</c>.
  /// </summary>
  /// <remarks>
  /// Half of the transform's <c>1/4</c> scale factor is folded into each of the two passes, so
  /// applying the table twice produces the <c>1/4 C(u) C(v)</c> the definition carries.
  /// </remarks>
  private static readonly double[] _Basis = _BuildBasis();

  private static double[] _BuildBasis() {
    var basis = new double[64];
    for (var u = 0; u < 8; ++u) {
      var scale = (u == 0 ? 1d / Math.Sqrt(2d) : 1d) / 2d;
      for (var x = 0; x < 8; ++x)
        basis[u * 8 + x] = scale * Math.Cos((2 * x + 1) * u * Math.PI / 16d);
    }

    return basis;
  }

  /// <summary>
  /// Transforms one block of dequantised coefficients in place into reconstructed component values.
  /// </summary>
  /// <param name="block">Sixty-four coefficients in raster order, <c>[v * 8 + u]</c>; overwritten
  /// with the reconstructed values in raster order, <c>[y * 8 + x]</c>.</param>
  internal static void Transform(Span<double> block) {
    // A block whose only non-zero coefficient is the DC is a flat block, and most blocks of a flat
    // region are exactly that. The shortcut is the exact answer rather than an approximation of it:
    // with every other coefficient zero the double sum collapses to C(0)C(0)F[0][0]/4, which is the
    // DC over eight.
    if (_IsDcOnly(block)) {
      block.Fill(block[0] / 8d);
      return;
    }

    Span<double> intermediate = stackalloc double[64];

    // Rows first: each row of coefficients becomes a row of partial sums over the horizontal
    // frequencies. A row of zeroes stays a row of zeroes, which is worth testing for because most
    // rows of most blocks are exactly that.
    for (var v = 0; v < 8; ++v) {
      var row = v * 8;
      if (_IsZeroRow(block, row)) {
        intermediate.Slice(row, 8).Clear();
        continue;
      }

      for (var x = 0; x < 8; ++x) {
        var sum = 0d;
        for (var u = 0; u < 8; ++u) {
          var coefficient = block[row + u];
          if (coefficient != 0d)
            sum += coefficient * _Basis[u * 8 + x];
        }

        intermediate[row + x] = sum;
      }
    }

    // Then columns, which finishes the separable transform.
    for (var x = 0; x < 8; ++x)
      for (var y = 0; y < 8; ++y) {
        var sum = 0d;
        for (var v = 0; v < 8; ++v)
          sum += intermediate[v * 8 + x] * _Basis[v * 8 + y];

        block[y * 8 + x] = sum;
      }
  }

  private static bool _IsDcOnly(ReadOnlySpan<double> block) {
    for (var i = 1; i < 64; ++i)
      if (block[i] != 0d)
        return false;

    return true;
  }

  private static bool _IsZeroRow(ReadOnlySpan<double> block, int row) {
    for (var u = 0; u < 8; ++u)
      if (block[row + u] != 0d)
        return false;

    return true;
  }
}
