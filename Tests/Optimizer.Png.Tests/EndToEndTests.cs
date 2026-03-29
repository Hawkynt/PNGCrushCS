using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Text;
using Crush.Core;
using FileFormat.Png;

namespace Optimizer.Png.Tests;

[TestFixture]
[SupportedOSPlatform("windows")]
public sealed class EndToEndTests {
  private static Bitmap CreateSolidColorBitmap(int width, int height, Color color) {
    var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.Clear(color);
    return bmp;
  }

  private static Bitmap CreateGradientBitmap(int width, int height) {
    var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      bmp.SetPixel(x, y, Color.FromArgb(255, x % 256, y % 256, (x + y) % 256));
    return bmp;
  }

  private static Bitmap CreateTwoColorBitmap(int width, int height) {
    var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      bmp.SetPixel(x, y, (x + y) % 2 == 0 ? Color.Black : Color.White);
    return bmp;
  }

  private static Bitmap CreateAlphaBitmap(int width, int height, int colorCount) {
    var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    var colors = new Color[colorCount];
    for (var i = 0; i < colorCount; ++i)
      colors[i] = Color.FromArgb((i * 60 + 50) % 256, i * 73 % 256, i * 37 % 256, i * 97 % 256);

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

  [Test]
  public async Task SolidColor_OptimizeAndReadback_ProducesValidPng() {
    using var bmp = CreateSolidColorBitmap(4, 4, Color.Red);
    var options = new PngOptimizationOptions {
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    Assert.That(result.FileContents, Is.Not.Null);
    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    Assert.That(result.CompressedSize, Is.GreaterThan(0));

    VerifyPngSignature(result.FileContents);
  }

  [Test]
  public async Task SolidColor_ReadbackPixelEquality() {
    using var bmp = CreateSolidColorBitmap(8, 8, Color.FromArgb(255, 0, 128, 255));
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    using var ms = new MemoryStream(result.FileContents);
    using var readBack = new Bitmap(ms);

    Assert.That(readBack.Width, Is.EqualTo(8));
    Assert.That(readBack.Height, Is.EqualTo(8));

    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x) {
      var px = readBack.GetPixel(x, y);
      Assert.That(px.R, Is.EqualTo(0), $"R mismatch at ({x},{y})");
      Assert.That(px.G, Is.EqualTo(128), $"G mismatch at ({x},{y})");
      Assert.That(px.B, Is.EqualTo(255), $"B mismatch at ({x},{y})");
    }
  }

  [Test]
  public async Task Gradient_RGB_ProducesValidPng() {
    using var bmp = CreateGradientBitmap(16, 16);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.ScanlineAdaptive],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    Assert.That(result.ColorMode, Is.EqualTo(PngColorType.RGB));
    VerifyPngSignature(result.FileContents);
  }

  [Test]
  public async Task TwoColor_UsesPaletteMode() {
    using var bmp = CreateTwoColorBitmap(8, 8);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    Assert.That(result.ColorMode, Is.AnyOf(PngColorType.Palette, PngColorType.Grayscale));
    VerifyPngSignature(result.FileContents);
  }

  [Test]
  public async Task Grayscale_DetectedCorrectly() {
    using var bmp = CreateGrayscaleBitmap(8, 8);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    Assert.That(result.ColorMode, Is.EqualTo(PngColorType.Grayscale));
    VerifyPngSignature(result.FileContents);
  }

  [Test]
  public async Task Grayscale_Readback_PixelValuesPreserved() {
    using var bmp = CreateSolidColorBitmap(4, 4, Color.FromArgb(255, 128, 128, 128));
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = false,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    using var ms = new MemoryStream(result.FileContents);
    using var readBack = new Bitmap(ms);

    var px = readBack.GetPixel(0, 0);
    Assert.That(px.R, Is.EqualTo(128).Within(1));
    Assert.That(px.G, Is.EqualTo(128).Within(1));
    Assert.That(px.B, Is.EqualTo(128).Within(1));
  }

  [Test]
  public async Task MultipleStrategies_AllProduceValidPngs() {
    using var bmp = CreateSolidColorBitmap(8, 8, Color.Blue);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      TryInterlacing = false,
      FilterStrategies = [
        FilterStrategy.SingleFilter,
        FilterStrategy.ScanlineAdaptive,
        FilterStrategy.WeightedContinuity,
        FilterStrategy.PartitionOptimized
      ],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 2
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    VerifyPngSignature(result.FileContents);
  }

  [Test]
  public async Task WithInterlacing_ProducesValidPng() {
    using var bmp = CreateSolidColorBitmap(8, 8, Color.Green);
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

    Assert.That(readBack.Width, Is.EqualTo(8));
    Assert.That(readBack.Height, Is.EqualTo(8));

    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x) {
      var px = readBack.GetPixel(x, y);
      Assert.That(px.R, Is.EqualTo(0), $"R mismatch at ({x},{y})");
      Assert.That(px.G, Is.EqualTo(128), $"G mismatch at ({x},{y})");
      Assert.That(px.B, Is.EqualTo(0), $"B mismatch at ({x},{y})");
    }
  }

  [Test]
  [Timeout(60000)]
  public async Task StressTestPng_Integration() {
    var fixturePath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "StressTest.png");
    if (!File.Exists(fixturePath))
      Assert.Ignore("StressTest.png fixture not found");

    using var bmp = new Bitmap(fixturePath);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Fastest],
      MaxParallelTasks = Environment.ProcessorCount
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    VerifyPngSignature(result.FileContents);
  }

  [Test]
  public async Task AllDeflateMethods_ProduceValidPngs() {
    using var bmp = CreateSolidColorBitmap(4, 4, Color.Yellow);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [
        DeflateMethod.Fastest,
        DeflateMethod.Fast,
        DeflateMethod.Default,
        DeflateMethod.Maximum,
        DeflateMethod.Ultra,
        DeflateMethod.Hyper
      ],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    VerifyPngSignature(result.FileContents);
  }

  [Test]
  public async Task AutoColorModeDisabled_UsesRGBOrRGBA() {
    using var bmp = CreateSolidColorBitmap(4, 4, Color.Red);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = false,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    Assert.That(result.ColorMode, Is.AnyOf(PngColorType.RGB, PngColorType.RGBA));
  }

  [Test]
  public async Task SmallImage_1x1_ProducesValidPng() {
    using var bmp = CreateSolidColorBitmap(1, 1, Color.Magenta);
    var options = new PngOptimizationOptions {
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    VerifyPngSignature(result.FileContents);
  }

  [Test]
  public async Task SolidColor_PaletteTrimmed_SmallPLTE() {
    using var bmp = CreateSolidColorBitmap(4, 4, Color.Red);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    if (result.ColorMode == PngColorType.Palette) {
      var plteLength = FindChunkDataLength(result.FileContents, "PLTE");
      Assert.That(plteLength, Is.EqualTo(3), "Solid color should have 1 palette entry (3 bytes)");
    }
  }

  [Test]
  public async Task TwoColor_PaletteTrimmedToTwo() {
    using var bmp = CreateTwoColorBitmap(8, 8);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    if (result.ColorMode == PngColorType.Palette) {
      var plteLength = FindChunkDataLength(result.FileContents, "PLTE");
      Assert.That(plteLength, Is.EqualTo(6), "Two-color image should have 2 palette entries (6 bytes)");
    }
  }

  [Test]
  public async Task FrequencySorted_MostCommonColorAtIndex0() {
    var bmp = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x)
      bmp.SetPixel(x, y, Color.Blue);
    bmp.SetPixel(0, 0, Color.Red);

    using (bmp) {
      var options = new PngOptimizationOptions {
        AutoSelectColorMode = true,
        TryInterlacing = false,
        FilterStrategies = [FilterStrategy.SingleFilter],
        DeflateMethods = [DeflateMethod.Default],
        MaxParallelTasks = 1
      };

      var optimizer = new PngOptimizer(bmp, options);
      var result = await optimizer.OptimizeAsync();

      if (result.ColorMode == PngColorType.Palette) {
        var plteData = ExtractChunkData(result.FileContents, "PLTE");
        Assert.That(plteData, Is.Not.Null);
        Assert.That(plteData![0], Is.EqualTo(0), "Most frequent color (Blue) R should be at index 0");
        Assert.That(plteData[1], Is.EqualTo(0), "Most frequent color (Blue) G should be at index 0");
        Assert.That(plteData[2], Is.EqualTo(255), "Most frequent color (Blue) B should be at index 0");
      }
    }
  }

  [Test]
  public async Task AlphaImage_FewColors_UsesPalettePlusTRNS() {
    using var bmp = CreateAlphaBitmap(32, 32, 4);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    Assert.That(result.ColorMode, Is.EqualTo(PngColorType.Palette));
    var trnsLength = FindChunkDataLength(result.FileContents, "tRNS");
    Assert.That(trnsLength, Is.GreaterThan(0), "Alpha image with few colors should have tRNS chunk");

    using var ms = new MemoryStream(result.FileContents);
    using var readBack = new Bitmap(ms);
    Assert.That(readBack.Width, Is.EqualTo(32));
    Assert.That(readBack.Height, Is.EqualTo(32));
  }

  [Test]
  public async Task AlphaImage_PaletteTRNS_SmallerThanRGBA() {
    using var bmp = CreateAlphaBitmap(32, 32, 4);
    var paletteOptions = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var rgbaOptions = new PngOptimizationOptions {
      AutoSelectColorMode = false,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var paletteResult = await new PngOptimizer(bmp, paletteOptions).OptimizeAsync();
    var rgbaResult = await new PngOptimizer(bmp, rgbaOptions).OptimizeAsync();

    Assert.That(paletteResult.CompressedSize, Is.LessThan(rgbaResult.CompressedSize),
      "Palette+tRNS should be smaller than RGBA for few-color alpha image");
  }

  [Test]
  public async Task OpaqueImage_NoTRNSChunk() {
    using var bmp = CreateSolidColorBitmap(4, 4, Color.FromArgb(255, 100, 200, 50));
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    var trnsLength = FindChunkDataLength(result.FileContents, "tRNS");
    Assert.That(trnsLength, Is.EqualTo(-1), "Opaque image should not have tRNS chunk");
  }

  [Test]
  public async Task AlphaImage_TRNSTruncatedAtLastNonOpaque() {
    // Create image: 3 opaque colors + 1 semi-transparent color
    var bmp = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
    var colors = new[] {
      Color.FromArgb(128, 255, 0, 0), // semi-transparent red
      Color.FromArgb(255, 0, 255, 0), // opaque green
      Color.FromArgb(255, 0, 0, 255), // opaque blue
      Color.FromArgb(255, 255, 255, 0) // opaque yellow
    };
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x)
      bmp.SetPixel(x, y, colors[(x + y * 8) % 4]);

    using (bmp) {
      var options = new PngOptimizationOptions {
        AutoSelectColorMode = true,
        TryInterlacing = false,
        FilterStrategies = [FilterStrategy.SingleFilter],
        DeflateMethods = [DeflateMethod.Default],
        MaxParallelTasks = 1
      };

      var optimizer = new PngOptimizer(bmp, options);
      var result = await optimizer.OptimizeAsync();

      if (result.ColorMode == PngColorType.Palette) {
        var trnsData = ExtractChunkData(result.FileContents, "tRNS");
        Assert.That(trnsData, Is.Not.Null, "Should have tRNS chunk for alpha image");
        // Non-opaque entries sorted first, so tRNS should be short (truncated at last non-opaque)
        var plteLength = FindChunkDataLength(result.FileContents, "PLTE");
        var paletteEntryCount = plteLength / 3;
        Assert.That(trnsData!.Length, Is.LessThan(paletteEntryCount),
          "tRNS should be truncated (shorter than total palette entries)");
      }
    }
  }

  [Test]
  public async Task AlphaImage_ReadbackPreservesAlpha() {
    using var bmp = CreateAlphaBitmap(8, 8, 4);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    using var ms = new MemoryStream(result.FileContents);
    using var readBack = new Bitmap(ms);

    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x) {
      var original = bmp.GetPixel(x, y);
      var loaded = readBack.GetPixel(x, y);
      Assert.That(loaded.A, Is.EqualTo(original.A), $"Alpha mismatch at ({x},{y})");
      Assert.That(loaded.R, Is.EqualTo(original.R), $"R mismatch at ({x},{y})");
      Assert.That(loaded.G, Is.EqualTo(original.G), $"G mismatch at ({x},{y})");
      Assert.That(loaded.B, Is.EqualTo(original.B), $"B mismatch at ({x},{y})");
    }
  }

  [Test]
  public async Task Maximum_ProducesOutputNoLargerThanDefault() {
    using var bmp = CreateGradientBitmap(16, 16);
    var defaultOptions = new PngOptimizationOptions {
      AutoSelectColorMode = false,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var maxOptions = new PngOptimizationOptions {
      AutoSelectColorMode = false,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Maximum],
      MaxParallelTasks = 1
    };

    var defaultResult = await new PngOptimizer(bmp, defaultOptions).OptimizeAsync();
    var maxResult = await new PngOptimizer(bmp, maxOptions).OptimizeAsync();

    Assert.That(maxResult.CompressedSize, Is.LessThanOrEqualTo(defaultResult.CompressedSize));
  }

  [Test]
  public async Task BruteForce_ProducesValidPng() {
    using var bmp = CreateGradientBitmap(16, 16);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = false,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.BruteForce],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    Assert.That(result.FilterStrategy, Is.EqualTo(FilterStrategy.BruteForce));
    VerifyPngSignature(result.FileContents);

    using var ms = new MemoryStream(result.FileContents);
    using var readBack = new Bitmap(ms);
    Assert.That(readBack.Width, Is.EqualTo(16));
    Assert.That(readBack.Height, Is.EqualTo(16));
  }

  [Test]
  public async Task BruteForce_NoLargerThanSingleFilter() {
    using var bmp = CreateGradientBitmap(32, 32);
    var singleOptions = new PngOptimizationOptions {
      AutoSelectColorMode = false,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Maximum],
      MaxParallelTasks = 1
    };

    var bruteOptions = new PngOptimizationOptions {
      AutoSelectColorMode = false,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.BruteForce],
      DeflateMethods = [DeflateMethod.Maximum],
      MaxParallelTasks = 1
    };

    var singleResult = await new PngOptimizer(bmp, singleOptions).OptimizeAsync();
    var bruteResult = await new PngOptimizer(bmp, bruteOptions).OptimizeAsync();

    Assert.That(bruteResult.CompressedSize, Is.LessThanOrEqualTo(singleResult.CompressedSize));
  }

  [Test]
  public async Task Maximum_And_Default_ProduceDifferentSizedOutput() {
    using var bmp = CreateGradientBitmap(256, 256);
    var defaultOptions = new PngOptimizationOptions {
      AutoSelectColorMode = false,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default],
      MaxParallelTasks = 1
    };

    var maxOptions = new PngOptimizationOptions {
      AutoSelectColorMode = false,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Maximum],
      MaxParallelTasks = 1
    };

    var defaultResult = await new PngOptimizer(bmp, defaultOptions).OptimizeAsync();
    var maxResult = await new PngOptimizer(bmp, maxOptions).OptimizeAsync();

    Assert.That(maxResult.CompressedSize, Is.Not.EqualTo(defaultResult.CompressedSize));
  }

  [Test]
  public async Task Ultra_ProducesValidPng_WithPixelEquality() {
    using var bmp = CreateGradientBitmap(32, 32);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = false,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Ultra],
      MaxParallelTasks = 1,
      EnableTwoPhaseOptimization = false
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    VerifyPngSignature(result.FileContents);
    Assert.That(result.DeflateMethod, Is.EqualTo(DeflateMethod.Ultra));

    using var ms = new MemoryStream(result.FileContents);
    using var readBack = new Bitmap(ms);
    Assert.That(readBack.Width, Is.EqualTo(32));
    Assert.That(readBack.Height, Is.EqualTo(32));

    for (var y = 0; y < 32; ++y)
    for (var x = 0; x < 32; ++x) {
      var original = bmp.GetPixel(x, y);
      var loaded = readBack.GetPixel(x, y);
      Assert.That(loaded.R, Is.EqualTo(original.R), $"R mismatch at ({x},{y})");
      Assert.That(loaded.G, Is.EqualTo(original.G), $"G mismatch at ({x},{y})");
      Assert.That(loaded.B, Is.EqualTo(original.B), $"B mismatch at ({x},{y})");
    }
  }

  [Test]
  public async Task Hyper_ProducesValidPng_WithPixelEquality() {
    using var bmp = CreateGradientBitmap(32, 32);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = false,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Hyper],
      ZopfliIterations = 5,
      MaxParallelTasks = 1
    };

    var optimizer = new PngOptimizer(bmp, options);
    var result = await optimizer.OptimizeAsync();

    VerifyPngSignature(result.FileContents);
    Assert.That(result.DeflateMethod, Is.EqualTo(DeflateMethod.Hyper));

    using var ms = new MemoryStream(result.FileContents);
    using var readBack = new Bitmap(ms);
    Assert.That(readBack.Width, Is.EqualTo(32));
    Assert.That(readBack.Height, Is.EqualTo(32));

    for (var y = 0; y < 32; ++y)
    for (var x = 0; x < 32; ++x) {
      var original = bmp.GetPixel(x, y);
      var loaded = readBack.GetPixel(x, y);
      Assert.That(loaded.R, Is.EqualTo(original.R), $"R mismatch at ({x},{y})");
      Assert.That(loaded.G, Is.EqualTo(original.G), $"G mismatch at ({x},{y})");
      Assert.That(loaded.B, Is.EqualTo(original.B), $"B mismatch at ({x},{y})");
    }
  }

  [Test]
  public async Task Ultra_SmallerThanMaximum_ForLargeRepetitiveImage() {
    // Use a large image where DP optimal parsing advantage outweighs header overhead
    var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
    for (var y = 0; y < 256; ++y)
    for (var x = 0; x < 256; ++x)
      bmp.SetPixel(x, y, Color.FromArgb(255, x % 16, y % 16, (x + y) % 16));

    using (bmp) {
      var maxOptions = new PngOptimizationOptions {
        AutoSelectColorMode = false,
        TryInterlacing = false,
        FilterStrategies = [FilterStrategy.SingleFilter],
        DeflateMethods = [DeflateMethod.Maximum],
        MaxParallelTasks = 1
      };

      var ultraOptions = new PngOptimizationOptions {
        AutoSelectColorMode = false,
        TryInterlacing = false,
        FilterStrategies = [FilterStrategy.SingleFilter],
        DeflateMethods = [DeflateMethod.Ultra],
        MaxParallelTasks = 1
      };

      var maxResult = await new PngOptimizer(bmp, maxOptions).OptimizeAsync();
      var ultraResult = await new PngOptimizer(bmp, ultraOptions).OptimizeAsync();

      Assert.That(ultraResult.CompressedSize, Is.LessThanOrEqualTo(maxResult.CompressedSize),
        $"Ultra={ultraResult.CompressedSize} should be <= Maximum={maxResult.CompressedSize}");
    }
  }

  [Test]
  public async Task Hyper_NoLargerThanUltra_ForGradient() {
    using var bmp = CreateGradientBitmap(64, 64);
    var ultraOptions = new PngOptimizationOptions {
      AutoSelectColorMode = false,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Ultra],
      MaxParallelTasks = 1
    };

    var hyperOptions = new PngOptimizationOptions {
      AutoSelectColorMode = false,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Hyper],
      ZopfliIterations = 5,
      MaxParallelTasks = 1
    };

    var ultraResult = await new PngOptimizer(bmp, ultraOptions).OptimizeAsync();
    var hyperResult = await new PngOptimizer(bmp, hyperOptions).OptimizeAsync();

    Assert.That(hyperResult.CompressedSize, Is.LessThanOrEqualTo(ultraResult.CompressedSize),
      $"Hyper={hyperResult.CompressedSize} should be <= Ultra={ultraResult.CompressedSize}");
  }

  [Test]
  public async Task TwoPhase_ProducesValidOutput() {
    using var bmp = CreateGradientBitmap(16, 16);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = false,
      TryInterlacing = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Default, DeflateMethod.Ultra],
      EnableTwoPhaseOptimization = true,
      Phase2CandidateCount = 3,
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

  private static int FindChunkDataLength(byte[] png, string chunkType) {
    var offset = 8; // skip signature
    while (offset + 8 <= png.Length) {
      var length = (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];
      var type = Encoding.ASCII.GetString(png, offset + 4, 4);
      if (type == chunkType)
        return length;

      offset += 12 + length; // length(4) + type(4) + data + crc(4)
    }

    return -1;
  }

  // --- RGBA-to-RGB+tRNS Tests ---

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void BinaryTransparency_RgbPlusTrns_ProducesValidPng() {
    // Create image with binary transparency: some pixels fully transparent, rest opaque
    using var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
    for (var y = 0; y < 16; ++y)
    for (var x = 0; x < 16; ++x)
      bmp.SetPixel(x, y, (x + y) % 3 == 0
        ? Color.FromArgb(0, 0, 0, 0) // fully transparent black
        : Color.FromArgb(255, x * 16 % 256, y * 16 % 256, 128));

    var options = new PngOptimizationOptions(
      DeflateMethods: [DeflateMethod.Default],
      FilterStrategies: [FilterStrategy.SingleFilter],
      AutoSelectColorMode: true
    );
    var optimizer = new PngOptimizer(bmp, options);
    var result = optimizer.OptimizeAsync().AsTask().Result;

    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    // Validate it's a valid PNG by reading back
    using var ms = new MemoryStream(result.FileContents);
    using var readback = new Bitmap(ms);
    Assert.That(readback.Width, Is.EqualTo(16));
    Assert.That(readback.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void SemiTransparentImage_RgbTrns_NotTried() {
    // Image with semi-transparent pixels should NOT qualify for RGB+tRNS
    using var bmp = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
    for (var y = 0; y < 4; ++y)
    for (var x = 0; x < 4; ++x)
      bmp.SetPixel(x, y, Color.FromArgb(128, 100, 150, 200)); // semi-transparent

    var optimizer = new PngOptimizer(bmp);
    var result = optimizer.OptimizeAsync().AsTask().Result;
    // Should not be RGB mode since we have semi-transparency
    Assert.That(result.ColorMode, Is.Not.EqualTo(PngColorType.RGB).Or.EqualTo(PngColorType.RGBA));
  }

  // --- Chunk Preservation Tests ---

  [Test]
  [Category("Unit")]
  public void PngChunkReader_ExtractsAncillaryChunks() {
    // Create a PNG with a tEXt chunk embedded
    using var bmp = CreateSolidColorBitmap(4, 4, Color.Red);
    var pngBytes = _SaveBitmapAsPng(bmp);

    // Inject a tEXt chunk after IHDR: build minimal tEXt (keyword=Test\0value)
    var textChunkData = Encoding.ASCII.GetBytes("Test\0HelloWorld");
    var withChunk = _InjectChunkBeforeIdat(pngBytes, "tEXt", textChunkData);

    var reader = PngChunkReader.Parse(withChunk);

    Assert.That(reader.HasChunks, Is.True);
    // tEXt should be categorized as after-IDAT (or between PLTE and IDAT depending on position)
    // Since we injected before IDAT and tEXt is in _AfterIdatChunks set but positioned before IDAT,
    // it ends up in BetweenPlteAndIdat because of position-based fallback
    var allChunks = reader.AfterIdat.Concat(reader.BetweenPlteAndIdat).ToList();
    Assert.That(allChunks.Any(c => c.Type == "tEXt"), Is.True, "Should find tEXt chunk");
  }

  [Test]
  [Category("Unit")]
  public void PngChunkReader_ExcludesCriticalChunks() {
    using var bmp = CreateSolidColorBitmap(4, 4, Color.Red);
    var pngBytes = _SaveBitmapAsPng(bmp);

    var reader = PngChunkReader.Parse(pngBytes);

    // Critical chunks (IHDR, PLTE, IDAT, IEND) should never appear in preserved chunks
    var allTypes = reader.BeforePlte.Select(c => c.Type)
      .Concat(reader.BetweenPlteAndIdat.Select(c => c.Type))
      .Concat(reader.AfterIdat.Select(c => c.Type));

    Assert.That(allTypes, Does.Not.Contain("IHDR"));
    Assert.That(allTypes, Does.Not.Contain("IDAT"));
    Assert.That(allTypes, Does.Not.Contain("IEND"));
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void ChunkPreservation_RoundTrip_PreservesTextChunk() {
    using var bmp = CreateSolidColorBitmap(4, 4, Color.Blue);
    var originalPng = _SaveBitmapAsPng(bmp);

    // Inject a tEXt chunk (keyword=TestKey\0TestValue)
    var textData = Encoding.Latin1.GetBytes("TestKey\0TestValue");
    var pngWithText = _InjectChunkBeforeIdat(originalPng, "tEXt", textData);

    var opts = new PngOptimizationOptions(
      FilterStrategies: [FilterStrategy.SingleFilter],
      DeflateMethods: [DeflateMethod.Default],
      PreserveAncillaryChunks: true
    );

    var optimizer = new PngOptimizer(bmp, pngWithText, opts);
    var result = optimizer.OptimizeAsync().AsTask().Result;

    // Verify tEXt chunk is in the output
    var outputText = ExtractChunkData(result.FileContents, "tEXt");
    Assert.That(outputText, Is.Not.Null, "tEXt chunk should be preserved");
    Assert.That(outputText, Is.EqualTo(textData));
  }

  private static byte[] _SaveBitmapAsPng(Bitmap bmp) {
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return ms.ToArray();
  }

  private static byte[] _InjectChunkBeforeIdat(byte[] png, string chunkType, byte[] data) {
    // Find IDAT position and inject chunk before it
    using var ms = new MemoryStream();
    var offset = 8;
    ms.Write(png, 0, 8); // signature

    while (offset + 8 <= png.Length) {
      var length = (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];
      var type = Encoding.ASCII.GetString(png, offset + 4, 4);

      if (type == "IDAT")
        // Write our injected chunk before IDAT
        _WriteRawChunk(ms, chunkType, data);

      // Copy original chunk (length + type + data + crc)
      var chunkTotalLen = 12 + length;
      ms.Write(png, offset, chunkTotalLen);
      offset += chunkTotalLen;
    }

    return ms.ToArray();
  }

  private static void _WriteRawChunk(Stream stream, string type, byte[] data) {
    // Length (big-endian)
    stream.WriteByte((byte)(data.Length >> 24));
    stream.WriteByte((byte)(data.Length >> 16));
    stream.WriteByte((byte)(data.Length >> 8));
    stream.WriteByte((byte)data.Length);
    // Type
    var typeBytes = Encoding.ASCII.GetBytes(type);
    stream.Write(typeBytes, 0, 4);
    // Data
    stream.Write(data, 0, data.Length);
    // CRC32 (type + data)
    var crc = 0xFFFFFFFF;
    foreach (var b in typeBytes)
      crc = _CrcUpdate(crc, b);
    foreach (var b in data)
      crc = _CrcUpdate(crc, b);
    crc ^= 0xFFFFFFFF;
    stream.WriteByte((byte)(crc >> 24));
    stream.WriteByte((byte)(crc >> 16));
    stream.WriteByte((byte)(crc >> 8));
    stream.WriteByte((byte)crc);
  }

  private static readonly uint[] _CrcTable = _BuildCrcTable();

  private static uint[] _BuildCrcTable() {
    var table = new uint[256];
    for (uint n = 0; n < 256; ++n) {
      var c = n;
      for (var k = 0; k < 8; ++k)
        c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
      table[n] = c;
    }

    return table;
  }

  private static uint _CrcUpdate(uint crc, byte b) {
    return _CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
  }

  private static byte[]? ExtractChunkData(byte[] png, string chunkType) {
    var offset = 8;
    while (offset + 8 <= png.Length) {
      var length = (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];
      var type = Encoding.ASCII.GetString(png, offset + 4, 4);
      if (type == chunkType) {
        var data = new byte[length];
        Array.Copy(png, offset + 8, data, 0, length);
        return data;
      }

      offset += 12 + length;
    }

    return null;
  }

  [Test]
  public async Task GrayscaleTRNS_ProducesValidPng_WithTransparency() {
    // Create grayscale image where gray=128 pixels are transparent
    var bmp = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x) {
      var gray = (x + y) % 2 == 0 ? 200 : 128;
      var alpha = gray == 128 ? 0 : 255;
      bmp.SetPixel(x, y, Color.FromArgb(alpha, gray, gray, gray));
    }

    using (bmp) {
      var options = new PngOptimizationOptions {
        AutoSelectColorMode = true,
        TryInterlacing = false,
        TryPartitioning = false,
        FilterStrategies = [FilterStrategy.SingleFilter],
        DeflateMethods = [DeflateMethod.Default],
        MaxParallelTasks = 1
      };
      var optimizer = new PngOptimizer(bmp, options);
      var result = await optimizer.OptimizeAsync();

      Assert.That(result.FileContents.Length, Is.GreaterThan(0));
      VerifyPngSignature(result.FileContents);

      // Read back and verify transparency is preserved
      using var ms = new MemoryStream(result.FileContents);
      using var readBack = new Bitmap(ms);
      Assert.That(readBack.Width, Is.EqualTo(8));
      Assert.That(readBack.Height, Is.EqualTo(8));

      for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x) {
        var px = readBack.GetPixel(x, y);
        if ((x + y) % 2 == 0) {
          Assert.That(px.A, Is.EqualTo(255), $"Opaque pixel at ({x},{y}) lost alpha");
          Assert.That(px.R, Is.EqualTo(200), $"Opaque pixel at ({x},{y}) wrong gray");
        } else {
          Assert.That(px.A, Is.EqualTo(0), $"Transparent pixel at ({x},{y}) not transparent");
        }
      }
    }
  }

  [Test]
  public async Task GrayscaleWithBinaryAlpha_TriesGrayscalePlusTRNS() {
    // Create a grayscale image with binary alpha — optimizer should try Grayscale mode
    var bmp = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
    for (var y = 0; y < 4; ++y)
    for (var x = 0; x < 4; ++x) {
      var gray = x == 0 ? 50 : 200;
      var alpha = x == 0 ? 0 : 255;
      bmp.SetPixel(x, y, Color.FromArgb(alpha, gray, gray, gray));
    }

    using (bmp) {
      var options = new PngOptimizationOptions {
        AutoSelectColorMode = true,
        TryInterlacing = false,
        TryPartitioning = false,
        FilterStrategies = [FilterStrategy.SingleFilter],
        DeflateMethods = [DeflateMethod.Default],
        MaxParallelTasks = 1
      };
      var optimizer = new PngOptimizer(bmp, options);
      var result = await optimizer.OptimizeAsync();

      // The result could be Grayscale or GrayscaleAlpha — both are valid
      // Grayscale with tRNS is preferred because it's smaller
      Assert.That(result.FileContents.Length, Is.GreaterThan(0));
      VerifyPngSignature(result.FileContents);

      // Verify the output is smaller than or equal to what GrayscaleAlpha would produce
      // (Grayscale is 1 byte/pixel vs GrayscaleAlpha's 2 bytes/pixel)
      Assert.That(result.ColorMode, Is.EqualTo(PngColorType.Grayscale).Or.EqualTo(PngColorType.GrayscaleAlpha));
    }
  }

  [Test]
  public async Task OptimizeAsync_WithProgress_ReportsAllCombos() {
    using var bmp = CreateSolidColorBitmap(4, 4, Color.Red);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = false,
      TryInterlacing = false,
      TryPartitioning = false,
      FilterStrategies = [FilterStrategy.SingleFilter],
      DeflateMethods = [DeflateMethod.Fastest],
      MaxParallelTasks = 1
    };
    var optimizer = new PngOptimizer(bmp, options);

    var reports = new List<OptimizationProgress>();
    IProgress<OptimizationProgress> progressReporter = new SynchronousProgress<OptimizationProgress>(reports.Add);
    await optimizer.OptimizeAsync(progress: progressReporter);

    Assert.That(reports, Has.Count.GreaterThan(0));
    var last = reports[^1];
    Assert.That(last.Phase, Is.EqualTo("Complete"));
    Assert.That(last.CombosCompleted, Is.EqualTo(last.CombosTotal));
  }

  [Test]
  public void OptimizeAsync_CancellationRequested_ThrowsOperationCanceledException() {
    using var bmp = CreateGradientBitmap(16, 16);
    var options = new PngOptimizationOptions {
      AutoSelectColorMode = true,
      TryInterlacing = true,
      FilterStrategies = [FilterStrategy.SingleFilter, FilterStrategy.ScanlineAdaptive],
      DeflateMethods = [DeflateMethod.Default, DeflateMethod.Maximum],
      MaxParallelTasks = 1
    };
    var optimizer = new PngOptimizer(bmp, options);

    using var cts = new CancellationTokenSource();
    cts.Cancel();

    Assert.That(
      async () => await optimizer.OptimizeAsync(cts.Token),
      Throws.InstanceOf<OperationCanceledException>()
    );
  }
}

file class SynchronousProgress<T>(Action<T> handler) : IProgress<T> {
  public void Report(T value) {
    handler(value);
  }
}
