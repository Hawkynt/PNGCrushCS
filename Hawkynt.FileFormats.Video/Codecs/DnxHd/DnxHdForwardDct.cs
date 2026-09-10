using System;

namespace FileFormat.Codecs.DnxHd;

/// <summary>
/// The reference forward DCT of SMPTE ST 2019-1:2016, 8.2.8.2, for the eight-bit encoder.
/// </summary>
/// <remarks>
/// The standard gives the FDCT as an informative defining sum and the decoder evaluates the matching
/// inverse sum in <see cref="DnxHdInverseDct"/>. This deliberately does the same thing in double
/// precision instead of importing somebody else's integer approximation; the transform output is
/// rounded once, at the boundary where the VC-3 bitstream becomes integer coefficients.
/// </remarks>
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

  /// <summary>
  /// Transforms one 8x8 source block into integer DCT coefficients in raster-frequency order.
  /// </summary>
  /// <remarks>
  /// Section 8.2.8.3 level-shifts eight-bit input to -128…127 before the transform. The padded source
  /// is always a whole macroblock, so the eight samples addressed here are guaranteed to exist.
  /// </remarks>
  internal static void Transform(
    ReadOnlySpan<ushort> plane, int stride, int x, int y, Span<int> coefficients) {

    Span<double> horizontal = stackalloc double[64];

    for (var j = 0; j < 8; ++j) {
      var row = (y + j) * stride + x;
      for (var u = 0; u < 8; ++u) {
        var sum = 0d;
        for (var i = 0; i < 8; ++i)
          sum += (plane[row + i] - 128) * _Basis[u * 8 + i];

        horizontal[j * 8 + u] = sum;
      }
    }

    for (var v = 0; v < 8; ++v)
      for (var u = 0; u < 8; ++u) {
        var sum = 0d;
        for (var j = 0; j < 8; ++j)
          sum += horizontal[j * 8 + u] * _Basis[v * 8 + j];

        coefficients[v * 8 + u] = (int)Math.Round(sum, MidpointRounding.AwayFromZero);
      }
  }
}
