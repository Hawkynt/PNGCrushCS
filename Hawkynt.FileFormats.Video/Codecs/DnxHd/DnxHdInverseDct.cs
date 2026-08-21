using System;

namespace FileFormat.Codecs.DnxHd;

/// <summary>
/// The inverse discrete cosine transform of SMPTE ST 2019-1:2016, 8.2.8.1, evaluated as the standard
/// defines it.
/// </summary>
/// <remarks>
/// The standard gives the transform as a defining sum and no algorithm for it. The conformance
/// document for VC-3 decoders — SMPTE RP 2019-2 — settles the accuracy instead, by the ISO/IEC
/// 23002-1 procedure, so any implementation within those bounds is a conforming one and no two
/// decoders agree in the last bit.
/// <para/>
/// So this evaluates the defining sum in double precision rather than reproducing anyone's fast
/// integer approximation, as <c>MpegInverseDct</c> does in this library and for the same reason: it
/// is the exact transform to within double rounding, which is the most defensible thing to be when
/// the reference decoder it is measured against is using an approximation of the same thing. VC-3
/// being intra only, the difference cannot accumulate — it is bounded within each block of each
/// frame and never predicted from. What it measures at is in the decoder's own remarks.
/// </remarks>
internal static class DnxHdInverseDct {

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
  /// Transforms one block of coefficients in place into video samples.
  /// </summary>
  /// <param name="block">Sixty-four coefficients in raster order, <c>[v * 8 + u]</c>; overwritten
  /// with the samples in raster order, <c>[j * 8 + i]</c>.</param>
  internal static void Transform(Span<double> block) {
    // A block whose only non-zero coefficient is the DC is a flat block, and most blocks of a flat
    // region are exactly that. The shortcut is exact rather than an approximation: with everything
    // else zero the double sum collapses to C(0)C(0)X(0,0)/4, which is the DC over eight.
    if (_IsDcOnly(block)) {
      block.Fill(block[0] / 8d);
      return;
    }

    Span<double> intermediate = stackalloc double[64];

    // Rows first: each row of coefficients becomes a row of partial sums over the horizontal
    // frequencies. A row of zeroes stays a row of zeroes, which is worth testing for because after
    // the zig-zag most rows of most blocks are exactly that.
    for (var v = 0; v < 8; ++v) {
      var row = v * 8;
      if (_IsZeroRow(block, row)) {
        intermediate.Slice(row, 8).Clear();
        continue;
      }

      for (var i = 0; i < 8; ++i) {
        var sum = 0d;
        for (var u = 0; u < 8; ++u) {
          var coefficient = block[row + u];
          if (coefficient != 0d)
            sum += coefficient * _Basis[u * 8 + i];
        }

        intermediate[row + i] = sum;
      }
    }

    // Then columns, which finishes the separable transform.
    for (var i = 0; i < 8; ++i)
      for (var j = 0; j < 8; ++j) {
        var sum = 0d;
        for (var v = 0; v < 8; ++v)
          sum += intermediate[v * 8 + i] * _Basis[v * 8 + j];

        block[j * 8 + i] = sum;
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
