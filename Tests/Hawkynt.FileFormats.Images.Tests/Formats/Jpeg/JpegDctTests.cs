using System;
using NUnit.Framework;

namespace FileFormat.Jpeg.Tests;

/// <summary>The forward transform is checked against the definition, not against our own inverse.</summary>
/// <remarks>
/// Its odd part had two of libjpeg's operand pairings swapped, which no round trip through this
/// project could see: the inverse transform is a different routine and was correct, so a decode of
/// our own encode still came back looking right. Against the defining sum the worst coefficient was
/// out by 365.
/// </remarks>
[TestFixture]
public sealed class JpegDctTests {

  [Test]
  [Category("Unit")]
  public void ForwardDctMatchesTheDefiningSum() {
    var random = new Random(1234);
    var worst = 0.0;

    for (var trial = 0; trial < 50; ++trial) {
      var samples = new int[64];
      for (var i = 0; i < 64; ++i)
        samples[i] = random.Next(0, 256) - 128;

      var expected = _DefiningDct(samples);
      var actual = (int[])samples.Clone();
      JpegDct.ForwardDct(actual);

      for (var i = 0; i < 64; ++i)
        worst = Math.Max(worst, Math.Abs(actual[i] / (double)JpegQuantizer.ForwardDctScale - expected[i]));
    }

    Assert.That(worst, Is.LessThan(2.0), $"worst coefficient error {worst:F3}");
  }

  /// <summary>The two-dimensional DCT-II as ITU-T T.81 A.3.3 states it.</summary>
  private static double[] _DefiningDct(int[] samples) {
    var result = new double[64];

    for (var u = 0; u < 8; ++u)
    for (var v = 0; v < 8; ++v) {
      var sum = 0.0;
      for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x)
        sum += samples[y * 8 + x]
               * Math.Cos((2 * x + 1) * v * Math.PI / 16)
               * Math.Cos((2 * y + 1) * u * Math.PI / 16);

      var cu = u == 0 ? 1 / Math.Sqrt(2) : 1;
      var cv = v == 0 ? 1 / Math.Sqrt(2) : 1;
      result[u * 8 + v] = 0.25 * cu * cv * sum;
    }

    return result;
  }
}
