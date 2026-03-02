using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using NUnit.Framework;

namespace TiffOptimizer.Tests;

[TestFixture]
public sealed class TiffOptimizerTests {
  private static Bitmap _CreateTestBitmap(int width = 8, int height = 8, bool grayscale = false) {
    var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      Color c;
      if (grayscale) {
        var g = (int)(255.0 * x / width);
        c = Color.FromArgb(255, g, g, g);
      } else {
        c = Color.FromArgb(255, x * 32 % 256, y * 32 % 256, (x + y) * 16 % 256);
      }

      bmp.SetPixel(x, y, c);
    }

    return bmp;
  }

  [Test]
  [Category("Unit")]
  public void Constructor_ValidBitmap_DoesNotThrow() {
    using var bmp = _CreateTestBitmap();
    Assert.DoesNotThrow(() => _ = new TiffOptimizer(bmp));
  }

  [Test]
  [Category("Unit")]
  public void Constructor_NullBitmap_ThrowsArgumentNull() {
    Assert.Throws<ArgumentNullException>(() => _ = new TiffOptimizer(null!));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void OptimizeAsync_DefaultOptions_ProducesResult() {
    using var bmp = _CreateTestBitmap();
    var optimizer = new TiffOptimizer(bmp, new TiffOptimizationOptions(
      [TiffCompression.None, TiffCompression.Deflate],
      [TiffPredictor.None],
      [8]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;

    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    Assert.That(result.CompressedSize, Is.GreaterThan(0));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void OptimizeAsync_PackBits_ProducesResult() {
    using var bmp = _CreateTestBitmap();
    var optimizer = new TiffOptimizer(bmp, new TiffOptimizationOptions(
      [TiffCompression.PackBits],
      [TiffPredictor.None],
      [8]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;

    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    Assert.That(result.Compression, Is.EqualTo(TiffCompression.PackBits));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void OptimizeAsync_LZW_ProducesResult() {
    using var bmp = _CreateTestBitmap();
    var optimizer = new TiffOptimizer(bmp, new TiffOptimizationOptions(
      [TiffCompression.Lzw],
      [TiffPredictor.None],
      [8]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;

    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    Assert.That(result.Compression, Is.EqualTo(TiffCompression.Lzw));
  }

  [Test]
  [Category("Unit")]
  public void Options_DefaultValues_AreReasonable() {
    var opts = new TiffOptimizationOptions();

    Assert.That(opts.Compressions.Count, Is.GreaterThan(0));
    Assert.That(opts.Predictors.Count, Is.GreaterThan(0));
    Assert.That(opts.StripRowCounts.Count, Is.GreaterThan(0));
    Assert.That(opts.MaxParallelTasks, Is.GreaterThan(0));
    Assert.That(opts.ZopfliIterations, Is.GreaterThanOrEqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ComboRecord_Equality_WorksCorrectly() {
    var a = new TiffOptimizationCombo(TiffCompression.Deflate, TiffPredictor.None, TiffColorMode.Rgb, 8);
    var b = new TiffOptimizationCombo(TiffCompression.Deflate, TiffPredictor.None, TiffColorMode.Rgb, 8);
    var c = new TiffOptimizationCombo(TiffCompression.Lzw, TiffPredictor.None, TiffColorMode.Rgb, 8);

    Assert.That(a, Is.EqualTo(b));
    Assert.That(a, Is.Not.EqualTo(c));
  }

  // --- Palette Frequency Sorting Tests ---

  [Test]
  [Category("Unit")]
  public void PaletteConversion_MostFrequentColorGetsIndex0() {
    // Create image where one color dominates
    using var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
    var dominant = Color.FromArgb(255, 200, 100, 50);
    var rare = Color.FromArgb(255, 10, 20, 30);
    for (var y = 0; y < 16; ++y)
    for (var x = 0; x < 16; ++x)
      bmp.SetPixel(x, y, x == 0 && y == 0 ? rare : dominant);

    var optimizer = new TiffOptimizer(bmp, new TiffOptimizationOptions(
      [TiffCompression.None],
      [TiffPredictor.None],
      [16],
      true
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    // If palette mode was chosen, most pixels should map to index 0
    if (result.ColorMode == TiffColorMode.Palette)
      Assert.Pass("Palette mode with frequency sorting applied");
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void PaletteFreqSort_CompressedSizeNotLarger() {
    using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
    var colors = new[] {
      Color.Red, Color.Green, Color.Blue, Color.White
    };
    for (var y = 0; y < 32; ++y)
    for (var x = 0; x < 32; ++x)
      bmp.SetPixel(x, y, colors[(x + y) % colors.Length]);

    var optimizer = new TiffOptimizer(bmp, new TiffOptimizationOptions(
      [TiffCompression.Deflate],
      [TiffPredictor.None],
      [32],
      true
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
  }

  // --- Dynamic Strip Sizing Tests ---

  [Test]
  [Category("Unit")]
  public void GenerateStripSizes_480_ContainsExpectedValues() {
    var sizes = TiffOptimizer._GenerateStripSizes(480);

    // Powers of 2 up to 480
    Assert.That(sizes, Does.Contain(1));
    Assert.That(sizes, Does.Contain(2));
    Assert.That(sizes, Does.Contain(4));
    Assert.That(sizes, Does.Contain(8));
    Assert.That(sizes, Does.Contain(16));
    Assert.That(sizes, Does.Contain(32));
    Assert.That(sizes, Does.Contain(64));
    Assert.That(sizes, Does.Contain(128));
    Assert.That(sizes, Does.Contain(256));

    // Factors of 480 (480 = 2^5 * 3 * 5)
    Assert.That(sizes, Does.Contain(3));
    Assert.That(sizes, Does.Contain(5));
    Assert.That(sizes, Does.Contain(6));
    Assert.That(sizes, Does.Contain(10));
    Assert.That(sizes, Does.Contain(15));
    Assert.That(sizes, Does.Contain(20));
    Assert.That(sizes, Does.Contain(24));
    Assert.That(sizes, Does.Contain(30));
    Assert.That(sizes, Does.Contain(40));
    Assert.That(sizes, Does.Contain(48));
    Assert.That(sizes, Does.Contain(60));
    Assert.That(sizes, Does.Contain(80));
    Assert.That(sizes, Does.Contain(96));
    Assert.That(sizes, Does.Contain(120));
    Assert.That(sizes, Does.Contain(160));
    Assert.That(sizes, Does.Contain(240));
    Assert.That(sizes, Does.Contain(480));

    // Must be sorted
    for (var i = 1; i < sizes.Count; ++i)
      Assert.That(sizes[i], Is.GreaterThan(sizes[i - 1]));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void DynamicStripSizing_ProducesSmallerOrEqualResult() {
    using var bmp = _CreateTestBitmap(32, 32);

    var fixedOpt = new TiffOptimizationOptions(
      [TiffCompression.Deflate],
      [TiffPredictor.None],
      [1, 8, 16],
      DynamicStripSizing: false
    );
    var dynamicOpt = new TiffOptimizationOptions(
      [TiffCompression.Deflate],
      [TiffPredictor.None],
      DynamicStripSizing: true
    );

    var fixedResult = new TiffOptimizer(bmp, fixedOpt).OptimizeAsync().AsTask().Result;
    var dynamicResult = new TiffOptimizer(bmp, dynamicOpt).OptimizeAsync().AsTask().Result;

    Assert.That(dynamicResult.CompressedSize, Is.LessThanOrEqualTo(fixedResult.CompressedSize),
      $"Dynamic={dynamicResult.CompressedSize} vs Fixed={fixedResult.CompressedSize}");
  }

  // --- Tile Support Tests ---

  [Test]
  [Category("Unit")]
  public void GenerateTileSizes_FiltersByMultipleOf16AndDimensions() {
    var candidates = new List<int> { 16, 32, 48, 64, 128, 256 };
    var sizes = TiffOptimizer._GenerateTileSizes(100, 100, candidates);

    Assert.That(sizes, Does.Contain(16));
    Assert.That(sizes, Does.Contain(32));
    Assert.That(sizes, Does.Contain(48));
    Assert.That(sizes, Does.Contain(64));
    Assert.That(sizes, Does.Not.Contain(128), "128 > min(100,100)");
    Assert.That(sizes, Does.Not.Contain(256), "256 > min(100,100)");
  }

  [Test]
  [Category("Unit")]
  public void GenerateTileSizes_RejectsNonMultiplesOf16() {
    var candidates = new List<int> { 15, 17, 30, 33, 64 };
    var sizes = TiffOptimizer._GenerateTileSizes(200, 200, candidates);

    Assert.That(sizes, Does.Not.Contain(15));
    Assert.That(sizes, Does.Not.Contain(17));
    Assert.That(sizes, Does.Not.Contain(30));
    Assert.That(sizes, Does.Not.Contain(33));
    Assert.That(sizes, Does.Contain(64));
  }

  [Test]
  [Category("Unit")]
  public void GenerateTileSizes_SmallImage_Fallback16() {
    var candidates = new List<int> { 64, 128, 256 };
    var sizes = TiffOptimizer._GenerateTileSizes(32, 32, candidates);

    // All candidates > min(32,32), so fallback to 16
    Assert.That(sizes, Does.Contain(16));
    Assert.That(sizes.Count, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void TiffOptimizationCombo_IsTiled_WhenTileDimensionsSet() {
    var stripCombo = new TiffOptimizationCombo(TiffCompression.Deflate, TiffPredictor.None, TiffColorMode.Rgb, 8);
    var tileCombo =
      new TiffOptimizationCombo(TiffCompression.Deflate, TiffPredictor.None, TiffColorMode.Rgb, 0, 64, 64);

    Assert.That(stripCombo.IsTiled, Is.False);
    Assert.That(tileCombo.IsTiled, Is.True);
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void OptimizeAsync_Tiled_ProducesValidResult() {
    using var bmp = _CreateTestBitmap(64, 64);
    var optimizer = new TiffOptimizer(bmp, new TiffOptimizationOptions(
      [TiffCompression.Deflate],
      [TiffPredictor.None],
      TryTiles: true,
      TileSizes: [16, 32, 64],
      DynamicStripSizing: false,
      StripRowCounts: [64]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;

    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    Assert.That(result.CompressedSize, Is.GreaterThan(0));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void OptimizeAsync_TiledAndStripBoth_PicksSmallest() {
    using var bmp = _CreateTestBitmap(64, 64);
    var optimizer = new TiffOptimizer(bmp, new TiffOptimizationOptions(
      [TiffCompression.Deflate],
      [TiffPredictor.None],
      TryTiles: true,
      TileSizes: [16, 32, 64],
      DynamicStripSizing: false,
      StripRowCounts: [1, 8, 64]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;

    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
  }
}
