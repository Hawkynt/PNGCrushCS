using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using FileFormat.Png;
using ArgbPixel = Optimizer.Png.PngOptimizer.ArgbPixel;

namespace Optimizer.Png.Tests;

[TestFixture]
[SupportedOSPlatform("windows")]
public sealed class MedianCutQuantizerTests {
  private static ArgbPixel MakePixel(byte r, byte g, byte b, byte a = 255) {
    return new ArgbPixel { R = r, G = g, B = b, A = a };
  }

  [Test]
  public void FourDistinctColors_MaxColors4_ExactPalette() {
    var pixels = new[] {
      MakePixel(255, 0, 0),
      MakePixel(0, 255, 0),
      MakePixel(0, 0, 255),
      MakePixel(255, 255, 0)
    };

    var quantizer = new MedianCutQuantizer(pixels, pixels.Length, 4, false);
    var (palette, count) = quantizer.Quantize();

    Assert.That(count, Is.EqualTo(4));
    Assert.That(palette, Has.Count.EqualTo(4));
  }

  [Test]
  public void ManyColors_MaxColors16_Returns16Entries() {
    var pixels = new ArgbPixel[1000];
    for (var i = 0; i < 1000; ++i)
      pixels[i] = MakePixel((byte)(i % 256), (byte)(i * 7 % 256), (byte)(i * 13 % 256));

    var quantizer = new MedianCutQuantizer(pixels, pixels.Length, 16, false);
    var (palette, count) = quantizer.Quantize();

    Assert.That(count, Is.EqualTo(16));
    Assert.That(palette, Has.Count.EqualTo(16));
  }

  [Test]
  public void AllSameColor_Returns1Entry() {
    var pixels = new ArgbPixel[100];
    for (var i = 0; i < 100; ++i)
      pixels[i] = MakePixel(128, 64, 200);

    var quantizer = new MedianCutQuantizer(pixels, pixels.Length, 256, false);
    var (palette, count) = quantizer.Quantize();

    Assert.That(count, Is.EqualTo(1));
    Assert.That(palette[0].R, Is.EqualTo(128));
    Assert.That(palette[0].G, Is.EqualTo(64));
    Assert.That(palette[0].B, Is.EqualTo(200));
  }

  [Test]
  public void MaxColors1_ReturnsSingleAveragedColor() {
    var pixels = new[] {
      MakePixel(0, 0, 0),
      MakePixel(0, 0, 0),
      MakePixel(0, 0, 0),
      MakePixel(255, 255, 255)
    };

    var quantizer = new MedianCutQuantizer(pixels, pixels.Length, 1, false);
    var (palette, count) = quantizer.Quantize();

    Assert.That(count, Is.EqualTo(1));
    // Weighted average: (0*3 + 255*1)/4 = 63.75 -> 64
    Assert.That(palette[0].R, Is.EqualTo(64).Within(1));
    Assert.That(palette[0].G, Is.EqualTo(64).Within(1));
    Assert.That(palette[0].B, Is.EqualTo(64).Within(1));
  }

  [Test]
  public void WithAlpha_DistinctArgbColors_Preserved() {
    var pixels = new[] {
      MakePixel(255, 0, 0, 128),
      MakePixel(255, 0, 0),
      MakePixel(0, 255, 0, 128),
      MakePixel(0, 255, 0)
    };

    var quantizer = new MedianCutQuantizer(pixels, pixels.Length, 4, true);
    var (palette, count) = quantizer.Quantize();

    Assert.That(count, Is.EqualTo(4));
  }

  [Test]
  public void SplitOccursAlongWidestAxis() {
    // Colors spread along R axis only (G=0, B=0)
    var pixels = new[] {
      MakePixel(0, 0, 0),
      MakePixel(0, 0, 0),
      MakePixel(255, 0, 0),
      MakePixel(255, 0, 0)
    };

    var quantizer = new MedianCutQuantizer(pixels, pixels.Length, 2, false);
    var (palette, count) = quantizer.Quantize();

    Assert.That(count, Is.EqualTo(2));

    // One centroid should be near (0,0,0) and the other near (255,0,0)
    var sorted = palette.OrderBy(p => p.R).ToList();
    Assert.That(sorted[0].R, Is.EqualTo(0).Within(1));
    Assert.That(sorted[1].R, Is.EqualTo(255).Within(1));
  }

  [Test]
  public void FindNearest_ReturnsCorrectIndex() {
    var pixels = new[] {
      MakePixel(0, 0, 0),
      MakePixel(255, 255, 255)
    };

    var quantizer = new MedianCutQuantizer(pixels, pixels.Length, 2, false);
    quantizer.Quantize();

    // A dark pixel should map to the dark palette entry
    var darkIdx = quantizer.FindNearest(MakePixel(10, 10, 10));
    var lightIdx = quantizer.FindNearest(MakePixel(240, 240, 240));

    Assert.That(darkIdx, Is.Not.EqualTo(lightIdx));
  }

  [Test]
  public void FindNearest_WithAlpha_ConsidersAlphaChannel() {
    var pixels = new[] {
      MakePixel(128, 128, 128, 0),
      MakePixel(128, 128, 128)
    };

    var quantizer = new MedianCutQuantizer(pixels, pixels.Length, 2, true);
    quantizer.Quantize();

    var transparentIdx = quantizer.FindNearest(MakePixel(128, 128, 128, 10));
    var opaqueIdx = quantizer.FindNearest(MakePixel(128, 128, 128, 250));

    Assert.That(transparentIdx, Is.Not.EqualTo(opaqueIdx));
  }

  [Test]
  public async Task GradientImage_LossyPalette_ProducesValidPng() {
    using var bmp = CreateGradientBitmap(64, 64);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      AllowLossyPalette = true,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    VerifyPngSignature(result.FileContents);

    // Verify it can be loaded back
    using var ms = new MemoryStream(result.FileContents);
    using var readBack = new Bitmap(ms);
    Assert.That(readBack.Width, Is.EqualTo(64));
    Assert.That(readBack.Height, Is.EqualTo(64));
  }

  [Test]
  public async Task GradientImage_DefaultNoLossy_DoesNotUsePalette() {
    using var bmp = CreateGradientBitmap(64, 64);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      AllowLossyPalette = false,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    Assert.That(result.ColorMode, Is.Not.EqualTo(PngColorType.Palette),
      "Default (no lossy) should not use palette for >256 color image");
  }

  [Test]
  public async Task LossyPalette_ReadbackWithinErrorBounds() {
    using var bmp = CreateGradientBitmap(32, 32);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      AllowLossyPalette = true,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    // If palette was chosen, verify quantization error is bounded
    if (result.ColorMode == PngColorType.Palette) {
      using var ms = new MemoryStream(result.FileContents);
      using var readBack = new Bitmap(ms);

      var maxError = 0;
      for (var y = 0; y < 32; ++y)
      for (var x = 0; x < 32; ++x) {
        var orig = bmp.GetPixel(x, y);
        var loaded = readBack.GetPixel(x, y);
        var error = Math.Abs(orig.R - loaded.R) + Math.Abs(orig.G - loaded.G) + Math.Abs(orig.B - loaded.B);
        maxError = Math.Max(maxError, error);
      }

      // Median-cut quantization error should be reasonable (< 200 per channel sum for 256 colors)
      Assert.That(maxError, Is.LessThan(200), "Quantization error too large");
    }
  }

  private static Bitmap CreateGradientBitmap(int width, int height) {
    var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      bmp.SetPixel(x, y, Color.FromArgb(255, x % 256, y % 256, (x + y) % 256));
    return bmp;
  }

  private static void VerifyPngSignature(byte[] data) {
    Assert.That(data.Length, Is.GreaterThanOrEqualTo(8));
    Assert.That(data[0], Is.EqualTo(137));
    Assert.That(data[1], Is.EqualTo((byte)'P'));
    Assert.That(data[2], Is.EqualTo((byte)'N'));
    Assert.That(data[3], Is.EqualTo((byte)'G'));
  }
}
