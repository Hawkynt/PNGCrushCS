using System;

namespace FileFormat.Codecs.Vc1;

/// <summary>The encoder-side analytical inverse of VC-1's normative 8x8 inverse transform.</summary>
/// <remarks>
/// Annex A.2 deliberately leaves encoder rounding and representation open. The matrix in Figure 157
/// has mutually orthogonal rows, so the forward transform follows directly from the normative inverse:
/// multiply by <c>T8</c> on both sides and divide each coefficient by the squared norms of the
/// corresponding rows. Keeping the derivation here avoids importing a reference encoder's choice of
/// butterfly, scaling or rounding.
/// </remarks>
internal static class Vc1ForwardTransform {

  /// <summary>Squared Euclidean norm of each row of Figure 157's matrix, derived from that matrix.</summary>
  private static ReadOnlySpan<int> _RowNormSquared => [1152, 1156, 1168, 1156, 1152, 1156, 1168, 1156];

  /// <summary>
  /// Transforms and quantises one 8x8 sample block for the uniform quantiser used by an intra picture.
  /// </summary>
  internal static void Quantise8x8(
    int[] samples,
    int stride,
    int x,
    int y,
    int dcStep,
    int acStep,
    Span<int> quantised) {
    ArgumentNullException.ThrowIfNull(samples);
    if (quantised.Length < 64)
      throw new ArgumentException("A VC-1 8x8 transform needs sixty-four coefficient slots.", nameof(quantised));

    var transform = Vc1InverseTransform.Matrix8;
    Span<int> horizontal = stackalloc int[64];

    // A = T8 . R. The source is already padded to a whole macroblock, so every 8x8 access is in range.
    for (var coefficientRow = 0; coefficientRow < 8; ++coefficientRow)
      for (var column = 0; column < 8; ++column) {
        var sum = 0;
        for (var sampleRow = 0; sampleRow < 8; ++sampleRow)
          sum += transform[(coefficientRow * 8) + sampleRow]
                 * samples[((y + sampleRow) * stride) + x + column];

        horizontal[(coefficientRow * 8) + column] = sum;
      }

    // D = 1024 . N^-1 . T8 . R . T8' . N^-1, where N is the diagonal matrix of
    // row-norm-squared values. 1024 is the product of the two inverse-transform stage scales.
    for (var coefficientRow = 0; coefficientRow < 8; ++coefficientRow)
      for (var coefficientColumn = 0; coefficientColumn < 8; ++coefficientColumn) {
        long sum = 0;
        for (var sampleColumn = 0; sampleColumn < 8; ++sampleColumn)
          sum += (long)horizontal[(coefficientRow * 8) + sampleColumn]
                 * transform[(coefficientColumn * 8) + sampleColumn];

        var coefficient = _DivideRounded(
          sum * 1024,
          (long)_RowNormSquared[coefficientRow] * _RowNormSquared[coefficientColumn]);
        var step = coefficientRow == 0 && coefficientColumn == 0 ? dcStep : acStep;
        quantised[(coefficientRow * 8) + coefficientColumn] = _DivideRounded(coefficient, step);
      }
  }

  private static int _DivideRounded(long numerator, long denominator) {
    if (denominator <= 0)
      throw new ArgumentOutOfRangeException(nameof(denominator));

    var magnitude = numerator < 0 ? -numerator : numerator;
    var rounded = (magnitude + (denominator >> 1)) / denominator;
    return checked((int)(numerator < 0 ? -rounded : rounded));
  }
}
