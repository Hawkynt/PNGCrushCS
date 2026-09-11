using System;

namespace FileFormat.Codecs.Mpeg;

/// <summary>
/// The forward 8x8 discrete cosine transform used by MPEG-1 and MPEG-2 intra coding.
/// </summary>
/// <remarks>
/// ISO/IEC 11172-2 and ITU-T H.262 define the inverse transform and its accuracy, not an encoder
/// implementation. This is the mathematical adjoint of <see cref="MpegInverseDct"/>'s basis, evaluated
/// in double precision so the encoder and decoder quantise around the same transform rather than around
/// two unrelated fast approximations.
/// </remarks>
internal static class MpegForwardDct {

  /// <summary><c>C(u)/2 * cos((2x+1)uπ/16)</c>, indexed <c>[u * 8 + x]</c>.</summary>
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
    if (samples.Length < 64)
      throw new ArgumentException("An MPEG transform needs sixty-four source samples.", nameof(samples));
    if (coefficients.Length < 64)
      throw new ArgumentException("An MPEG transform needs room for sixty-four coefficients.", nameof(coefficients));

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
