using System;

namespace FileFormat.Codecs.Vp3;

/// <summary>Forward 8×8 DCT with VP3's coefficient scaling.</summary>
/// <remarks>
/// VP3's quantisation matrices contain the factor of four described by the Theora specification: the
/// coefficient representation fed to the inverse transform is four times an orthonormal DCT. This
/// implementation computes that transform directly and independently from the integer inverse path.
/// It is separable, so a block costs two 8×8 passes rather than sixty-four full 8×8 dot products.
/// </remarks>
internal static class Vp3ForwardDct {
  private const int _SIDE = 8;
  private static readonly double[] _Basis = _BuildBasis();

  /// <summary>
  /// Transforms sixty-four spatial residuals in raster order to sixty-four natural-order VP3
  /// coefficients. The returned coefficients have VP3's ×4 transform scale and are not quantised.
  /// </summary>
  internal static void Transform(ReadOnlySpan<int> samples, Span<double> coefficients) {
    if (samples.Length < 64 || coefficients.Length < 64)
      throw new ArgumentException("A VP3 DCT block is exactly 8×8 samples.");

    Span<double> horizontal = stackalloc double[64];

    for (var y = 0; y < _SIDE; ++y)
    for (var u = 0; u < _SIDE; ++u) {
      var sum = 0.0;
      for (var x = 0; x < _SIDE; ++x)
        sum += samples[y * _SIDE + x] * _Basis[u * _SIDE + x];
      horizontal[y * _SIDE + u] = sum;
    }

    for (var v = 0; v < _SIDE; ++v)
    for (var u = 0; u < _SIDE; ++u) {
      var sum = 0.0;
      for (var y = 0; y < _SIDE; ++y)
        sum += horizontal[y * _SIDE + u] * _Basis[v * _SIDE + y];
      coefficients[v * _SIDE + u] = sum * 4.0;
    }
  }

  private static double[] _BuildBasis() {
    var result = new double[64];
    for (var frequency = 0; frequency < _SIDE; ++frequency) {
      var scale = frequency == 0 ? 1.0 / Math.Sqrt(8.0) : 0.5;
      for (var sample = 0; sample < _SIDE; ++sample)
        result[frequency * _SIDE + sample] =
          scale * Math.Cos((2 * sample + 1) * frequency * Math.PI / 16.0);
    }

    return result;
  }
}
