using System;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// What a quantised coefficient is worth before its weight is applied.
/// </summary>
/// <remarks>
/// Quantising rounds towards zero, so a coefficient that came back as one step
/// was more likely a little under one than a little over. The format says what
/// to assume rather than leaving the decoder to take the number at face value:
/// a lone step is worth slightly less than a whole one, by an amount stated per
/// plane, and anything larger is pulled towards zero by a fixed amount divided
/// by itself, which matters less the larger it is.
///
/// <para>Taking them at face value leaves a small error on every coefficient a
/// picture has, and a picture with detail has a great many at exactly one step
/// — which is why it showed up as a fraction of a level everywhere rather than
/// as anything obviously broken.</para>
/// </remarks>
[TestFixture]
internal sealed class JxlQuantBiasTests {

  /// <summary>Nothing is still nothing.</summary>
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  public void ZeroStaysZero(int channel) {
    Assert.That(JxlVarDctQuant.AdjustQuantBias(0, channel), Is.Zero);
  }

  /// <param name="channel">Which plane the coefficient belongs to.</param>
  /// <param name="worth">What one step is worth in it.</param>
  [TestCase(0, 1.0f - 0.05465007330715401f)]
  [TestCase(1, 1.0f - 0.07005449891748593f)]
  [TestCase(2, 1.0f - 0.049935103337343655f)]
  public void OneStepIsWorthSlightlyLessThanOne(int channel, float worth) {
    Assert.Multiple(() => {
      Assert.That(JxlVarDctQuant.AdjustQuantBias(1, channel), Is.EqualTo(worth).Within(1e-7f));
      Assert.That(JxlVarDctQuant.AdjustQuantBias(-1, channel), Is.EqualTo(-worth).Within(1e-7f));
      Assert.That(worth, Is.LessThan(1.0f).And.GreaterThan(0.9f), "slightly less, not much less");
    });
  }

  /// <param name="quantised">A coefficient of more than one step.</param>
  [TestCase(2)]
  [TestCase(-2)]
  [TestCase(7)]
  [TestCase(-100)]
  public void MoreThanOneStepIsPulledTowardsZero(int quantised) {
    var adjusted = JxlVarDctQuant.AdjustQuantBias(quantised, channel: 1);

    Assert.Multiple(() => {
      Assert.That(adjusted, Is.EqualTo(quantised - 0.145f / quantised).Within(1e-6f));
      Assert.That(Math.Abs(adjusted), Is.LessThan(Math.Abs(quantised)), "towards zero");
      Assert.That(Math.Sign(adjusted), Is.EqualTo(Math.Sign(quantised)), "not past it");
    });
  }

  /// <summary>The correction shrinks as the coefficient grows, so a large one
  /// is taken almost at face value.</summary>
  [Test]
  public void TheCorrectionFadesAsTheCoefficientGrows() {
    var small = Math.Abs(2 - JxlVarDctQuant.AdjustQuantBias(2, 1));
    var large = Math.Abs(100 - JxlVarDctQuant.AdjustQuantBias(100, 1));

    Assert.That(large, Is.LessThan(small / 10.0f));
  }

  /// <summary>The three planes disagree about what one step is worth, which is
  /// the whole reason it is stated per plane.</summary>
  [Test]
  public void ThePlanesDisagreeAboutOneStep() {
    var x = JxlVarDctQuant.AdjustQuantBias(1, 0);
    var y = JxlVarDctQuant.AdjustQuantBias(1, 1);
    var b = JxlVarDctQuant.AdjustQuantBias(1, 2);

    Assert.Multiple(() => {
      Assert.That(x, Is.Not.EqualTo(y).Within(1e-6f));
      Assert.That(y, Is.Not.EqualTo(b).Within(1e-6f));
    });
  }
}
