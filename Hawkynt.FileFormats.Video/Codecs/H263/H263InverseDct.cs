using System;

namespace FileFormat.Codecs.H263;

/// <summary>
/// The inverse discrete cosine transform of ITU-T H.263 clause 6.2.2, evaluated as the Recommendation
/// defines it.
/// </summary>
/// <remarks>
/// H.263 does not specify an algorithm for this either, only the transform and the accuracy its
/// result must be within — Annex A, which sets out the same measurement IEEE 1180 does. Every decoder
/// therefore has its own and no two agree in the last bit, which is why the reconstruction levels of
/// clause 6.2.1 are always odd multiples of the step size: an odd coefficient set cannot sum to the
/// half-integer values at which two conforming transforms are free to round in opposite directions,
/// so the difference between them cannot accumulate through prediction.
/// <para/>
/// So this evaluates the defining sum in double precision rather than reproducing anyone's fast
/// integer approximation, which makes it the exact transform to within double rounding — the most
/// defensible thing to be when the reference decoder it is measured against is using an approximation
/// of the same thing.
/// <para/>
/// The two fast paths — a block whose only non-zero coefficient is the DC, and a row that is entirely
/// zero — are exact rather than approximate, and they are worth having because the first is most of
/// the blocks of a flat region and the second is most of the rows of almost every block.
/// </remarks>
internal static class H263InverseDct {

  /// <summary><c>C(u)/2 * cos((2x+1)u&#960;/16)</c>, indexed <c>[u * 8 + x]</c>.</summary>
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
  /// Transforms one block of coefficients in place into samples, saturated to the range H.263 defines
  /// the transform's output over.
  /// </summary>
  /// <param name="block">Sixty-four coefficients in raster order; overwritten with the samples.</param>
  internal static void Transform(Span<int> block) {
    if (_IsDcOnly(block)) {
      var flat = _Round(block[0] / 8d);
      block.Fill(_Saturate(flat));
      return;
    }

    Span<double> intermediate = stackalloc double[64];

    for (var y = 0; y < 8; ++y) {
      var row = y * 8;
      if (_IsZeroRow(block, row)) {
        intermediate.Slice(row, 8).Clear();
        continue;
      }

      for (var x = 0; x < 8; ++x) {
        var sum = 0d;
        for (var u = 0; u < 8; ++u) {
          var coefficient = block[row + u];
          if (coefficient != 0)
            sum += coefficient * _Basis[u * 8 + x];
        }

        intermediate[row + x] = sum;
      }
    }

    for (var x = 0; x < 8; ++x)
      for (var y = 0; y < 8; ++y) {
        var sum = 0d;
        for (var v = 0; v < 8; ++v)
          sum += intermediate[v * 8 + x] * _Basis[v * 8 + y];

        block[y * 8 + x] = _Saturate(_Round(sum));
      }
  }

  private static bool _IsDcOnly(ReadOnlySpan<int> block) {
    for (var i = 1; i < 64; ++i)
      if (block[i] != 0)
        return false;

    return true;
  }

  private static bool _IsZeroRow(ReadOnlySpan<int> block, int row) {
    for (var u = 0; u < 8; ++u)
      if (block[row + u] != 0)
        return false;

    return true;
  }

  /// <summary>Rounds to the nearest integer, halves upward — the rounding the IEEE 1180 reference uses.</summary>
  private static int _Round(double value) => (int)Math.Floor(value + 0.5d);

  /// <summary>Saturates to [-256, 255], the range H.263 Annex A defines the transform's output over.</summary>
  private static int _Saturate(int value) => value < -256 ? -256 : value > 255 ? 255 : value;
}
