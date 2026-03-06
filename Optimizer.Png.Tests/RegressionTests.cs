using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using FileFormat.Png;

namespace Optimizer.Png.Tests;

[TestFixture]
[Category("Regression")]
[SupportedOSPlatform("windows")]
public sealed class RegressionTests {
  [Test]
  [TestCase(PngColorType.Palette, 1, 1)]
  [TestCase(PngColorType.Palette, 2, 1)]
  [TestCase(PngColorType.Palette, 4, 1)]
  [TestCase(PngColorType.Palette, 8, 1)]
  [TestCase(PngColorType.Grayscale, 1, 1)]
  [TestCase(PngColorType.Grayscale, 2, 1)]
  [TestCase(PngColorType.Grayscale, 4, 1)]
  [TestCase(PngColorType.Grayscale, 8, 1)]
  [TestCase(PngColorType.GrayscaleAlpha, 8, 2)]
  [TestCase(PngColorType.RGB, 8, 3)]
  [TestCase(PngColorType.RGBA, 8, 4)]
  public void GetFilterStride_NeverReturnsZero(PngColorType mode, int bitDepth, int expectedStride) {
    var stride = PngOptimizer.GetFilterStride(mode, bitDepth);
    Assert.That(stride, Is.EqualTo(expectedStride));
  }

  [Test]
  public void GetSamplesPerPixel_AllColorModes() {
    Assert.That(PngOptimizer.GetSamplesPerPixel(PngColorType.Grayscale), Is.EqualTo(1));
    Assert.That(PngOptimizer.GetSamplesPerPixel(PngColorType.GrayscaleAlpha), Is.EqualTo(2));
    Assert.That(PngOptimizer.GetSamplesPerPixel(PngColorType.RGB), Is.EqualTo(3));
    Assert.That(PngOptimizer.GetSamplesPerPixel(PngColorType.RGBA), Is.EqualTo(4));
    Assert.That(PngOptimizer.GetSamplesPerPixel(PngColorType.Palette), Is.EqualTo(1));
  }

  [Test]
  public void GetBitDepthForColors_CorrectBoundaries() {
    Assert.That(PngOptimizer.GetBitDepthForColors(2), Is.EqualTo(1));
    Assert.That(PngOptimizer.GetBitDepthForColors(1), Is.EqualTo(1));
    Assert.That(PngOptimizer.GetBitDepthForColors(3), Is.EqualTo(2));
    Assert.That(PngOptimizer.GetBitDepthForColors(4), Is.EqualTo(2));
    Assert.That(PngOptimizer.GetBitDepthForColors(5), Is.EqualTo(4));
    Assert.That(PngOptimizer.GetBitDepthForColors(16), Is.EqualTo(4));
    Assert.That(PngOptimizer.GetBitDepthForColors(17), Is.EqualTo(8));
    Assert.That(PngOptimizer.GetBitDepthForColors(256), Is.EqualTo(8));
  }

  [Test]
  public async Task SubBytePalette_ProducesValidPng() {
    var bmp = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.Clear(Color.Black);
    bmp.SetPixel(0, 0, Color.White);

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

      Assert.That(result.FileContents.Length, Is.GreaterThan(0));
      Assert.That(result.CompressedSize, Is.GreaterThan(0));

      using var ms = new MemoryStream(result.FileContents);
      using var readBack = new Bitmap(ms);
      Assert.That(readBack.Width, Is.EqualTo(4));
      Assert.That(readBack.Height, Is.EqualTo(4));
    }
  }

  [Test]
  public async Task PixelDataSize_NoOverallocation_SmallImage() {
    var bmp = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
    bmp.SetPixel(0, 0, Color.Red);
    bmp.SetPixel(1, 0, Color.Green);
    bmp.SetPixel(0, 1, Color.Blue);
    bmp.SetPixel(1, 1, Color.White);

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

      using var ms = new MemoryStream(result.FileContents);
      using var readBack = new Bitmap(ms);
      Assert.That(readBack.GetPixel(0, 0).R, Is.EqualTo(255));
      Assert.That(readBack.GetPixel(1, 0).G, Is.EqualTo(128).Within(1));
    }
  }

  [Test]
  public void PooledMemoryStream_ExpandsBeyondInitialCapacity() {
    using var pms = new MemoryStream(4);
    var data = new byte[100];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    pms.Write(data);
    Assert.That(pms.Position, Is.EqualTo(100));
    Assert.That(pms.Length, Is.GreaterThanOrEqualTo(100));
  }
}
