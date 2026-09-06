using System;

namespace FileFormat.Codecs.MsMpeg4;

/// <summary>
/// The forward discrete cosine transform, evaluated from the same basis the inverse uses.
/// </summary>
/// <remarks>
/// The standard specifies only the inverse transform, and only as an accuracy bound; the forward one
/// is the encoder's business entirely and nothing conforms or fails to conform by it. What matters is
/// that it is the adjoint of the inverse this library evaluates, so that a block quantised at the
/// finest step and reconstructed comes back to what went in — which is the property a rate-distortion
/// decision silently relies on and which a mismatched pair of transforms breaks in a way that looks
/// like the quantiser being coarser than it is.
/// <para/>
/// So the basis is built here the way <see cref="Mpeg4.Mpeg4InverseDct"/> builds it, and the two loops
/// are the same loops with the indices exchanged. Double precision throughout, because the cost is
/// paid once per block on an encode and the alternative is choosing somebody's fast approximation and
/// inheriting its bias.
/// </remarks>
internal static class MsMpeg4ForwardDct {

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
