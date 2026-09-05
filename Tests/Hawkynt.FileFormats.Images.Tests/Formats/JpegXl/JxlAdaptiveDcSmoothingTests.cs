using System;
using System.Linq;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Taking the steps back out of a frame's low frequencies.
/// </summary>
/// <remarks>
/// One value per block, quantised, leaves visible steps between neighbouring
/// blocks in anything smooth, and the encoder writes the frame expecting the
/// decoder to take them out again. What makes it safe is that it measures how
/// far a value is from its neighbours in whole quantisation steps: a difference
/// the quantiser could not have introduced is a real edge and is left alone.
/// </remarks>
[TestFixture]
internal sealed class JxlAdaptiveDcSmoothingTests {

  private const int _Width = 5;
  private const int _Height = 5;
  private static readonly float[] _Steps = [1.0f, 1.0f, 1.0f];

  private static float[] _Planes(Func<int, int, float> sample) {
    var count = _Width * _Height;
    var planes = new float[3 * count];
    for (var c = 0; c < 3; ++c)
    for (var y = 0; y < _Height; ++y)
    for (var x = 0; x < _Width; ++x)
      planes[c * count + y * _Width + x] = sample(x, y);

    return planes;
  }

  private static float _Middle(float[] planes, int channel = 0)
    => planes[channel * _Width * _Height + 2 * _Width + 2];

  [Test]
  public void OneValueEverywhereIsLeftAlone() {
    var planes = _Planes((_, _) => 7.5f);

    JxlAdaptiveDcSmoothing.Apply(planes, _Width, _Height, _Steps);

    Assert.That(planes.Distinct().Count(), Is.EqualTo(1));
  }

  /// <summary>A value a small fraction of a step away from its neighbours is
  /// quantisation noise, and is pulled in.</summary>
  [Test]
  public void ASmallDisagreementIsSmoothedAway() {
    var planes = _Planes((x, y) => x == 2 && y == 2 ? 0.1f : 0.0f);

    JxlAdaptiveDcSmoothing.Apply(planes, _Width, _Height, _Steps);

    Assert.That(_Middle(planes), Is.LessThan(0.1f).And.GreaterThanOrEqualTo(0.0f),
      "the odd one out was pulled towards its neighbours");
  }

  /// <summary>A value further than the quantiser could have moved it is a real
  /// edge and is kept exactly.</summary>
  [Test]
  public void ARealEdgeIsKept() {
    var planes = _Planes((x, y) => x == 2 && y == 2 ? 100.0f : 0.0f);

    JxlAdaptiveDcSmoothing.Apply(planes, _Width, _Height, _Steps);

    Assert.That(_Middle(planes), Is.EqualTo(100.0f).Within(1e-6f));
  }

  /// <summary>The three planes decide together, so a colour edge is not
  /// smoothed out of one of them while another keeps it.</summary>
  [Test]
  public void AnEdgeInOnePlaneHoldsTheOthersBack() {
    var count = _Width * _Height;
    var planes = _Planes((x, y) => x == 2 && y == 2 ? 0.1f : 0.0f);
    // The third plane has a real edge in the same place.
    planes[2 * count + 2 * _Width + 2] = 100.0f;

    JxlAdaptiveDcSmoothing.Apply(planes, _Width, _Height, _Steps);

    Assert.Multiple(() => {
      Assert.That(_Middle(planes, 2), Is.EqualTo(100.0f).Within(1e-6f), "the plane with the edge");
      Assert.That(_Middle(planes, 0), Is.EqualTo(0.1f).Within(1e-6f), "and the ones that agreed with it");
    });
  }

  /// <summary>The outermost row and column have no neighbourhood and keep what
  /// they had.</summary>
  [Test]
  public void TheEdgeOfThePictureIsKept() {
    var random = new Random(31);
    var planes = _Planes((_, _) => (float)random.NextDouble());
    var before = planes.ToArray();

    JxlAdaptiveDcSmoothing.Apply(planes, _Width, _Height, _Steps);

    for (var c = 0; c < 3; ++c)
    for (var y = 0; y < _Height; ++y)
    for (var x = 0; x < _Width; ++x) {
      if (x != 0 && x != _Width - 1 && y != 0 && y != _Height - 1)
        continue;

      var at = c * _Width * _Height + y * _Width + x;
      Assert.That(planes[at], Is.EqualTo(before[at]), $"plane {c} at ({x},{y})");
    }
  }

  /// <summary>A picture with no interior at all is left as it is.</summary>
  /// <param name="width">Blocks across.</param>
  /// <param name="height">Blocks down.</param>
  [TestCase(2, 5)]
  [TestCase(5, 2)]
  [TestCase(1, 1)]
  public void APictureWithNoInteriorIsLeftAlone(int width, int height) {
    var random = new Random(width * 100 + height);
    var planes = new float[3 * width * height];
    for (var i = 0; i < planes.Length; ++i)
      planes[i] = (float)random.NextDouble();
    var before = planes.ToArray();

    JxlAdaptiveDcSmoothing.Apply(planes, width, height, _Steps);

    Assert.That(planes, Is.EqualTo(before));
  }
}
