using System;
using FileFormat.Jpeg2000;
using FileFormat.Jpeg2000.Codec;

namespace FileFormat.Jpeg2000.Tests;

/// <summary>The 5/3 and 9/7 lifting steps of ITU-T T.800 Annex F.</summary>
/// <remarks>
/// The signals here are interleaved: sample <c>j</c> of the array is at reference-grid coordinate
/// <c>i0 + j</c>, and the parity argument is the low bit of <c>i0</c>. That is what tells the filter
/// which half of the line is low-pass, so a line that begins at an odd coordinate is not the same
/// problem as one that begins at an even coordinate.
/// </remarks>
[TestFixture]
public sealed class Jp2WaveletTests {

  private static void _AssertReversible(int[] signal, int parity) {
    var length = signal.Length;
    var lowCount = parity == 0 ? (length + 1) / 2 : length / 2;
    var original = (int[])signal.Clone();

    Jp2Wavelet.Forward53(signal, lowCount, length - lowCount, parity);
    Jp2Wavelet.Inverse53(signal, lowCount, length - lowCount, parity);

    Assert.That(signal, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void Reversible53_EvenLength_RoundTrips()
    => _AssertReversible([10, 20, 30, 40, 50, 60, 70, 80], 0);

  [Test]
  [Category("Unit")]
  public void Reversible53_OddLength_RoundTrips()
    => _AssertReversible([5, 15, 25, 35, 45], 0);

  [Test]
  [Category("Unit")]
  public void Reversible53_TwoSamples_RoundTrips()
    => _AssertReversible([100, 200], 0);

  [Test]
  [Category("Unit")]
  public void Reversible53_NegativeValues_RoundTrip()
    => _AssertReversible([-10, 20, -30, 40, -50, 60], 0);

  [Test]
  [Category("Unit")]
  public void Reversible53_OddOrigin_RoundTrips() {
    _AssertReversible([7, 3, 9, 1, 8, 2, 6], 1);
    _AssertReversible([7, 3, 9, 1], 1);
  }

  [Test]
  [Category("Unit")]
  public void Reversible53_SingleSample_RoundTrips() {
    _AssertReversible([42], 0);

    // A lone sample at an odd coordinate is a high-pass one, and F.3.7 halves it.
    var odd = new[] { 42 };
    Jp2Wavelet.Inverse53(odd, 0, 1, 1);
    Assert.That(odd[0], Is.EqualTo(21));
  }

  [Test]
  [Category("Unit")]
  public void Reversible53_ConstantSignal_HasNoDetail() {
    var signal = new[] { 42, 42, 42, 42, 42, 42 };
    Jp2Wavelet.Forward53(signal, 3, 3, 0);

    Assert.Multiple(() => {
      for (var i = 0; i < 3; ++i)
        Assert.That(signal[2 * i + 1], Is.Zero, $"detail coefficient {i}");
    });
  }

  [Test]
  [Category("Unit")]
  public void Irreversible97_RoundTripsWithinFloatingPointError() {
    var signal = new float[] { 12, 45, 9, 88, 3, 61, 77, 20 };
    var original = (float[])signal.Clone();

    Jp2Wavelet.Forward97(signal, 4, 4, 0);
    Jp2Wavelet.Inverse97(signal, 4, 4, 0);

    Assert.Multiple(() => {
      for (var i = 0; i < signal.Length; ++i)
        Assert.That(signal[i], Is.EqualTo(original[i]).Within(0.01f), $"sample {i}");
    });
  }

  [TestCase(1, 8, 8)]
  [TestCase(2, 7, 5)]
  [TestCase(3, 16, 16)]
  [TestCase(5, 65, 49)]
  [Category("Unit")]
  public void TheTwoDimensionalTransformRoundTripsAtEveryDepth(int levels, int width, int height) {
    var component = _BuildComponent(levels, width, height);
    var random = new Random(4711);
    var original = new int[width * height];
    for (var i = 0; i < original.Length; ++i)
      original[i] = random.Next(-128, 128);

    Array.Copy(original, component.Samples, original.Length);
    Jp2Wavelet.ForwardTransform(component);
    Jp2Wavelet.InverseTransform(component);

    Assert.That(component.Samples, Is.EqualTo(original));
  }

  private static Jp2TileComponent _BuildComponent(int levels, int width, int height) {
    var style = new Jp2CodingStyle {
      DecompositionLevels = levels,
      CodeBlockWidthExp = 6,
      CodeBlockHeightExp = 6,
      Transform = 1,
      QuantizationStyle = 0,
      GuardBits = 2,
      QuantExponents = new int[3 * levels + 1],
      QuantMantissas = new int[3 * levels + 1],
    };
    style.UseDefaultPrecincts();

    var image = new Jp2Image {
      X1 = width,
      Y1 = height,
      TileWidth = width,
      TileHeight = height,
      Components = [new()],
    };

    var tile = Jp2StructureBuilder.Build(image, 0, [style], 1, 0, false, false, false, allocateCoefficients: true);
    return tile.Components[0];
  }
}
