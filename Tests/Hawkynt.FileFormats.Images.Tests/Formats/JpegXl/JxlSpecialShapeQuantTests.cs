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

  /// <summary>
  /// The AFV shape's block is three curves laid together: its odd rows are a
  /// 4x8, its even rows and odd columns a 4x4, and what is left is its own —
  /// five entries stated outright at the corner, the rest read off four bands.
  /// These are the five stated ones, which are the part that is unique to it.
  /// </summary>
  /// <param name="channel">Which of the three the weights are for.</param>
  /// <param name="corner">The shape's first five weights, in the order it
  /// states them.</param>
  [TestCase(0, new[] { 3072.0f, 3072.0f, 256.0f, 256.0f, 256.0f })]
  [TestCase(1, new[] { 1024.0f, 1024.0f, 50.0f, 50.0f, 50.0f })]
  [TestCase(2, new[] { 384.0f, 384.0f, 12.0f, 12.0f, 12.0f })]
  public void TheAfvShapeStatesFiveEntriesAtItsCorner(int channel, float[] corner) {
    var set = JxlVarDctQuant.DefaultsForStrategy(JxlAcStrategyType.Afv0);
    Assert.That(set, Is.Not.Null);

    var weights = set!.Tables[channel].Weights;
    Assert.Multiple(() => {
      Assert.That(weights, Has.Length.EqualTo(64));
      Assert.That(weights[1 * 8 + 0], Is.EqualTo(1.0f / corner[0]).Within(1e-9), "below the corner");
      Assert.That(weights[0 * 8 + 1], Is.EqualTo(1.0f / corner[1]).Within(1e-9), "beside it");
      Assert.That(weights[2 * 8 + 0], Is.EqualTo(1.0f / corner[2]).Within(1e-9), "two below");
      Assert.That(weights[0 * 8 + 2], Is.EqualTo(1.0f / corner[3]).Within(1e-9), "two across");
      Assert.That(weights[2 * 8 + 2], Is.EqualTo(1.0f / corner[4]).Within(1e-9), "the corner itself");
    });
  }

  /// <summary>The four AFV shapes are one shape turned about, and the format
  /// gives them one table between them.</summary>
  [Test]
  public void TheFourAfvShapesShareOneTable() {
    var zero = JxlVarDctQuant.DefaultsForStrategy(JxlAcStrategyType.Afv0)!;

    foreach (var other in new[] { JxlAcStrategyType.Afv1, JxlAcStrategyType.Afv2, JxlAcStrategyType.Afv3 }) {
      var set = JxlVarDctQuant.DefaultsForStrategy(other);
      Assert.That(set, Is.Not.Null, $"{other} has a table");
      for (var c = 0; c < 3; ++c)
        Assert.That(set!.Tables[c].Weights, Is.EqualTo(zero.Tables[c].Weights), $"{other} channel {c}");
    }
  }

  /// <summary>Every entry is a real weight — nothing was left at its zero fill,
  /// which is what a gap in the layout would show up as.</summary>
  [Test]
  public void EveryEntryOfTheAfvTableWasWritten() {
    var set = JxlVarDctQuant.DefaultsForStrategy(JxlAcStrategyType.Afv0)!;

    for (var c = 0; c < 3; ++c) {
      var weights = set.Tables[c].Weights;
      for (var i = 0; i < 64; ++i)
        Assert.That(weights[i], Is.GreaterThan(0.0f).And.LessThan(float.PositiveInfinity), $"channel {c} entry {i}");
    }
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
