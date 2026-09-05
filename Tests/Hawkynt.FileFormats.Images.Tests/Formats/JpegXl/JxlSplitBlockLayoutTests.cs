using System;
using System.Linq;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// The shapes that divide an 8x8 block into halves, and which coefficients
/// belong to which half.
/// </summary>
/// <remarks>
/// A block is kept here the way the scan order fills it, which is the transpose
/// of the way the format writes it down. A transform over the whole block
/// cannot tell the two apart, because turning both the coefficients and the
/// result round changes nothing — which is why every whole-block shape decoded
/// correctly while this went unnoticed. The shapes that divide the block can
/// tell: they pick particular rows out of it by number.
///
/// <para>The two levels of a split block are written as their sum and their
/// difference, so setting only those two says exactly what each half should
/// come out at, and each half must come out flat at it. Reading the block the
/// wrong way round takes the difference from an ordinary frequency instead and
/// leaves both halves wrong.</para>
/// </remarks>
[TestFixture]
internal sealed class JxlSplitBlockLayoutTests {

  private const float _Level = 100.0f;
  private const float _Difference = 20.0f;

  private static float[] _TwoLevels() {
    var coefficients = new float[64];
    coefficients[0] = _Level;
    coefficients[1] = _Difference;
    return coefficients;
  }

  /// <summary>Two transforms side by side: the left four columns take the sum,
  /// the right four take the difference.</summary>
  [Test]
  public void SideBySideHalvesTakeTheSumAndTheDifference() {
    var pixels = new float[64];
    JxlVarDctIdct.InverseAcStrategy(JxlAcStrategyType.Dct8x4, _TwoLevels(), pixels);

    var left = new System.Collections.Generic.List<float>();
    var right = new System.Collections.Generic.List<float>();
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x)
      (x < 4 ? left : right).Add(pixels[y * 8 + x]);

    Assert.Multiple(() => {
      Assert.That(left.Min(), Is.EqualTo(_Level + _Difference).Within(1e-3f), "the left half is flat at the sum");
      Assert.That(left.Max(), Is.EqualTo(_Level + _Difference).Within(1e-3f));
      Assert.That(right.Min(), Is.EqualTo(_Level - _Difference).Within(1e-3f), "the right half is flat at the difference");
      Assert.That(right.Max(), Is.EqualTo(_Level - _Difference).Within(1e-3f));
    });
  }

  /// <summary>And two stacked: the top four rows take the sum, the bottom four
  /// the difference.</summary>
  [Test]
  public void StackedHalvesTakeTheSumAndTheDifference() {
    var pixels = new float[64];
    JxlVarDctIdct.InverseAcStrategy(JxlAcStrategyType.Dct4x8, _TwoLevels(), pixels);

    var top = new System.Collections.Generic.List<float>();
    var bottom = new System.Collections.Generic.List<float>();
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x)
      (y < 4 ? top : bottom).Add(pixels[y * 8 + x]);

    Assert.Multiple(() => {
      Assert.That(top.Min(), Is.EqualTo(_Level + _Difference).Within(1e-3f), "the top half is flat at the sum");
      Assert.That(top.Max(), Is.EqualTo(_Level + _Difference).Within(1e-3f));
      Assert.That(bottom.Min(), Is.EqualTo(_Level - _Difference).Within(1e-3f), "the bottom half is flat at the difference");
      Assert.That(bottom.Max(), Is.EqualTo(_Level - _Difference).Within(1e-3f));
    });
  }

  /// <summary>With no difference stated the two halves are the same, which is
  /// the flat block the whole family agrees on.</summary>
  /// <param name="strategy">One of the two shapes that split the block.</param>
  [TestCase(JxlAcStrategyType.Dct8x4)]
  [TestCase(JxlAcStrategyType.Dct4x8)]
  public void WithNoDifferenceTheHalvesAgree(JxlAcStrategyType strategy) {
    var coefficients = new float[64];
    coefficients[0] = _Level;
    var pixels = new float[64];

    JxlVarDctIdct.InverseAcStrategy(strategy, coefficients, pixels);

    Assert.Multiple(() => {
      Assert.That(pixels.Min(), Is.EqualTo(_Level).Within(1e-3f));
      Assert.That(pixels.Max(), Is.EqualTo(_Level).Within(1e-3f));
    });
  }
}
