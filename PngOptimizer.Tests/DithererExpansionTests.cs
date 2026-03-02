using System.Drawing;
using System.Drawing.Imaging;

namespace PngOptimizer.Tests;

[TestFixture]
public sealed class DithererExpansionTests {
  private static Bitmap _CreateColorfulBitmap(int width = 64, int height = 64) {
    var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      bmp.SetPixel(x, y, Color.FromArgb(255, x * 4 % 256, y * 4 % 256, (x + y) * 2 % 256));
    return bmp;
  }

  private static Bitmap _CreateGradientBitmap(int width = 32, int height = 32) {
    var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var r = (int)(255.0 * x / width);
      var g = (int)(255.0 * y / height);
      bmp.SetPixel(x, y, Color.FromArgb(255, r, g, 128));
    }

    return bmp;
  }

  [Test]
  [Category("Unit")]
  public void Options_UseDithering_DefaultQuantizers_HasThreeEntries() {
    var opts = new PngOptimizationOptions(UseDithering: true);
    Assert.That(opts.QuantizerNames.Count, Is.EqualTo(3));
    Assert.That(opts.QuantizerNames, Does.Contain("Wu"));
    Assert.That(opts.QuantizerNames, Does.Contain("Octree"));
    Assert.That(opts.QuantizerNames, Does.Contain("MedianCut"));
  }

  [Test]
  [Category("Unit")]
  public void Options_UseDithering_DefaultDitherers_HasTwoEntries() {
    var opts = new PngOptimizationOptions(UseDithering: true);
    Assert.That(opts.DithererNames.Count, Is.EqualTo(2));
    Assert.That(opts.DithererNames, Does.Contain("None"));
    Assert.That(opts.DithererNames, Does.Contain("FloydSteinberg"));
  }

  [Test]
  [Category("Unit")]
  public void Options_CustomQuantizerNames_ArePreserved() {
    var opts = new PngOptimizationOptions(
      UseDithering: true,
      QuantizerNames: ["Neuquant", "PngQuant"]
    );
    Assert.That(opts.QuantizerNames.Count, Is.EqualTo(2));
    Assert.That(opts.QuantizerNames, Does.Contain("Neuquant"));
  }

  [Test]
  [Category("Unit")]
  public void QuantizerDithererCombo_Equality_WorksCorrectly() {
    var a = new QuantizerDithererCombo("Wu", "None");
    var b = new QuantizerDithererCombo("Wu", "None");
    var c = new QuantizerDithererCombo("Octree", "FloydSteinberg");
    Assert.That(a, Is.EqualTo(b));
    Assert.That(a, Is.Not.EqualTo(c));
  }

  [Test]
  [Category("Unit")]
  public void OptimizationCombo_WithLossyPaletteCombo_RecordsIt() {
    var qd = new QuantizerDithererCombo("Wu", "FloydSteinberg");
    var combo = new OptimizationCombo(ColorMode.Palette, 8, InterlaceMethod.None, FilterStrategy.SingleFilter,
      DeflateMethod.Default, qd);
    Assert.That(combo.LossyPaletteCombo, Is.Not.Null);
    Assert.That(combo.LossyPaletteCombo!.Value.QuantizerName, Is.EqualTo("Wu"));
    Assert.That(combo.LossyPaletteCombo!.Value.DithererName, Is.EqualTo("FloydSteinberg"));
  }

  [Test]
  [Category("Unit")]
  public void OptimizationCombo_WithoutLossyPaletteCombo_IsNull() {
    var combo = new OptimizationCombo(ColorMode.RGB, 8, InterlaceMethod.None, FilterStrategy.SingleFilter,
      DeflateMethod.Default);
    Assert.That(combo.LossyPaletteCombo, Is.Null);
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void Optimize_WuNoDithering_ProducesValidOutput() {
    using var bmp = _CreateColorfulBitmap();
    var optimizer = new PngOptimizer(bmp, new PngOptimizationOptions(
      true,
      false,
      false,
      UseDithering: true,
      AllowLossyPalette: false,
      QuantizerNames: ["Wu"],
      DithererNames: ["None"],
      FilterStrategies: [FilterStrategy.SingleFilter],
      DeflateMethods: [DeflateMethod.Default]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    Assert.That(result.CompressedSize, Is.GreaterThan(0));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void Optimize_OctreeFloydSteinberg_ProducesValidOutput() {
    using var bmp = _CreateColorfulBitmap();
    var optimizer = new PngOptimizer(bmp, new PngOptimizationOptions(
      true,
      false,
      false,
      UseDithering: true,
      AllowLossyPalette: false,
      QuantizerNames: ["Octree"],
      DithererNames: ["FloydSteinberg"],
      FilterStrategies: [FilterStrategy.SingleFilter],
      DeflateMethods: [DeflateMethod.Default]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    Assert.That(result.CompressedSize, Is.GreaterThan(0));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void Optimize_MedianCutAtkinson_ProducesValidOutput() {
    using var bmp = _CreateGradientBitmap();
    var optimizer = new PngOptimizer(bmp, new PngOptimizationOptions(
      true,
      false,
      false,
      UseDithering: true,
      AllowLossyPalette: false,
      QuantizerNames: ["MedianCut"],
      DithererNames: ["Atkinson"],
      FilterStrategies: [FilterStrategy.SingleFilter],
      DeflateMethods: [DeflateMethod.Default]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void Optimize_Bayer4x4_ProducesValidOutput() {
    using var bmp = _CreateGradientBitmap();
    var optimizer = new PngOptimizer(bmp, new PngOptimizationOptions(
      true,
      false,
      false,
      UseDithering: true,
      AllowLossyPalette: false,
      QuantizerNames: ["Wu"],
      DithererNames: ["Bayer4x4"],
      FilterStrategies: [FilterStrategy.SingleFilter],
      DeflateMethods: [DeflateMethod.Default]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void Optimize_NeuquantQuantizer_ProducesValidOutput() {
    using var bmp = _CreateColorfulBitmap(32, 32);
    var optimizer = new PngOptimizer(bmp, new PngOptimizationOptions(
      true,
      false,
      false,
      UseDithering: true,
      AllowLossyPalette: false,
      QuantizerNames: ["Neuquant"],
      DithererNames: ["None"],
      FilterStrategies: [FilterStrategy.SingleFilter],
      DeflateMethods: [DeflateMethod.Default]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void Optimize_PngQuantQuantizer_ProducesValidOutput() {
    using var bmp = _CreateColorfulBitmap(32, 32);
    var optimizer = new PngOptimizer(bmp, new PngOptimizationOptions(
      true,
      false,
      false,
      UseDithering: true,
      AllowLossyPalette: false,
      QuantizerNames: ["PngQuant"],
      DithererNames: ["None"],
      FilterStrategies: [FilterStrategy.SingleFilter],
      DeflateMethods: [DeflateMethod.Default]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void Optimize_MultipleCombos_ProducesValidOutput() {
    using var bmp = _CreateGradientBitmap();
    var optimizer = new PngOptimizer(bmp, new PngOptimizationOptions(
      true,
      false,
      false,
      UseDithering: true,
      AllowLossyPalette: false,
      QuantizerNames: ["Wu", "Octree"],
      DithererNames: ["None", "FloydSteinberg"],
      FilterStrategies: [FilterStrategy.SingleFilter],
      DeflateMethods: [DeflateMethod.Default]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    Assert.That(result.CompressedSize, Is.GreaterThan(0));
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void Optimize_HighQuality_ProducesValidOutput() {
    using var bmp = _CreateGradientBitmap();
    var optimizer = new PngOptimizer(bmp, new PngOptimizationOptions(
      true,
      false,
      false,
      UseDithering: true,
      IsHighQualityQuantization: true,
      AllowLossyPalette: false,
      QuantizerNames: ["Wu"],
      DithererNames: ["None"],
      FilterStrategies: [FilterStrategy.SingleFilter],
      DeflateMethods: [DeflateMethod.Default]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_DitheringResult_IsReadablePng() {
    using var bmp = _CreateColorfulBitmap(32, 32);
    var optimizer = new PngOptimizer(bmp, new PngOptimizationOptions(
      true,
      false,
      false,
      UseDithering: true,
      AllowLossyPalette: false,
      QuantizerNames: ["Octree"],
      DithererNames: ["FloydSteinberg"],
      FilterStrategies: [FilterStrategy.SingleFilter],
      DeflateMethods: [DeflateMethod.Default]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;

    var tempFile = Path.Combine(Path.GetTempPath(), $"dither_e2e_{Guid.NewGuid():N}.png");
    try {
      File.WriteAllBytes(tempFile, result.FileContents);
      using var readBack = new Bitmap(tempFile);
      Assert.That(readBack.Width, Is.EqualTo(32));
      Assert.That(readBack.Height, Is.EqualTo(32));
    } finally {
      File.Delete(tempFile);
    }
  }

  [Test]
  [Category("Unit")]
  public void Options_UseDithering_False_DoesNotAffectDefaults() {
    var opts = new PngOptimizationOptions(UseDithering: false);
    Assert.That(opts.UseDithering, Is.False);
    Assert.That(opts.QuantizerNames.Count, Is.GreaterThan(0));
    Assert.That(opts.DithererNames.Count, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void OptimizationResult_WithCombo_ToStringIncludesQuantizer() {
    var result = new OptimizationResult(
      ColorMode.Palette, 8, InterlaceMethod.None, FilterStrategy.SingleFilter,
      DeflateMethod.Default, 1000, [], 0, TimeSpan.Zero, [],
      new QuantizerDithererCombo("Wu", "FloydSteinberg")
    );
    var str = result.ToString();
    Assert.That(str, Does.Contain("Wu+FloydSteinberg"));
  }

  [Test]
  [Category("Unit")]
  [TestCase("Wu")]
  [TestCase("Octree")]
  [TestCase("MedianCut")]
  [TestCase("Neuquant")]
  [TestCase("PngQuant")]
  public void UnknownDitherer_ThrowsArgumentException(string quantizer) {
    using var bmp = _CreateColorfulBitmap(32, 32);
    var optimizer = new PngOptimizer(bmp, new PngOptimizationOptions(
      true,
      false,
      false,
      UseDithering: true,
      AllowLossyPalette: false,
      QuantizerNames: [quantizer],
      DithererNames: ["NonExistentDitherer"],
      FilterStrategies: [FilterStrategy.SingleFilter],
      DeflateMethods: [DeflateMethod.Default]
    ));

    Assert.Throws<AggregateException>(() => _ = optimizer.OptimizeAsync().AsTask().Result);
  }

  [Test]
  [Category("Unit")]
  public void UnknownQuantizer_ThrowsArgumentException() {
    using var bmp = _CreateColorfulBitmap(32, 32);
    var optimizer = new PngOptimizer(bmp, new PngOptimizationOptions(
      true,
      false,
      false,
      UseDithering: true,
      AllowLossyPalette: false,
      QuantizerNames: ["NonExistentQuantizer"],
      DithererNames: ["None"],
      FilterStrategies: [FilterStrategy.SingleFilter],
      DeflateMethods: [DeflateMethod.Default]
    ));

    Assert.Throws<AggregateException>(() => _ = optimizer.OptimizeAsync().AsTask().Result);
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public void Optimize_SierraErrorDiffusion_ProducesValidOutput() {
    using var bmp = _CreateGradientBitmap();
    var optimizer = new PngOptimizer(bmp, new PngOptimizationOptions(
      true,
      false,
      false,
      UseDithering: true,
      AllowLossyPalette: false,
      QuantizerNames: ["Wu"],
      DithererNames: ["Sierra"],
      FilterStrategies: [FilterStrategy.SingleFilter],
      DeflateMethods: [DeflateMethod.Default]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
  }
}
