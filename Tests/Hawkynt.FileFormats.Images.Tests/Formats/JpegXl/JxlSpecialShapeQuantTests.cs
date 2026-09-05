using System;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// The two shapes that state their weights outright instead of as a curve.
/// </summary>
/// <remarks>
/// Neither is a plain transform, so neither has a curve over distance to be
/// described by. The Hornuss shape carries one value over the whole block with
/// three corner entries of its own; the 2x2 shape is four nested squares. Both
/// were being dequantised with the 8x8 curve, which is a different set of
/// numbers entirely and which shows up as single badly wrong blocks rather than
/// as a picture that is a little off everywhere.
/// </remarks>
[TestFixture]
internal sealed class JxlSpecialShapeQuantTests {

  /// <summary>One value over the block, with the two neighbours of the corner
  /// and the corner itself stated separately.</summary>
  [Test]
  public void TheHornussShapeIsOneValueWithThreeCornerEntries() {
    var set = JxlVarDctQuant.DefaultsForStrategy(JxlAcStrategyType.Hornuss);
    Assert.That(set, Is.Not.Null);

    var weights = set!.Tables[0].Weights;
    Assert.Multiple(() => {
      Assert.That(weights, Has.Length.EqualTo(64));
      Assert.That(weights[0], Is.EqualTo(1.0f / 280.0f).Within(1e-9));
      Assert.That(weights[1], Is.EqualTo(1.0f / 3160.0f).Within(1e-9));
      Assert.That(weights[8], Is.EqualTo(1.0f / 3160.0f).Within(1e-9));
      Assert.That(weights[9], Is.EqualTo(1.0f / 3160.0f).Within(1e-9));
      // Everything else is the one value.
      for (var i = 2; i < 64; ++i) {
        if (i is 8 or 9)
          continue;
        Assert.That(weights[i], Is.EqualTo(weights[0]).Within(1e-9), $"entry {i}");
      }
    });
  }

  /// <summary>Four nested squares: the 2x2 corner, then out to 4x4, then to
  /// the whole 8x8.</summary>
  [Test]
  public void TheTwoByTwoShapeIsFourNestedSquares() {
    var set = JxlVarDctQuant.DefaultsForStrategy(JxlAcStrategyType.Dct2x2);
    Assert.That(set, Is.Not.Null);

    var weights = set!.Tables[2].Weights;
    float At(int x, int y) => weights[y * 8 + x];

    Assert.Multiple(() => {
      Assert.That(At(1, 0), Is.EqualTo(1.0f / 640.0f).Within(1e-9));
      Assert.That(At(0, 1), Is.EqualTo(1.0f / 640.0f).Within(1e-9));
      Assert.That(At(1, 1), Is.EqualTo(1.0f / 320.0f).Within(1e-9));
      // The band that reaches out to four across, on both sides of the corner.
      Assert.That(At(2, 0), Is.EqualTo(1.0f / 128.0f).Within(1e-9));
      Assert.That(At(0, 3), Is.EqualTo(1.0f / 128.0f).Within(1e-9));
      Assert.That(At(3, 3), Is.EqualTo(1.0f / 64.0f).Within(1e-9));
      // And out to eight.
      Assert.That(At(4, 0), Is.EqualTo(1.0f / 32.0f).Within(1e-9));
      Assert.That(At(0, 7), Is.EqualTo(1.0f / 32.0f).Within(1e-9));
      Assert.That(At(7, 7), Is.EqualTo(1.0f / 16.0f).Within(1e-9));
    });
  }

  /// <summary>Neither is the plain 8x8 curve under another name.</summary>
  [Test]
  public void NeitherIsTheEightByEightCurve() {
    var dct8 = JxlVarDctQuant.DefaultsForStrategy(JxlAcStrategyType.Dct8x8)!;
    var hornuss = JxlVarDctQuant.DefaultsForStrategy(JxlAcStrategyType.Hornuss)!;
    var dct2 = JxlVarDctQuant.DefaultsForStrategy(JxlAcStrategyType.Dct2x2)!;

    Assert.Multiple(() => {
      Assert.That(Math.Abs(hornuss.Tables[0].Weights[9] - dct8.Tables[0].Weights[9]), Is.GreaterThan(1e-9f));
      Assert.That(Math.Abs(dct2.Tables[0].Weights[9] - dct8.Tables[0].Weights[9]), Is.GreaterThan(1e-9f));
    });
  }
}
