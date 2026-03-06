using System;
using System.Drawing;
using System.Drawing.Imaging;
using Crush.TestUtilities;
using FileFormat.Jpeg;
using NUnit.Framework;
using Optimizer.Jpeg;

namespace Optimizer.Jpeg.Tests;

[TestFixture]
public sealed class JpegOptimizerTests {
  [Test]
  [Category("Unit")]
  public void Constructor_ValidBitmap_DoesNotThrow() {
    using var bmp = TestBitmapFactory.CreateTestBitmap();
    Assert.DoesNotThrow(() => _ = new JpegOptimizer(bmp));
  }

  [Test]
  [Category("Unit")]
  public void Constructor_NullBitmap_ThrowsArgumentNull() {
    Assert.Throws<ArgumentNullException>(() => _ = new JpegOptimizer(null!));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void OptimizeAsync_LossyMode_ProducesResult() {
    using var bmp = TestBitmapFactory.CreateTestBitmap();
    var optimizer = new JpegOptimizer(bmp, new JpegOptimizationOptions(
      AllowLossy: true,
      MinQuality: 75,
      Qualities: [85],
      Subsamplings: [JpegSubsampling.Chroma444]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;

    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    Assert.That(result.CompressedSize, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void Options_DefaultValues_AreReasonable() {
    var opts = new JpegOptimizationOptions();

    Assert.That(opts.Modes.Count, Is.GreaterThan(0));
    Assert.That(opts.Qualities.Count, Is.GreaterThan(0));
    Assert.That(opts.Subsamplings.Count, Is.GreaterThan(0));
    Assert.That(opts.MaxParallelTasks, Is.GreaterThan(0));
    Assert.That(opts.MinQuality, Is.InRange(1, 100));
  }

  [Test]
  [Category("Unit")]
  public void ComboRecord_Equality_WorksCorrectly() {
    var a = new JpegOptimizationCombo(JpegMode.Baseline, true, true, false, 0, JpegSubsampling.Chroma444);
    var b = new JpegOptimizationCombo(JpegMode.Baseline, true, true, false, 0, JpegSubsampling.Chroma444);
    var c = new JpegOptimizationCombo(JpegMode.Progressive, true, true, false, 0, JpegSubsampling.Chroma444);

    Assert.That(a, Is.EqualTo(b));
    Assert.That(a, Is.Not.EqualTo(c));
  }
}
