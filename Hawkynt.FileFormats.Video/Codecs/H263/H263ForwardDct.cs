using System;

namespace FileFormat.Codecs.H263;

/// <summary>The forward transform paired with <see cref="H263InverseDct"/> for encoding.</summary>
internal static class H263ForwardDct {

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