using System;

namespace FileFormat.Codecs.Vc1;

/// <summary>
/// The 8x8 inverse transform of SMPTE 421M Annex A.
/// </summary>
/// <remarks>
/// Similar to an inverse discrete cosine transform and deliberately not one. It is defined as exact
/// integer matrix arithmetic — two multiplications by a constant matrix with a stated rounding and
/// shift between them — so every decoder produces bit-identical output, which is what makes an
/// intra-coded picture comparable sample for sample against a reference rather than approximately.
/// <para/>
/// The rounding of the second stage is the part that looks wrong and is not. Annex A adds a column
/// vector <c>C8 = (0 0 0 0 1 1 1 1)'</c> before the shift, so the lower four rows of every block round
/// one way and the upper four the other. Dropping it, or applying it along the wrong axis, moves about
/// half the samples of a picture by a single level — which survives casual inspection and fails an
/// exact comparison.
/// </remarks>
internal static class Vc1InverseTransform {

  /// <summary>The 8-point transform matrix of Figure 157.</summary>
  private static ReadOnlySpan<int> _T8 => [
    12, 12, 12, 12, 12, 12, 12, 12,
    16, 15, 9, 4, -4, -9, -15, -16,
    16, 6, -6, -16, -16, -6, 6, 16,
    15, -4, -16, -9, 9, 16, 4, -15,
    12, -12, -12, 12, 12, -12, -12, 12,
    9, -16, 4, 15, -15, -4, 16, -9,
    6, -16, 16, -6, -6, 16, -16, 6,
    4, -9, 15, -16, 16, -15, 9, -4,
  ];

  /// <summary>
  /// Transforms one 8x8 block of inverse quantised coefficients into spatial samples, in place.
  /// </summary>
  /// <remarks>
  /// The output is the 10-bit signed reconstruction, without the constant 128 added and without
  /// clamping: overlap smoothing runs on exactly this, because the smoothing filter can push a value
  /// past what eight bits hold and clamping first would lose it (8.5).
  /// </remarks>
  internal static void Apply(Span<int> block) {
    Span<int> intermediate = stackalloc int[64];

    // First stage, along the rows: E = (D . T8 + 4) >> 3, so the sum runs down a column of T8.
    for (var row = 0; row < 8; ++row) {
      var from = row * 8;
      for (var column = 0; column < 8; ++column) {
        var sum = 0;
        for (var k = 0; k < 8; ++k)
          sum += block[from + k] * _T8[(k * 8) + column];

        intermediate[from + column] = (sum + 4) >> 3;
      }
    }

    // Second stage, along the columns: R = (T8' . E + C8 . 1 + 64) >> 7. The transpose means this sum
    // runs down a column of T8 as well, indexed by the output row; the added column vector is nought
    // for the first four rows and one for the last four.
    for (var column = 0; column < 8; ++column) {
      for (var row = 0; row < 8; ++row) {
        var sum = 0;
        for (var k = 0; k < 8; ++k)
          sum += intermediate[(k * 8) + column] * _T8[(k * 8) + row];

        block[(row * 8) + column] = (sum + 64 + (row >= 4 ? 1 : 0)) >> 7;
      }
    }
  }
}
