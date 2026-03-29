using System;
using System.Drawing;
using System.Drawing.Imaging;
using Crush.TestUtilities;
using NUnit.Framework;
using FileFormat.Pcx;
using Optimizer.Pcx;

namespace Optimizer.Pcx.Tests;

[TestFixture]
public sealed class PcxOptimizerTests {
  [Test]
  [Category("Unit")]
  public void Constructor_ValidBitmap_DoesNotThrow() {
    using var bmp = TestBitmapFactory.CreateTestBitmap();
    Assert.DoesNotThrow(() => _ = new PcxOptimizer(bmp));
  }

  [Test]
  [Category("Unit")]
  public void Constructor_NullBitmap_ThrowsArgumentNull() {
    Assert.Throws<ArgumentNullException>(() => _ = new PcxOptimizer(null!));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void OptimizeAsync_DefaultOptions_ProducesResult() {
    using var bmp = TestBitmapFactory.CreateTestBitmap();
    var optimizer = new PcxOptimizer(bmp, new PcxOptimizationOptions(
      PlaneConfigs: [PcxPlaneConfig.SeparatePlanes],
      PaletteOrders: [PcxPaletteOrder.Original]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;

    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    Assert.That(result.CompressedSize, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void Options_DefaultValues_AreReasonable() {
    var opts = new PcxOptimizationOptions();

    Assert.That(opts.PlaneConfigs.Count, Is.GreaterThan(0));
    Assert.That(opts.PaletteOrders.Count, Is.GreaterThan(0));
    Assert.That(opts.MaxParallelTasks, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void ComboRecord_Equality_WorksCorrectly() {
    var a = new PcxOptimizationCombo(PcxColorMode.Rgb24, PcxPlaneConfig.SeparatePlanes, PcxPaletteOrder.Original);
    var b = new PcxOptimizationCombo(PcxColorMode.Rgb24, PcxPlaneConfig.SeparatePlanes, PcxPaletteOrder.Original);
    var c = new PcxOptimizationCombo(PcxColorMode.Indexed8, PcxPlaneConfig.SinglePlane, PcxPaletteOrder.Original);

    Assert.That(a, Is.EqualTo(b));
    Assert.That(a, Is.Not.EqualTo(c));
  }
}
