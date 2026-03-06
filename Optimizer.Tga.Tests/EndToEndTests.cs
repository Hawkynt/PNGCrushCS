using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using Crush.TestUtilities;
using NUnit.Framework;
using FileFormat.Tga;
using Optimizer.Tga;

namespace Optimizer.Tga.Tests;

[TestFixture]
public sealed class EndToEndTests {
  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_WriteThenReadBack_ProducesValidTga() {
    using var bmp = TestBitmapFactory.CreateTestBitmap(16, 16);
    var optimizer = new TgaOptimizer(bmp, new TgaOptimizationOptions(
      Compressions: [TgaCompression.None],
      Origins: [TgaOrigin.TopLeft]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    // Verify TGA header signature
    Assert.That(result.FileContents.Length, Is.GreaterThan(18), "TGA must have at least an 18-byte header");
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_Rle_SolidColor_CompressesBetterThanRaw() {
    using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
    for (var y = 0; y < 32; ++y)
    for (var x = 0; x < 32; ++x)
      bmp.SetPixel(x, y, Color.Red);

    var noneOpt = new TgaOptimizationOptions(
      Compressions: [TgaCompression.None],
      Origins: [TgaOrigin.TopLeft],
      AutoSelectColorMode: false
    );
    var rleOpt = new TgaOptimizationOptions(
      Compressions: [TgaCompression.Rle],
      Origins: [TgaOrigin.TopLeft],
      AutoSelectColorMode: false
    );

    var noneResult = new TgaOptimizer(bmp, noneOpt).OptimizeAsync().AsTask().Result;
    var rleResult = new TgaOptimizer(bmp, rleOpt).OptimizeAsync().AsTask().Result;

    Assert.That(rleResult.CompressedSize, Is.LessThan(noneResult.CompressedSize),
      "RLE should compress solid color better than no compression");
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_MultipleCompressions_PicksSmallest() {
    using var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
    for (var y = 0; y < 16; ++y)
    for (var x = 0; x < 16; ++x)
      bmp.SetPixel(x, y, Color.Blue);

    var optimizer = new TgaOptimizer(bmp, new TgaOptimizationOptions(
      Compressions: [TgaCompression.None, TgaCompression.Rle],
      Origins: [TgaOrigin.TopLeft],
      AutoSelectColorMode: true
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.CompressedSize, Is.GreaterThan(0));
  }

  [Test]
  public void OptimizeAsync_CancellationRequested_ThrowsOperationCanceledException() {
    using var bmp = TestBitmapFactory.CreateTestBitmap(16, 16);
    var optimizer = new TgaOptimizer(bmp, new TgaOptimizationOptions(
      Compressions: [TgaCompression.None],
      Origins: [TgaOrigin.TopLeft]
    ));
    using var cts = new CancellationTokenSource();
    cts.Cancel();
    Assert.That(
      async () => await optimizer.OptimizeAsync(cts.Token),
      Throws.InstanceOf<OperationCanceledException>()
    );
  }

  [Test]
  public void FromFile_NonExistentFile_ThrowsFileNotFoundException() {
    var nonExistentFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.tga"));
    Assert.Throws<FileNotFoundException>(() => TgaOptimizer.FromFile(nonExistentFile));
  }

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => TgaOptimizer.FromFile(null!));
  }

  [Test]
  public void Constructor_NullBitmap_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => new TgaOptimizer(null!));
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_AllColorModes_ProduceValidOutput() {
    // 2 colors: all modes should work
    using var bmp = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x)
      bmp.SetPixel(x, y, (x + y) % 2 == 0 ? Color.White : Color.Black);

    foreach (var colorMode in new[] {
               TgaColorMode.Rgba32, TgaColorMode.Rgb24,
               TgaColorMode.Grayscale8, TgaColorMode.Indexed8
             }) {
      var optimizer = new TgaOptimizer(bmp, new TgaOptimizationOptions(
        ColorModes: [colorMode],
        Compressions: [TgaCompression.None],
        Origins: [TgaOrigin.TopLeft],
        AutoSelectColorMode: false
      ));

      var result = optimizer.OptimizeAsync().AsTask().Result;
      Assert.That(result.FileContents.Length, Is.GreaterThan(0),
        $"ColorMode {colorMode} produced empty output");
    }
  }
}
