using System;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// The curves the format defines for each transform shape.
/// </summary>
/// <remarks>
/// Every shape's weights come from a handful of numbers describing how the
/// quantisation step grows with distance from the corner of the block. Those
/// numbers are the format's, not a decoder's to choose: a curve that is roughly
/// the right shape reconstructs a picture that is roughly right everywhere, and
/// nothing about it looks wrong enough to notice. Several of these were
/// plausible round numbers rather than the stated ones, which is what a
/// spread-out error across a whole transform looks like.
///
/// <para>The first number of each curve is the one that sets its overall scale,
/// so it is the one worth pinning; the rest are checked by count, because a
/// curve of the wrong length is interpolated over the wrong range.</para>
/// </remarks>
[TestFixture]
internal sealed class JxlQuantBandTests {

  /// <param name="strategy">The shape.</param>
  /// <param name="bands">How many numbers its curve is described by.</param>
  /// <param name="firstX">Where the X plane's curve starts.</param>
  /// <param name="firstY">Where the Y plane's does.</param>
  /// <param name="firstB">And the B plane's.</param>
  [TestCase(JxlAcStrategyType.Dct8x8, 6, 3150.0f, 560.0f, 512.0f)]
  [TestCase(JxlAcStrategyType.Dct16x16, 7, 8996.8726f, 3191.4836f, 1157.5041f)]
  [TestCase(JxlAcStrategyType.Dct32x32, 8, 15718.408f, 7305.7637f, 3803.5317f)]
  [TestCase(JxlAcStrategyType.Dct16x8, 7, 7240.7734f, 1448.1547f, 506.85414f)]
  [TestCase(JxlAcStrategyType.Dct8x32, 8, 16283.249f, 5089.1575f, 3397.776f)]
  [TestCase(JxlAcStrategyType.Dct16x32, 8, 13844.971f, 4798.964f, 1807.2369f)]
  [TestCase(JxlAcStrategyType.Dct4x8, 4, 2198.0505f, 764.36554f, 527.10757f)]
  [TestCase(JxlAcStrategyType.Dct4x4, 4, 2200.0f, 392.0f, 112.0f)]
  [TestCase(JxlAcStrategyType.Dct64x64, 8, 0.9f * 26629.074f, 0.9f * 9311.324f, 0.9f * 4992.2485f)]
  public void AShapesCurveIsTheOneTheFormatStates(
    JxlAcStrategyType strategy, int bands, float firstX, float firstY, float firstB
  ) {
    var stated = JxlVarDctQuant.DistanceBandsForStrategy(strategy);
    Assert.That(stated, Is.Not.Null, $"{strategy} has a curve of its own");

    var expected = new[] { firstX, firstY, firstB };
    Assert.Multiple(() => {
      for (var c = 0; c < 3; ++c) {
        Assert.That(stated![c], Has.Length.EqualTo(bands), $"channel {c} band count");
        Assert.That(stated[c][0], Is.EqualTo(expected[c]).Within(expected[c] * 1e-6f), $"channel {c} start");
      }
    });
  }

  /// <summary>A shape and its transpose are one curve between them, as the
  /// format gives them one table.</summary>
  [TestCase(JxlAcStrategyType.Dct16x8, JxlAcStrategyType.Dct8x16)]
  [TestCase(JxlAcStrategyType.Dct32x8, JxlAcStrategyType.Dct8x32)]
  [TestCase(JxlAcStrategyType.Dct32x16, JxlAcStrategyType.Dct16x32)]
  [TestCase(JxlAcStrategyType.Dct4x8, JxlAcStrategyType.Dct8x4)]
  [TestCase(JxlAcStrategyType.Dct64x32, JxlAcStrategyType.Dct32x64)]
  public void AShapeAndItsTransposeShareOneCurve(JxlAcStrategyType one, JxlAcStrategyType other) {
    Assert.That(JxlVarDctQuant.DistanceBandsForStrategy(one),
      Is.SameAs(JxlVarDctQuant.DistanceBandsForStrategy(other)));
  }

  /// <summary>Every curve starts somewhere positive; a step of zero or less has
  /// no meaning and the construction refuses it.</summary>
  [TestCaseSource(nameof(_EveryCurvedShape))]
  public void EveryCurveStartsSomewhereReal(JxlAcStrategyType strategy) {
    var stated = JxlVarDctQuant.DistanceBandsForStrategy(strategy)!;

    for (var c = 0; c < 3; ++c)
      Assert.That(stated[c][0], Is.GreaterThan(0.0f), $"channel {c}");
  }

  private static readonly JxlAcStrategyType[] _EveryCurvedShape = [
    JxlAcStrategyType.Dct8x8, JxlAcStrategyType.Dct16x16, JxlAcStrategyType.Dct32x32,
    JxlAcStrategyType.Dct16x8, JxlAcStrategyType.Dct8x16, JxlAcStrategyType.Dct32x8,
    JxlAcStrategyType.Dct8x32, JxlAcStrategyType.Dct32x16, JxlAcStrategyType.Dct16x32,
    JxlAcStrategyType.Dct4x4, JxlAcStrategyType.Dct4x8, JxlAcStrategyType.Dct8x4,
    JxlAcStrategyType.Dct64x64, JxlAcStrategyType.Dct64x32, JxlAcStrategyType.Dct32x64,
  ];
}
