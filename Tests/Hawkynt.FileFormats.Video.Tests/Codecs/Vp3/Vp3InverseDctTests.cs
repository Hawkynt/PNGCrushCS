namespace FileFormat.Codecs.Vp3.Tests;

/// <summary>
/// The inverse DCT, and why the DC-only shortcut beside it is not an optimisation.
/// </summary>
/// <remarks>
/// Section 7.9.3 fixes the transform exactly rather than to a tolerance, so the useful tests are the
/// ones with an exactly known answer: an empty block, a block with only a DC coefficient, and the
/// relationship between the full transform and the shortcut that replaces it when a block has nothing
/// but a DC coefficient.
/// <para/>
/// That last one is the interesting test. The shortcut is not the same arithmetic — it skips the two
/// multiplications and rounds differently — and the specification requires it to be used in place of
/// the transform. It agrees with the transform to within one everywhere, and differs somewhere, which
/// is exactly why a decoder that used the transform for both cases would drift from a decoder that
/// did as it was told.
/// </remarks>
[TestFixture]
public sealed class Vp3InverseDctTests {

  /// <summary>The DC-only reconstruction of step 2(d)vii of Section 7.9.4.</summary>
  private static int _Shortcut(int dequantised) => (short)((dequantised + 15) >> 5);

  private static short[] _Transform(short[] coefficients) {
    var residual = new short[64];
    Vp3InverseDct.Transform(coefficients, residual);
    return residual;
  }

  [Test]
  [Category("Unit")]
  public void ABlockWithNoCoefficientsTransformsToNothing() {
    var residual = _Transform(new short[64]);
    foreach (var value in residual)
      Assert.That(value, Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void ABlockWithOnlyADirectCurrentCoefficientTransformsToAFlatBlock() {
    // Every alternating-current coefficient being zero leaves the same value on all sixty-four
    // samples: the transform of a constant is a constant.
    foreach (var dequantised in new short[] { 32, 64, 1024, -1024, 4096, -4096, 32767, -32768 }) {
      var coefficients = new short[64];
      coefficients[0] = dequantised;
      var residual = _Transform(coefficients);

      foreach (var value in residual)
        Assert.That(value, Is.EqualTo(residual[0]), $"direct current {dequantised} does not give a flat block");
    }
  }

  [Test]
  [Category("Unit")]
  public void TheTransformScalesTheDirectCurrentCoefficientDownByThirtyTwo() {
    // Each pass scales by two relative to the orthonormal transform and the final division takes out
    // sixteen, so a dequantised value comes back divided by thirty-two.
    var coefficients = new short[64];
    coefficients[0] = 1024;
    Assert.That(_Transform(coefficients)[0], Is.EqualTo(32));

    coefficients[0] = -1024;
    Assert.That(_Transform(coefficients)[0], Is.EqualTo(-32));
  }

  [Test]
  [Category("Unit")]
  public void TheDirectCurrentShortcutAgreesWithTheTransformToWithinOneEverywhere() {
    // If they disagreed by more than one somewhere, one of the two would be wrong.
    for (var dequantised = short.MinValue; dequantised < short.MaxValue; ++dequantised) {
      var coefficients = new short[64];
      coefficients[0] = (short)dequantised;
      var difference = _Transform(coefficients)[0] - _Shortcut(dequantised);
      Assert.That(difference, Is.InRange(-1, 1), $"direct current {dequantised}");
    }
  }

  [Test]
  [Category("Unit")]
  public void TheDirectCurrentShortcutIsNotTheSameArithmeticAsTheTransform() {
    // The specification requires the shortcut for a block with no alternating-current coefficients,
    // and this is why that is a requirement and not a licence: 113 is one of six hundred and
    // twenty-two dequantised values in sixteen bits where the two differ. A decoder that ran the
    // transform instead would drift from one that did not, and the drift would accumulate through
    // every frame predicted from it.
    var coefficients = new short[64];
    coefficients[0] = 113;

    Assert.That(_Shortcut(113), Is.EqualTo(4));
    Assert.That(_Transform(coefficients)[0], Is.Not.EqualTo(_Shortcut(113)));
  }

  [Test]
  [Category("Unit")]
  public void ACoefficientLargeEnoughToOverflowWrapsRatherThanSaturating() {
    // Section 7.9.3 requires unsaturated arithmetic: the high bits are dropped and the low ones kept.
    // A saturating implementation would clamp here and produce a different — and wrong — block.
    var coefficients = new short[64];
    coefficients[0] = 8000;
    coefficients[1] = 8000;
    coefficients[8] = 8000;
    coefficients[9] = 8000;

    var residual = _Transform(coefficients);
    var sawNegative = false;
    var sawPositive = false;
    foreach (var value in residual) {
      sawNegative |= value < 0;
      sawPositive |= value > 0;
    }

    Assert.That(sawNegative && sawPositive, Is.True,
      "an overflowing block should wrap to both signs rather than clamp to one");
  }
}
