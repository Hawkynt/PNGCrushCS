using System;
using System.Drawing;
using System.Linq;
using NUnit.Framework;

namespace GifOptimizer.Tests;

[TestFixture]
public sealed class PaletteReordererTests {
  private static readonly Color[] _TestPalette = [
    Color.FromArgb(255, 255, 0, 0),
    Color.FromArgb(255, 0, 255, 0),
    Color.FromArgb(255, 0, 0, 255),
    Color.FromArgb(255, 255, 255, 255)
  ];

  private static readonly byte[] _TestPixels = [0, 1, 2, 3, 0, 0, 1, 1, 2, 2, 3, 3, 0, 1, 2, 3];

  [Test]
  [Category("Unit")]
  public void Original_ReturnsIdentityRemap() {
    var (newPalette, remap) = PaletteReorderer.Reorder(_TestPalette, _TestPixels, PaletteReorderStrategy.Original);

    Assert.That(newPalette, Is.EqualTo(_TestPalette));
    for (var i = 0; i < remap.Length; ++i)
      Assert.That(remap[i], Is.EqualTo(i));
  }

  [TestCase(PaletteReorderStrategy.FrequencySorted)]
  [TestCase(PaletteReorderStrategy.LuminanceSorted)]
  [TestCase(PaletteReorderStrategy.SpatialLocality)]
  [TestCase(PaletteReorderStrategy.LzwRunAware)]
  [TestCase(PaletteReorderStrategy.HilbertCurve)]
  [TestCase(PaletteReorderStrategy.CompressionOptimized)]
  [Category("Unit")]
  public void Reorder_ProducesValidPermutation(PaletteReorderStrategy strategy) {
    var (newPalette, remap) = PaletteReorderer.Reorder(_TestPalette, _TestPixels, strategy);

    Assert.That(newPalette.Length, Is.EqualTo(_TestPalette.Length));
    Assert.That(remap.Length, Is.EqualTo(_TestPalette.Length));

    // Each original color should appear exactly once in new palette
    var originalArgbs = _TestPalette.Select(c => c.ToArgb()).OrderBy(x => x).ToArray();
    var newArgbs = newPalette.Select(c => c.ToArgb()).OrderBy(x => x).ToArray();
    Assert.That(newArgbs, Is.EqualTo(originalArgbs));

    // Remap values should be a permutation of 0..N-1
    var remapSorted = remap.OrderBy(x => x).ToArray();
    for (var i = 0; i < remapSorted.Length; ++i)
      Assert.That(remapSorted[i], Is.EqualTo(i));
  }

  [TestCase(PaletteReorderStrategy.FrequencySorted)]
  [TestCase(PaletteReorderStrategy.LuminanceSorted)]
  [TestCase(PaletteReorderStrategy.SpatialLocality)]
  [TestCase(PaletteReorderStrategy.LzwRunAware)]
  [TestCase(PaletteReorderStrategy.HilbertCurve)]
  [TestCase(PaletteReorderStrategy.CompressionOptimized)]
  [Category("Unit")]
  public void RemapThenInverse_IsIdentity(PaletteReorderStrategy strategy) {
    var (newPalette, remap) = PaletteReorderer.Reorder(_TestPalette, _TestPixels, strategy);
    var remapped = PaletteReorderer.ApplyRemap(_TestPixels, remap);

    // For each pixel: newPalette[remap[originalIndex]].ToArgb() == _TestPalette[originalIndex].ToArgb()
    for (var i = 0; i < _TestPixels.Length; ++i) {
      var originalColor = _TestPalette[_TestPixels[i]].ToArgb();
      var remappedColor = newPalette[remapped[i]].ToArgb();
      Assert.That(remappedColor, Is.EqualTo(originalColor), $"Color mismatch at pixel {i}");
    }
  }

  [Test]
  [Category("Unit")]
  public void FrequencySort_MostFrequentFirst() {
    var pixels = new byte[] { 2, 2, 2, 2, 0, 0, 1, 3 };
    var (newPalette, _) = PaletteReorderer.Reorder(_TestPalette, pixels, PaletteReorderStrategy.FrequencySorted);

    // Index 2 (Blue) is most frequent (4 times), should be first
    Assert.That(newPalette[0].ToArgb(), Is.EqualTo(Color.Blue.ToArgb()));
  }

  [Test]
  [Category("Unit")]
  public void CompressionOptimized_ReturnsSmallestCompressor() {
    // CompressionOptimized should produce output no larger than frequency-sorted
    var pixels = new byte[256];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 4);

    var (freqPalette, freqRemap) =
      PaletteReorderer.Reorder(_TestPalette, pixels, PaletteReorderStrategy.FrequencySorted);
    var freqRemapped = PaletteReorderer.ApplyRemap(pixels, freqRemap);
    var freqCompressed = LzwCompressor.Compress(freqRemapped, 8);

    var (optPalette, optRemap) =
      PaletteReorderer.Reorder(_TestPalette, pixels, PaletteReorderStrategy.CompressionOptimized);
    var optRemapped = PaletteReorderer.ApplyRemap(pixels, optRemap);
    var optCompressed = LzwCompressor.Compress(optRemapped, 8);

    Assert.That(optCompressed.Length, Is.LessThanOrEqualTo(freqCompressed.Length),
      $"CompressionOptimized={optCompressed.Length} vs FrequencySorted={freqCompressed.Length}");
  }

  [Test]
  [Category("Unit")]
  public void ApplyRemap_TransformsAllPixels() {
    var remap = new byte[] { 3, 2, 1, 0 };
    var pixels = new byte[] { 0, 1, 2, 3 };
    var result = PaletteReorderer.ApplyRemap(pixels, remap);

    Assert.That(result, Is.EqualTo(new byte[] { 3, 2, 1, 0 }));
  }
}
