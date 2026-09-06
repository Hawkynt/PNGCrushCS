using System;

namespace FileFormat.Codecs.H261;

/// <summary>
/// The forward discrete cosine transform, evaluated from the same basis
/// <see cref="H263.H263InverseDct"/> inverts.
/// </summary>
/// <remarks>
/// ITU-T H.261 specifies the inverse transform only, and only as an accuracy bound (Annex A). The
/// forward one is the encoder's business entirely and nothing conforms or fails to conform by it. What
/// does matter is that it is the adjoint of the inverse this library evaluates, so a block quantised at
/// the finest step and reconstructed comes back to what went in; a mismatched pair looks exactly like
/// the quantiser being coarser than it is, and it biases every mode decision that compares a coded
/// block against an uncoded one.
/// <para/>
/// So the basis is built here the way <see cref="H263.H263InverseDct"/> builds it and the two loops are
/// the same loops with the indices exchanged. Double precision throughout, because the cost is paid
/// once per block on an encode and the alternative is adopting somebody's fast approximation together
/// with its bias.
/// </remarks>
internal static class H261ForwardDct {

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

  /// <summary>Transforms sixty-four samples in raster order into sixty-four coefficients.</summary>
  internal static void Transform(scoped ReadOnlySpan<int> samples, scoped Span<double> coefficients) {
    Span<double> intermediate = stackalloc double[64];

    for (var y = 0; y < 8; ++y)
      for (var u = 0; u < 8; ++u) {
        var sum = 0d;
        for (var x = 0; x < 8; ++x)
          sum += samples[y * 8 + x] * _Basis[u * 8 + x];

        intermediate[y * 8 + u] = sum;
      }

    for (var u = 0; u < 8; ++u)
      for (var v = 0; v < 8; ++v) {
        var sum = 0d;
        for (var y = 0; y < 8; ++y)
          sum += intermediate[y * 8 + u] * _Basis[v * 8 + y];

        coefficients[v * 8 + u] = sum;
      }
  }
}
