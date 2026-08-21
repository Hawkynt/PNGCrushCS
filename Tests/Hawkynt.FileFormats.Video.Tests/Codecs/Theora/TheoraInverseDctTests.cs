using System;

namespace FileFormat.Codecs.Theora.Tests;

/// <summary>
/// The inverse transform, which the specification defines to the bit.
/// </summary>
/// <remarks>
/// Section 7.9.3 requires the exact integer operations rather than an accuracy bound, because
/// Theora's DC prediction carries one block's quantised value into the next and a single rounding
/// difference would work its way through the rest of the frame and then through every frame
/// predicted from it. So the numbers below are worked through from the specification's own steps and
/// the constants of Table 7.65, not recorded from a run: where one disagrees with the decoder, the
/// arithmetic in the comment says which of the two is wrong.
/// </remarks>
[TestFixture]
public sealed class TheoraInverseDctTests {

  [Test]
  [Category("Unit")]
  public void TheTransformOfNothingIsNothing() {
    Span<int> coefficients = stackalloc int[64];
    Span<int> residual = stackalloc int[64];

    TheoraInverseDct.Transform(coefficients, residual);

    foreach (var value in residual)
      Assert.That(value, Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void ADirectCurrentCoefficientAloneGivesAFlatBlock() {
    // With only Y[0] non-zero, every step of the one-dimensional transform collapses: T[2] through
    // T[7] are zero, T[0] and T[1] are both C4 * Y[0] >> 16, and all eight outputs come out equal to
    // that. So a row transform of [64, 0...] gives 46341 * 64 >> 16 = 45 in every column, and the
    // column transform of [45, 0...] gives 46341 * 45 >> 16 = 31, rounded by (31 + 8) >> 4 = 2.
    Span<int> coefficients = stackalloc int[64];
    Span<int> residual = stackalloc int[64];
    coefficients[0] = 64;

    TheoraInverseDct.Transform(coefficients, residual);

    foreach (var value in residual)
      Assert.That(value, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void TheFirstAlternatingCoefficientGivesAHorizontalRamp() {
    // Coefficients 0 and 1 both 64. The row transform of [64, 64, 0...] works through as
    //   T[0] = T[1] = C4 * 64 >> 16                 = 45
    //   T[4] = C7 * 64 >> 16 = 12785 * 64 >> 16     = 12
    //   T[7] = C1 * 64 >> 16 = 64277 * 64 >> 16     = 62
    //   T[5] = C4 * 12 >> 16 = 8,   T[6] = C4 * 62 >> 16 = 43
    //   T[6], T[5] = 43 + 8, 43 - 8                 = 51, 35
    // giving X = [107, 96, 80, 57, 33, 10, -6, -17]. Every other row is zero, so each column is a
    // direct-current transform of its own value: C4 * X >> 16, then (v + 8) >> 4.
    Span<int> coefficients = stackalloc int[64];
    Span<int> residual = stackalloc int[64];
    coefficients[0] = 64;
    coefficients[1] = 64;

    TheoraInverseDct.Transform(coefficients, residual);

    int[] expected = [5, 4, 4, 3, 1, 0, 0, -1];
    for (var row = 0; row < 8; ++row)
    for (var column = 0; column < 8; ++column)
      Assert.That(residual[row * 8 + column], Is.EqualTo(expected[column]),
        $"row {row}, column {column}");
  }

  [Test]
  [Category("Unit")]
  public void TheFirstVerticalCoefficientGivesAVerticalRamp() {
    // The same coefficient in the other direction — natural index 8 is the first row frequency. It
    // gives a ramp down the rows, which is the check that the second pass reads its input down the
    // columns rather than across them: that is the one way round a transform can be wrong and still
    // look entirely plausible.
    //
    // Note that it is *not* the horizontal ramp transposed. The row pass here produces 45 in both of
    // the first two rows, and the column pass then transforms [45, 45, 0...] where the horizontal
    // case transformed [64, 64, 0...] — so the fourth entry comes out 2 rather than 3. The
    // asymmetry is the specification's: only the second pass rounds, and the first truncates its
    // output to sixteen bits before the second reads it.
    Span<int> coefficients = stackalloc int[64];
    Span<int> residual = stackalloc int[64];
    coefficients[0] = 64;
    coefficients[8] = 64;

    TheoraInverseDct.Transform(coefficients, residual);

    int[] expected = [5, 4, 4, 2, 1, 0, 0, -1];
    for (var row = 0; row < 8; ++row)
    for (var column = 0; column < 8; ++column)
      Assert.That(residual[row * 8 + column], Is.EqualTo(expected[row]),
        $"row {row}, column {column}");
  }
}
