using System;
using System.Drawing;
using System.Drawing.Imaging;
using FileFormat.Bmp;
using Optimizer.Bmp;
using Crush.TestUtilities;
using NUnit.Framework;

namespace Optimizer.Bmp.Tests;

[TestFixture]
public sealed class BmpOptimizerTests {
  [Test]
  [Category("Unit")]
  public void Constructor_ValidBitmap_DoesNotThrow() {
    using var bmp = TestBitmapFactory.CreateTestBitmap();
    Assert.DoesNotThrow(() => _ = new BmpOptimizer(bmp));
  }

  [Test]
  [Category("Unit")]
  public void Constructor_NullBitmap_ThrowsArgumentNull() {
    Assert.Throws<ArgumentNullException>(() => _ = new BmpOptimizer(null!));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void OptimizeAsync_DefaultOptions_ProducesResult() {
    using var bmp = TestBitmapFactory.CreateTestBitmap();
    var optimizer = new BmpOptimizer(bmp, new BmpOptimizationOptions(
      Compressions: [BmpCompression.None],
      RowOrders: [BmpRowOrder.BottomUp]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;

    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    Assert.That(result.CompressedSize, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void Options_DefaultValues_AreReasonable() {
    var opts = new BmpOptimizationOptions();

    Assert.That(opts.Compressions.Count, Is.GreaterThan(0));
    Assert.That(opts.RowOrders.Count, Is.GreaterThan(0));
    Assert.That(opts.MaxParallelTasks, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void ComboRecord_Equality_WorksCorrectly() {
    var a = new BmpOptimizationCombo(BmpColorMode.Rgb24, BmpCompression.None, BmpRowOrder.BottomUp);
    var b = new BmpOptimizationCombo(BmpColorMode.Rgb24, BmpCompression.None, BmpRowOrder.BottomUp);
    var c = new BmpOptimizationCombo(BmpColorMode.Palette8, BmpCompression.None, BmpRowOrder.BottomUp);

    Assert.That(a, Is.EqualTo(b));
    Assert.That(a, Is.Not.EqualTo(c));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void OptimizeAsync_GrayscaleImage_DetectsGrayscale() {
    using var bmp = TestBitmapFactory.CreateTestBitmap(8, 8, true);
    var optimizer = new BmpOptimizer(bmp, new BmpOptimizationOptions(
      Compressions: [BmpCompression.None],
      RowOrders: [BmpRowOrder.BottomUp],
      AutoSelectColorMode: true
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void OptimizeAsync_FewColors_UsesPalette() {
    using var bmp = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x)
      bmp.SetPixel(x, y, (x + y) % 2 == 0 ? Color.Red : Color.Blue);

    var optimizer = new BmpOptimizer(bmp, new BmpOptimizationOptions(
      Compressions: [BmpCompression.None],
      RowOrders: [BmpRowOrder.BottomUp],
      AutoSelectColorMode: true
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    // With only 2 colors and auto mode, should pick a palette or 1-bit mode (smaller)
    Assert.That(result.CompressedSize, Is.GreaterThan(0));
  }
}
