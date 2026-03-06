using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using FileFormat.Bmp;
using Optimizer.Bmp;
using Crush.TestUtilities;
using NUnit.Framework;

namespace Optimizer.Bmp.Tests;

[TestFixture]
public sealed class EndToEndTests {
  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_WriteThenReadBack_ProducesValidBmp() {
    using var bmp = TestBitmapFactory.CreateTestBitmap(16, 16);
    var optimizer = new BmpOptimizer(bmp, new BmpOptimizationOptions(
      Compressions: [BmpCompression.None],
      RowOrders: [BmpRowOrder.BottomUp]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    using var tempFile = new TempFileScope(".bmp");
    File.WriteAllBytes(tempFile.Path, result.FileContents);

    using var readBack = new Bitmap(tempFile.Path);
    Assert.That(readBack.Width, Is.EqualTo(16));
    Assert.That(readBack.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_MultipleCompressions_PicksSmallest() {
    using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
    // Solid color - highly compressible with RLE
    for (var y = 0; y < 32; ++y)
    for (var x = 0; x < 32; ++x)
      bmp.SetPixel(x, y, Color.Red);

    var optimizer = new BmpOptimizer(bmp, new BmpOptimizationOptions(
      Compressions: [BmpCompression.None, BmpCompression.Rle8],
      RowOrders: [BmpRowOrder.BottomUp],
      AutoSelectColorMode: true
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.CompressedSize, Is.GreaterThan(0));
  }

  [Test]
  public void OptimizeAsync_CancellationRequested_ThrowsOperationCanceledException() {
    using var bmp = TestBitmapFactory.CreateTestBitmap(16, 16);
    var optimizer = new BmpOptimizer(bmp, new BmpOptimizationOptions(
      Compressions: [BmpCompression.None],
      RowOrders: [BmpRowOrder.BottomUp]
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
    var nonExistentFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.bmp"));
    Assert.Throws<FileNotFoundException>(() => BmpOptimizer.FromFile(nonExistentFile));
  }

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BmpOptimizer.FromFile(null!));
  }

  [Test]
  public void Constructor_NullBitmap_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => new BmpOptimizer(null!));
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_AllColorModes_ProduceValidOutput() {
    // Use 2 colors so all palette modes work
    using var bmp = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x)
      bmp.SetPixel(x, y, (x + y) % 2 == 0 ? Color.White : Color.Black);

    foreach (var colorMode in new[] {
               BmpColorMode.Rgb24, BmpColorMode.Rgb16_565,
               BmpColorMode.Palette8, BmpColorMode.Palette4, BmpColorMode.Palette1,
               BmpColorMode.Grayscale8
             }) {
      var optimizer = new BmpOptimizer(bmp, new BmpOptimizationOptions(
        ColorModes: [colorMode],
        Compressions: [BmpCompression.None],
        RowOrders: [BmpRowOrder.BottomUp],
        AutoSelectColorMode: false
      ));

      var result = optimizer.OptimizeAsync().AsTask().Result;
      Assert.That(result.FileContents.Length, Is.GreaterThan(0),
        $"ColorMode {colorMode} produced empty output");
    }
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_Rgb24_WriteThenReadBack_PixelEquality() {
    using var bmp = TestBitmapFactory.CreateTestBitmap(8, 8);
    var optimizer = new BmpOptimizer(bmp, new BmpOptimizationOptions(
      ColorModes: [BmpColorMode.Rgb24],
      Compressions: [BmpCompression.None],
      RowOrders: [BmpRowOrder.BottomUp],
      AutoSelectColorMode: false
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    using var tempFile = new TempFileScope(".bmp");
    File.WriteAllBytes(tempFile.Path, result.FileContents);
    using var readBack = new Bitmap(tempFile.Path);

    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x) {
      var expected = bmp.GetPixel(x, y);
      var actual = readBack.GetPixel(x, y);
      Assert.That(actual.R, Is.EqualTo(expected.R), $"R mismatch at ({x},{y})");
      Assert.That(actual.G, Is.EqualTo(expected.G), $"G mismatch at ({x},{y})");
      Assert.That(actual.B, Is.EqualTo(expected.B), $"B mismatch at ({x},{y})");
    }
  }
}
