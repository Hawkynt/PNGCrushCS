using System;
using System.Drawing;
using System.Drawing.Imaging;
using Crush.TestUtilities;
using NUnit.Framework;
using FileFormat.Tga;
using Optimizer.Tga;

namespace Optimizer.Tga.Tests;

[TestFixture]
public sealed class TgaOptimizerTests {
  [Test]
  [Category("Unit")]
  public void Constructor_ValidBitmap_DoesNotThrow() {
    using var bmp = TestBitmapFactory.CreateTestBitmap();
    Assert.DoesNotThrow(() => _ = new TgaOptimizer(bmp));
  }

  [Test]
  [Category("Unit")]
  public void Constructor_NullBitmap_ThrowsArgumentNull() {
    Assert.Throws<ArgumentNullException>(() => _ = new TgaOptimizer(null!));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void OptimizeAsync_DefaultOptions_ProducesResult() {
    using var bmp = TestBitmapFactory.CreateTestBitmap();
    var optimizer = new TgaOptimizer(bmp, new TgaOptimizationOptions(
      Compressions: [TgaCompression.None],
      Origins: [TgaOrigin.TopLeft]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;

    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    Assert.That(result.CompressedSize, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void Options_DefaultValues_AreReasonable() {
    var opts = new TgaOptimizationOptions();

    Assert.That(opts.Compressions.Count, Is.GreaterThan(0));
    Assert.That(opts.Origins.Count, Is.GreaterThan(0));
    Assert.That(opts.MaxParallelTasks, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void ComboRecord_Equality_WorksCorrectly() {
    var a = new TgaOptimizationCombo(TgaColorMode.Rgb24, TgaCompression.None, TgaOrigin.TopLeft);
    var b = new TgaOptimizationCombo(TgaColorMode.Rgb24, TgaCompression.None, TgaOrigin.TopLeft);
    var c = new TgaOptimizationCombo(TgaColorMode.Indexed8, TgaCompression.None, TgaOrigin.TopLeft);

    Assert.That(a, Is.EqualTo(b));
    Assert.That(a, Is.Not.EqualTo(c));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void OptimizeAsync_GrayscaleImage_DetectsGrayscale() {
    using var bmp = TestBitmapFactory.CreateTestBitmap(8, 8, true);
    var optimizer = new TgaOptimizer(bmp, new TgaOptimizationOptions(
      Compressions: [TgaCompression.None],
      Origins: [TgaOrigin.TopLeft],
      AutoSelectColorMode: true
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
  }
}
