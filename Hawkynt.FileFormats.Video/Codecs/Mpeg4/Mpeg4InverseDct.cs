using System;

namespace FileFormat.Codecs.Mpeg4;

/// <summary>
/// The inverse discrete cosine transform of ISO/IEC 14496-2 clause 7.4.5, evaluated as the standard
/// defines it.
/// </summary>
/// <remarks>
/// The standard does not specify an algorithm for this, only the transform and an accuracy the result
/// must be within (Annex A, which sets out the same measurement IEEE 1180 does). Every decoder
/// therefore has its own and no two agree in the last bit, which is why the reconstruction levels of
/// 7.4.4 are always odd multiples of the step size under the H.263 quantisation method: an odd
/// coefficient set cannot sum to the half-integer values at which two conforming transforms are free
/// to round in opposite directions, so the difference between them cannot accumulate through
/// prediction.
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
internal static class Mpeg4InverseDct {

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
  /// Transforms one block of coefficients in place into samples, saturated to the range ISO/IEC
  /// 14496-2 defines the transform's output over.
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

  /// <summary>
  /// Rounds to the nearest integer, and at an exact half to the even one.
  /// </summary>
  /// <remarks>
  /// The tie is worth choosing deliberately here, which it is not in H.263 or MPEG-1. Those two force
  /// every reconstruction level to an odd multiple of the step size, so a block's samples land on an
  /// exact half only by accident. MPEG-4's intra DC is the step size times the coded level with no
  /// such forcing, and the transform of a block whose only coefficient is that DC is the DC over
  /// eight — so a quarter of all flat intra blocks land on an exact half, and which way they go is a
  /// visible decision rather than a rounding detail.
  /// <para/>
  /// To the even one, because rounding every tie upward biases those blocks half a level brighter,
  /// and a quarter of the blocks of a picture carrying half a level is a brightening that survives
  /// into every picture predicted from it. It is also what the reference decoder's floating-point
  /// transform does: measured over the streams in the decoder's own remarks, rounding ties upward
  /// leaves twenty-nine per cent of the samples of a 352x288 stream one level out, and rounding them
  /// to even leaves none.
  /// </remarks>
  private static int _Round(double value) => (int)Math.Round(value, MidpointRounding.ToEven);

  /// <summary>Saturates to [-256, 255], the range the transform's output is defined over.</summary>
  private static int _Saturate(int value) => value < -256 ? -256 : value > 255 ? 255 : value;
}
