using System;

namespace FileFormat.Codecs.Asv;

/// <summary>
/// The forward discrete cosine transform, evaluated from the same basis
/// <see cref="H263.H263InverseDct"/> inverts.
/// </summary>
/// <remarks>
/// asv1.txt states the inverse transform and nothing about the forward one, which is the encoder's
/// business entirely — no stream conforms or fails to conform by it. What does matter is that it is
/// the adjoint of the inverse this library evaluates, so that a coefficient quantised at the finest
/// step and reconstructed comes back to what went in; a mismatched pair looks exactly like a
/// quantiser coarser than the one the file states, and nothing in the bitstream would say so.
/// <para/>
/// Double precision throughout. The cost is paid once a block on an encode, and the alternative is
/// adopting somebody's fast integer approximation and inheriting its bias into every measurement made
/// against a decoder that uses a different one.
/// <para/>
/// The scale is the transform's own: sixty-four samples of value <c>v</c> give a direct-current
/// coefficient of <c>8v</c>, which is exactly the <c>c00' = 8 * c00</c> of clause 3.5 read backwards.
/// </remarks>
internal static class AsvForwardDct {

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
