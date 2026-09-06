using System;

namespace FileFormat.Codecs.ProRes;

/// <summary>
/// The separable cosine basis both transforms of RDD 36:2022, 7.4 are built from.
/// </summary>
/// <remarks>
/// <c>C(u)/2 · cos((2x+1)uπ/16)</c>, indexed <c>[u * 8 + x]</c>. Half of the transform's <c>1/4</c>
/// scale factor is folded into each of the two passes, so applying the table twice produces the
/// <c>1/4 C(u) C(v)</c> the definition carries — in either direction, since the forward and inverse
/// transforms of an orthonormal pair differ only in which index the sum runs over.
/// <para/>
/// One table for both directions rather than two, so that an encoder and a decoder in the same
/// process cannot come to disagree about the transform they are inverses of.
/// </remarks>
internal static class ProResDctBasis {

  /// <summary><c>C(u)/2 · cos((2x+1)uπ/16)</c>, indexed <c>[u * 8 + x]</c>.</summary>
  internal static readonly double[] Cosines = _Build();

  private static double[] _Build() {
    var basis = new double[64];
    for (var u = 0; u < 8; ++u) {
      var scale = (u == 0 ? 1d / Math.Sqrt(2d) : 1d) / 2d;
      for (var x = 0; x < 8; ++x)
        basis[u * 8 + x] = scale * Math.Cos((2 * x + 1) * u * Math.PI / 16d);
    }

    return basis;
  }
}
