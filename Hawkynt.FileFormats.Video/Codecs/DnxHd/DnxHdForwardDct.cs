using System;

namespace FileFormat.Codecs.DnxHd;

/// <summary>The forward 8x8 DCT defined by SMPTE ST 2019-1, evaluated in double precision.</summary>
internal static class DnxHdForwardDct {

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

  /// <summary>Transforms signed, level-shifted samples in raster order to coefficients in raster order.</summary>
  internal static void Transform(ReadOnlySpan<double> samples, Span<double> coefficients) {
    if (samples.Length < 64)
      throw new ArgumentException("A VC-3 DCT needs 64 samples.", nameof(samples));
    if (coefficients.Length < 64)
      throw new ArgumentException("A VC-3 DCT produces 64 coefficients.", nameof(coefficients));

    var flat = samples[0];
    var allFlat = true;
    for (var i = 1; i < 64; ++i)
      if (samples[i] != flat) {
        allFlat = false;
        break;
      }

    if (allFlat) {
      coefficients[..64].Clear();
      coefficients[0] = flat * 8d;
      return;
    }

    Span<double> intermediate = stackalloc double[64];

    for (var y = 0; y < 8; ++y)
      for (var u = 0; u < 8; ++u) {
        var sum = 0d;
        for (var x = 0; x < 8; ++x)
          sum += samples[y * 8 + x] * _Basis[u * 8 + x];

        intermediate[y * 8 + u] = sum;
      }

    for (var v = 0; v < 8; ++v)
      for (var u = 0; u < 8; ++u) {
        var sum = 0d;
        for (var y = 0; y < 8; ++y)
          sum += intermediate[y * 8 + u] * _Basis[v * 8 + y];

        coefficients[v * 8 + u] = sum;
      }
  }
}
