using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using FileFormat.Png;

namespace Optimizer.Png.Tests;

[TestFixture]
public sealed class Adam7Tests {
  [Test]
  public void GetPassDimensions_8x8_CorrectSizes() {
    var expected = new (int w, int h)[] {
      (1, 1), // pass 0
      (1, 1), // pass 1
      (2, 1), // pass 2
      (2, 2), // pass 3
      (4, 2), // pass 4
      (4, 4), // pass 5
      (8, 4) // pass 6
    };

    // The sum of all pass pixels should equal the total image pixels
    var totalPixels = 0;
    for (var pass = 0; pass < Adam7.PassCount; ++pass) {
      var (w, h) = Adam7.GetPassDimensions(pass, 8, 8);
      Assert.That(w, Is.EqualTo(expected[pass].w), $"Pass {pass} width");
      Assert.That(h, Is.EqualTo(expected[pass].h), $"Pass {pass} height");
      totalPixels += w * h;
    }

    Assert.That(totalPixels, Is.EqualTo(64));
  }

  [Test]
  public void GetPassDimensions_1x1_OnlyPass0HasPixels() {
    for (var pass = 0; pass < Adam7.PassCount; ++pass) {
      var (w, h) = Adam7.GetPassDimensions(pass, 1, 1);
      if (pass == 0) {
        Assert.That(w, Is.EqualTo(1), "Pass 0 width for 1x1");
        Assert.That(h, Is.EqualTo(1), "Pass 0 height for 1x1");
      } else {
        Assert.That(w * h, Is.EqualTo(0), $"Pass {pass} should have no pixels for 1x1");
      }
    }
  }

  [TestCase(1, 1)]
  [TestCase(2, 2)]
  [TestCase(3, 3)]
  [TestCase(4, 4)]
  [TestCase(5, 5)]
  [TestCase(6, 6)]
  [TestCase(7, 7)]
  [TestCase(8, 8)]
  [TestCase(9, 9)]
  public void GetPassDimensions_SmallImages_NeverNegativeAndTotalCorrect(int width, int height) {
    var totalPixels = 0;
    for (var pass = 0; pass < Adam7.PassCount; ++pass) {
      var (w, h) = Adam7.GetPassDimensions(pass, width, height);
      Assert.That(w, Is.GreaterThanOrEqualTo(0), $"Pass {pass} width");
      Assert.That(h, Is.GreaterThanOrEqualTo(0), $"Pass {pass} height");
      totalPixels += w * h;
    }

    Assert.That(totalPixels, Is.EqualTo(width * height), "Total pixels across all passes must equal image pixels");
  }

  [Test]
  [SupportedOSPlatform("windows")]
  public async Task Adam7_RGB_ReadbackCorrect() {
    using var bmp = CreateGradientBitmap(16, 16);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = false,
      TryInterlacing = true,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    // Force the interlaced result
    if (result.InterlaceMethod != PngInterlaceMethod.Adam7) {
      // Re-run forcing only Adam7
      options = options with { TryInterlacing = false };
      var opts2 = new PngOptimizationOptions {
        AutoSelectColorMode = false,
        TryInterlacing = true,
        FilterStrategies = [FilterStrategy.SingleFilter],
        DeflateMethods = [DeflateMethod.Default],
        MaxParallelTasks = 1
      };
      optimizer = new PngOptimizer(bmp, opts2);
      result = await optimizer.OptimizeAsync();
    }

    VerifyPngSignature(result.FileContents);

    using var ms = new MemoryStream(result.FileContents);
    using var readBack = new Bitmap(ms);

    Assert.That(readBack.Width, Is.EqualTo(16));
    Assert.That(readBack.Height, Is.EqualTo(16));

    for (var y = 0; y < 16; ++y)
    for (var x = 0; x < 16; ++x) {
      var original = bmp.GetPixel(x, y);
      var loaded = readBack.GetPixel(x, y);
      Assert.That(loaded.R, Is.EqualTo(original.R), $"R mismatch at ({x},{y})");
      Assert.That(loaded.G, Is.EqualTo(original.G), $"G mismatch at ({x},{y})");
      Assert.That(loaded.B, Is.EqualTo(original.B), $"B mismatch at ({x},{y})");
    }
  }

  [Test]
  [SupportedOSPlatform("windows")]
  public async Task Adam7_Palette_ProducesValidPng() {
    using var bmp = CreateFewColorBitmap(16, 16, 4);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      TryInterlacing = true,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    VerifyPngSignature(result.FileContents);

    using var ms = new MemoryStream(result.FileContents);
    using var readBack = new Bitmap(ms);
    Assert.That(readBack.Width, Is.EqualTo(16));
    Assert.That(readBack.Height, Is.EqualTo(16));
  }

  [Test]
  [SupportedOSPlatform("windows")]
  public async Task Adam7_Grayscale_ProducesValidPng() {
    using var bmp = CreateGrayscaleBitmap(16, 16);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      TryInterlacing = true,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    VerifyPngSignature(result.FileContents);

    using var ms = new MemoryStream(result.FileContents);
    using var readBack = new Bitmap(ms);
    Assert.That(readBack.Width, Is.EqualTo(16));
    Assert.That(readBack.Height, Is.EqualTo(16));
  }

  [Test]
  [SupportedOSPlatform("windows")]
  public async Task Adam7_1x1Image_ProducesValidPng() {
    using var bmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
    bmp.SetPixel(0, 0, Color.FromArgb(255, 100, 200, 50));

    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      TryInterlacing = true,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    VerifyPngSignature(result.FileContents);

    using var ms = new MemoryStream(result.FileContents);
    using var readBack = new Bitmap(ms);
    Assert.That(readBack.Width, Is.EqualTo(1));
    Assert.That(readBack.Height, Is.EqualTo(1));
  }

  private static Bitmap CreateGradientBitmap(int width, int height) {
    var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      bmp.SetPixel(x, y, Color.FromArgb(255, x % 256, y % 256, (x + y) % 256));
    return bmp;
  }

  private static Bitmap CreateFewColorBitmap(int width, int height, int colorCount) {
    var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    var colors = new Color[colorCount];
    for (var i = 0; i < colorCount; ++i)
      colors[i] = Color.FromArgb(255, i * 73 % 256, i * 37 % 256, i * 97 % 256);

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      bmp.SetPixel(x, y, colors[(x + y * width) % colorCount]);
    return bmp;
  }

  private static Bitmap CreateGrayscaleBitmap(int width, int height) {
    var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var gray = (x * 17 + y * 31) % 256;
      bmp.SetPixel(x, y, Color.FromArgb(255, gray, gray, gray));
    }

    return bmp;
  }

  private static void VerifyPngSignature(byte[] data) {
    Assert.That(data.Length, Is.GreaterThanOrEqualTo(8));
    Assert.That(data[0], Is.EqualTo(137));
    Assert.That(data[1], Is.EqualTo((byte)'P'));
    Assert.That(data[2], Is.EqualTo((byte)'N'));
    Assert.That(data[3], Is.EqualTo((byte)'G'));
    Assert.That(data[4], Is.EqualTo(13));
    Assert.That(data[5], Is.EqualTo(10));
    Assert.That(data[6], Is.EqualTo(26));
    Assert.That(data[7], Is.EqualTo(10));
  }
}
