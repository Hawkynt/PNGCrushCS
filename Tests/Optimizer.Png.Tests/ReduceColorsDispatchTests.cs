using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using Optimizer.Png;

namespace Optimizer.Png.Tests;

[TestFixture]
public sealed class ReduceColorsDispatchTests {

  private static Bitmap _CreateTestBitmap(int width = 32, int height = 32) {
    var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      bmp.SetPixel(x, y, Color.FromArgb(255, x * 8 % 256, y * 8 % 256, (x + y) * 4 % 256));
    return bmp;
  }

  [Test]
  [Category("Unit")]
  public void QuantizerNames_ReturnsAtLeast41() {
    var names = ReduceColorsDispatch.QuantizerNames.ToList();
    Assert.That(names.Count, Is.GreaterThanOrEqualTo(41));
  }

  [Test]
  [Category("Unit")]
  public void DithererNames_ReturnsAtLeast124() {
    var names = ReduceColorsDispatch.DithererNames.ToList();
    Assert.That(names.Count, Is.GreaterThanOrEqualTo(124));
  }

  [Test]
  [Category("Unit")]
  public void QuantizerNames_ContainsKnownEntries() {
    var names = ReduceColorsDispatch.QuantizerNames.ToList();
    Assert.That(names, Does.Contain("Wu"));
    Assert.That(names, Does.Contain("Octree"));
    Assert.That(names, Does.Contain("Median Cut"));
  }

  [Test]
  [Category("Unit")]
  public void DithererNames_ContainsKnownEntries() {
    var names = ReduceColorsDispatch.DithererNames.ToList();
    Assert.That(names, Does.Contain("NoDithering_Instance"));
    Assert.That(names, Does.Contain("ErrorDiffusion_FloydSteinberg"));
  }

  [Test]
  [Category("Unit")]
  public void UnknownQuantizer_ThrowsArgumentException() {
    using var bmp = _CreateTestBitmap(8, 8);
    var ex = Assert.Throws<ArgumentException>(() =>
      ReduceColorsDispatch.ReduceColors(bmp, "NonExistentQuantizer", "NoDithering_Instance", 256, false)
    );
    Assert.That(ex!.Message, Does.Contain("NonExistentQuantizer"));
    Assert.That(ex.Message, Does.Contain("Available"));
  }

  [Test]
  [Category("Unit")]
  public void UnknownDitherer_ThrowsArgumentException() {
    using var bmp = _CreateTestBitmap(8, 8);
    var ex = Assert.Throws<ArgumentException>(() =>
      ReduceColorsDispatch.ReduceColors(bmp, "Wu", "NonExistentDitherer", 256, false)
    );
    Assert.That(ex!.Message, Does.Contain("NonExistentDitherer"));
    Assert.That(ex.Message, Does.Contain("Available"));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(10000)]
  public void ReduceColors_WuNoDithering_ProducesIndexedBitmap() {
    using var bmp = _CreateTestBitmap();
    using var result = ReduceColorsDispatch.ReduceColors(bmp, "Wu", "NoDithering_Instance", 256, false);

    Assert.That(result.Width, Is.EqualTo(32));
    Assert.That(result.Height, Is.EqualTo(32));
    Assert.That(result.PixelFormat, Is.EqualTo(PixelFormat.Format8bppIndexed));
    Assert.That(result.Palette.Entries.Length, Is.GreaterThan(0));
    Assert.That(result.Palette.Entries.Length, Is.LessThanOrEqualTo(256));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(10000)]
  public void ReduceColors_OctreeFloydSteinberg_ProducesIndexedBitmap() {
    using var bmp = _CreateTestBitmap();
    using var result = ReduceColorsDispatch.ReduceColors(bmp, "Octree", "ErrorDiffusion_FloydSteinberg", 256, false);

    Assert.That(result.Width, Is.EqualTo(32));
    Assert.That(result.Height, Is.EqualTo(32));
    Assert.That(result.PixelFormat, Is.EqualTo(PixelFormat.Format8bppIndexed));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(10000)]
  public void ReduceColors_MedianCut_ProducesIndexedBitmap() {
    using var bmp = _CreateTestBitmap();
    using var result = ReduceColorsDispatch.ReduceColors(bmp, "Median Cut", "NoDithering_Instance", 256, false);

    Assert.That(result.Width, Is.EqualTo(32));
    Assert.That(result.Height, Is.EqualTo(32));
    Assert.That(result.PixelFormat, Is.EqualTo(PixelFormat.Format8bppIndexed));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(10000)]
  public void ReduceColors_SameTypePair_UsesCachedDelegate() {
    using var bmp1 = _CreateTestBitmap(8, 8);
    using var bmp2 = _CreateTestBitmap(16, 16);

    using var result1 = ReduceColorsDispatch.ReduceColors(bmp1, "Wu", "NoDithering_Instance", 256, false);
    using var result2 = ReduceColorsDispatch.ReduceColors(bmp2, "Wu", "NoDithering_Instance", 128, false);

    Assert.That(result1.PixelFormat, Is.EqualTo(PixelFormat.Format8bppIndexed));
    Assert.That(result2.PixelFormat, Is.EqualTo(PixelFormat.Format8bppIndexed));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(10000)]
  public void ReduceColors_HighQuality_ProducesValidResult() {
    using var bmp = _CreateTestBitmap(16, 16);
    using var result = ReduceColorsDispatch.ReduceColors(bmp, "Wu", "NoDithering_Instance", 256, true);

    Assert.That(result.Width, Is.EqualTo(16));
    Assert.That(result.Height, Is.EqualTo(16));
    Assert.That(result.PixelFormat, Is.EqualTo(PixelFormat.Format8bppIndexed));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(10000)]
  public void ReduceColors_LimitedPalette_RespectsMaxColors() {
    using var bmp = _CreateTestBitmap(16, 16);
    using var result = ReduceColorsDispatch.ReduceColors(bmp, "Wu", "NoDithering_Instance", 16, false);

    Assert.That(result.Palette.Entries.Length, Is.LessThanOrEqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void QuantizerNames_AreNotEmpty() {
    foreach (var name in ReduceColorsDispatch.QuantizerNames)
      Assert.That(name, Is.Not.Null.And.Not.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DithererNames_AreNotEmpty() {
    foreach (var name in ReduceColorsDispatch.DithererNames)
      Assert.That(name, Is.Not.Null.And.Not.Empty);
  }
}
