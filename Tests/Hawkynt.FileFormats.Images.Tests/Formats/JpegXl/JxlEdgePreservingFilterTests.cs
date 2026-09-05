using System;
using System.Linq;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// The filter a VarDCT frame is finished with.
/// </summary>
/// <remarks>
/// It is a weighted average over a neighbourhood, so what it can and cannot do
/// is bounded whatever the weights come out as: it cannot invent a value that
/// was not already somewhere near, it cannot disturb a region that is all one
/// value, and it must leave alone the blocks the frame says to leave alone.
/// Those are the three things asserted here, because they hold for any picture
/// rather than for one that happens to be to hand.
/// </remarks>
[TestFixture]
internal sealed class JxlEdgePreservingFilterTests {

  private const int _Width = 24;
  private const int _Height = 24;
  private const int _BlocksWide = 3;
  private const int _BlocksHigh = 3;

  private static float[][] _Planes(Func<int, int, int, float> sample) {
    var planes = new float[3][];
    for (var c = 0; c < 3; ++c) {
      planes[c] = new float[_Width * _Height];
      for (var y = 0; y < _Height; ++y)
      for (var x = 0; x < _Width; ++x)
        planes[c][y * _Width + x] = sample(c, x, y);
    }

    return planes;
  }

  private static int[] _Filled(int value) => Enumerable.Repeat(value, _BlocksWide * _BlocksHigh).ToArray();

  /// <summary>A region that is all one value has nothing to smooth, and comes
  /// back as it went in.</summary>
  [Test]
  public void ARegionOfOneValueIsUnchanged() {
    var planes = _Planes((c, _, _) => 0.25f * (c + 1));

    JxlEdgePreservingFilter.Apply(
      planes, _Width, _Height, _Filled(8), _Filled(7), _BlocksWide, _BlocksHigh,
      invGlobalScale: 12.8225f, iterations: 1);

    for (var c = 0; c < 3; ++c)
      Assert.That(planes[c].Distinct().Count(), Is.EqualTo(1), $"plane {c} stayed one value");
  }

  /// <summary>A block the frame states no sharpness for is left alone
  /// entirely — that is the format's own way of turning the filter off where
  /// the encoder judged it would do harm.</summary>
  [Test]
  public void ABlockOfNoStatedSharpnessIsCopiedThrough() {
    var random = new Random(4242);
    var planes = _Planes((_, _, _) => (float)random.NextDouble());
    var before = planes.Select(p => p.ToArray()).ToArray();

    JxlEdgePreservingFilter.Apply(
      planes, _Width, _Height, _Filled(8), _Filled(0), _BlocksWide, _BlocksHigh,
      invGlobalScale: 12.8225f, iterations: 3);

    for (var c = 0; c < 3; ++c)
      Assert.That(planes[c], Is.EqualTo(before[c]), $"plane {c} was not touched");
  }

  /// <summary>Being an average, it cannot take a sample outside the range of
  /// what was already there.</summary>
  /// <param name="iterations">How many passes the frame asked for.</param>
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  public void ItNeverLeavesTheRangeItStartedIn(int iterations) {
    var random = new Random(iterations * 7919);
    var planes = _Planes((_, _, _) => (float)random.NextDouble());
    var low = planes.Select(p => p.Min()).ToArray();
    var high = planes.Select(p => p.Max()).ToArray();

    JxlEdgePreservingFilter.Apply(
      planes, _Width, _Height, _Filled(8), _Filled(7), _BlocksWide, _BlocksHigh,
      invGlobalScale: 12.8225f, iterations: iterations);

    for (var c = 0; c < 3; ++c) {
      Assert.That(planes[c].Min(), Is.GreaterThanOrEqualTo(low[c] - 1e-5f), $"plane {c} went below");
      Assert.That(planes[c].Max(), Is.LessThanOrEqualTo(high[c] + 1e-5f), $"plane {c} went above");
    }
  }

  /// <summary>Asking for no passes at all does nothing.</summary>
  [Test]
  public void NoPassesDoesNothing() {
    var random = new Random(11);
    var planes = _Planes((_, _, _) => (float)random.NextDouble());
    var before = planes.Select(p => p.ToArray()).ToArray();

    JxlEdgePreservingFilter.Apply(
      planes, _Width, _Height, _Filled(8), _Filled(7), _BlocksWide, _BlocksHigh,
      invGlobalScale: 12.8225f, iterations: 0);

    for (var c = 0; c < 3; ++c)
      Assert.That(planes[c], Is.EqualTo(before[c]));
  }
}
