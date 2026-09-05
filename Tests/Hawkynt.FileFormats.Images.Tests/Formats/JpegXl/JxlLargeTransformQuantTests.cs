using System;
using System.IO;
using FileFormat.JpegXl;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// The weights a large transform's coefficients are dequantised with.
/// </summary>
/// <remarks>
/// Every transform shape has a curve of its own. The 64x64 and 64x32 shapes had
/// none here, so their coefficients were dequantised with the 8x8 curve
/// stretched over the block — a different curve entirely, and one that a
/// picture small enough to be a single transform is coded by from end to end.
/// </remarks>
[TestFixture]
internal sealed class JxlLargeTransformQuantTests {

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  [TestCase(JxlAcStrategyType.Dct64x64)]
  [TestCase(JxlAcStrategyType.Dct64x32)]
  [TestCase(JxlAcStrategyType.Dct32x64)]
  public void ALargeTransformHasWeightsOfItsOwn(JxlAcStrategyType strategy) {
    var set = JxlVarDctQuant.DefaultsForStrategy(strategy);
    var (width, height) = JxlVarDctIdct.BlockSize(strategy);

    Assert.That(set, Is.Not.Null, "without a table of its own the 8x8 one gets stretched over the block");
    for (var channel = 0; channel < 3; ++channel) {
      // Laid out the way the block it is applied to is, not the way the shape
      // is named.
      Assert.That(set!.Tables[channel].Width, Is.EqualTo(width), $"channel {channel} is as wide as the block");
      Assert.That(set.Tables[channel].Height, Is.EqualTo(height), $"channel {channel} is as tall as it");
    }
  }

  /// <summary>The curve is the shape's own, not the 8x8 one under another name.</summary>
  [Test]
  public void TheLargeTransformCurveIsNotTheSmallOne() {
    var large = JxlVarDctQuant.DefaultsForStrategy(JxlAcStrategyType.Dct64x64)!;
    var small = JxlVarDctQuant.DefaultsForStrategy(JxlAcStrategyType.Dct8x8)!;

    Assert.That(Math.Abs(large.Tables[0].Weights[0] - small.Tables[0].Weights[0]), Is.GreaterThan(1e-9f),
      $"the 64x64 curve starts at {large.Tables[0].Weights[0]} and the 8x8 one at {small.Tables[0].Weights[0]}");
  }

  /// <summary>
  /// A 64x64 gradient cjxl 0.12.0 wrote, which is one transform covering the
  /// whole picture, beside `djxl`'s own decode of it. With the 8x8 curve
  /// stretched over the block this differed from libjxl in 116 of its 4,096
  /// pixels and by as much as 24 levels; it is now within one level everywhere.
  /// </summary>
  [Test]
  public void APictureThatIsOneLargeTransformMatchesLibjxlToWithinALevel() {
    var decoded = JpegXlReader.TryReadSpecRgb24(
      _Fixture("cjxl_dct64_gradient.jxl"), out var width, out var height, out var rgb);
    Assert.That(decoded, Is.True);

    var (refWidth, refHeight, expected) = _ReadPpm(_Fixture("cjxl_dct64_gradient.ppm"));
    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(refWidth));
      Assert.That(height, Is.EqualTo(refHeight));
      Assert.That(rgb, Is.Not.Null.And.Length.EqualTo(expected.Length));
    });

    var worst = 0;
    var worstAt = -1;
    for (var i = 0; i < expected.Length; ++i) {
      var delta = Math.Abs(rgb![i] - expected[i]);
      if (delta <= worst)
        continue;

      worst = delta;
      worstAt = i;
    }

    Assert.That(worst, Is.LessThanOrEqualTo(1),
      $"sample {worstAt} is {(worstAt < 0 ? 0 : rgb![worstAt])} where libjxl has {(worstAt < 0 ? 0 : expected[worstAt])}");
  }

  private static (int Width, int Height, byte[] Pixels) _ReadPpm(byte[] ppm) {
    var at = 0;
    string Token() {
      while (at < ppm.Length && char.IsWhiteSpace((char)ppm[at]))
        ++at;
      var start = at;
      while (at < ppm.Length && !char.IsWhiteSpace((char)ppm[at]))
        ++at;
      return System.Text.Encoding.ASCII.GetString(ppm, start, at - start);
    }

    Assert.That(Token(), Is.EqualTo("P6"));
    var width = int.Parse(Token());
    var height = int.Parse(Token());
    Assert.That(Token(), Is.EqualTo("255"));
    ++at;

    var pixels = new byte[width * height * 3];
    Array.Copy(ppm, at, pixels, 0, pixels.Length);
    return (width, height, pixels);
  }
}
