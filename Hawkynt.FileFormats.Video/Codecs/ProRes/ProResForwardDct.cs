using System;

namespace FileFormat.Codecs.ProRes;

/// <summary>
/// The forward discrete cosine transform, evaluated as the inverse of the one RDD 36:2022, 7.4
/// defines.
/// </summary>
/// <remarks>
/// RDD 36 specifies no transform for an encoder at all — it describes a decoding process — so what
/// the forward direction has to be is whatever inverts <see cref="ProResInverseDct"/> exactly. That
/// is the defining sum with the same basis, evaluated in double precision, which is what this is: the
/// two share one <see cref="ProResDctBasis"/> so that they cannot come to disagree about the
/// transform they are inverses of.
/// <para/>
/// Double precision throughout rather than a fixed-point approximation, for the same reason the
/// inverse is: the coefficients that come out are quantised immediately afterwards, and rounding them
/// twice — once for the transform's own arithmetic and once for the quantiser — is a second
/// quantisation the format does not have.
/// </remarks>
internal static class ProResForwardDct {

  /// <summary>
  /// Transforms one block of component values in place into coefficients.
  /// </summary>
  /// <param name="block">Sixty-four values in raster order, <c>[y * 8 + x]</c>; overwritten with the
  /// coefficients in raster order, <c>[v * 8 + u]</c>.</param>
  internal static void Transform(Span<double> block) {
    var basis = ProResDctBasis.Cosines;
    Span<double> intermediate = stackalloc double[64];

    // Rows first: each row of samples becomes a row of horizontal frequencies.
    for (var y = 0; y < 8; ++y) {
      var row = y * 8;
      for (var u = 0; u < 8; ++u) {
        var sum = 0d;
        for (var x = 0; x < 8; ++x)
          sum += block[row + x] * basis[u * 8 + x];

        intermediate[row + u] = sum;
      }
    }

    // Then columns, which finishes the separable transform.
    for (var u = 0; u < 8; ++u)
      for (var v = 0; v < 8; ++v) {
        var sum = 0d;
        for (var y = 0; y < 8; ++y)
          sum += intermediate[y * 8 + u] * basis[v * 8 + y];

        block[v * 8 + u] = sum;
      }
  }
}
