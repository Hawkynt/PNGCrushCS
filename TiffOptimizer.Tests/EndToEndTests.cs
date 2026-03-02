using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using BitMiracle.LibTiff.Classic;
using NUnit.Framework;

namespace TiffOptimizer.Tests;

[TestFixture]
public sealed class EndToEndTests {
  private static Bitmap _CreateTestBitmap(int width = 16, int height = 16) {
    var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      bmp.SetPixel(x, y, Color.FromArgb(255, x * 16 % 256, y * 16 % 256, (x + y) * 8 % 256));
    return bmp;
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_WriteThenReadBack_ProducesValidTiff() {
    using var bmp = _CreateTestBitmap();
    var optimizer = new TiffOptimizer(bmp, new TiffOptimizationOptions(
      [TiffCompression.None, TiffCompression.Deflate],
      [TiffPredictor.None],
      [16]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    var tempFile = Path.Combine(Path.GetTempPath(), $"tiff_e2e_{Guid.NewGuid():N}.tiff");
    try {
      File.WriteAllBytes(tempFile, result.FileContents);

      using var readBack = Tiff.Open(tempFile, "r");
      Assert.That(readBack, Is.Not.Null);

      var w = readBack!.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
      var h = readBack.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
      Assert.That(w, Is.EqualTo(16));
      Assert.That(h, Is.EqualTo(16));
    } finally {
      File.Delete(tempFile);
    }
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_MultipleCompressions_PicksSmallest() {
    using var bmp = _CreateTestBitmap(32, 32);
    var optimizer = new TiffOptimizer(bmp, new TiffOptimizationOptions(
      [TiffCompression.None, TiffCompression.PackBits, TiffCompression.Lzw, TiffCompression.Deflate],
      [TiffPredictor.None],
      [32]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;

    // Compressed result should be smaller than uncompressed
    Assert.That(result.Compression, Is.Not.EqualTo(TiffCompression.None));
    Assert.That(result.CompressedSize, Is.GreaterThan(0));
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_GrayscaleImage_DetectsGrayscale() {
    using var bmp = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x) {
      var g = (x + y) * 16 % 256;
      bmp.SetPixel(x, y, Color.FromArgb(255, g, g, g));
    }

    var optimizer = new TiffOptimizer(bmp, new TiffOptimizationOptions(
      [TiffCompression.Deflate],
      [TiffPredictor.None],
      [8],
      true
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_Tiled_WriteThenReadBack_ProducesValidTiff() {
    using var bmp = _CreateTestBitmap(64, 64);
    var optimizer = new TiffOptimizer(bmp, new TiffOptimizationOptions(
      [TiffCompression.Deflate],
      [TiffPredictor.None],
      TryTiles: true,
      TileSizes: [16, 32],
      DynamicStripSizing: false,
      StripRowCounts: [64]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    var tempFile = Path.Combine(Path.GetTempPath(), $"tiff_tile_e2e_{Guid.NewGuid():N}.tiff");
    try {
      File.WriteAllBytes(tempFile, result.FileContents);

      using var readBack = Tiff.Open(tempFile, "r");
      Assert.That(readBack, Is.Not.Null);

      var w = readBack!.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
      var h = readBack.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
      Assert.That(w, Is.EqualTo(64));
      Assert.That(h, Is.EqualTo(64));

      if (result.IsTiled) {
        var tw = readBack.GetField(TiffTag.TILEWIDTH);
        Assert.That(tw, Is.Not.Null, "Tiled result should have TILEWIDTH tag");
      }
    } finally {
      File.Delete(tempFile);
    }
  }

  [Test]
  public void OptimizeAsync_CancellationRequested_ThrowsOperationCanceledException() {
    using var bmp = _CreateTestBitmap();
    var optimizer = new TiffOptimizer(bmp, new TiffOptimizationOptions(
      [TiffCompression.Deflate],
      [TiffPredictor.None],
      [16]
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
    var nonExistentFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.tiff"));
    Assert.Throws<FileNotFoundException>(() => TiffOptimizer.FromFile(nonExistentFile));
  }

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => TiffOptimizer.FromFile(null!));
  }

  [Test]
  public void Constructor_NullBitmap_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => new TiffOptimizer(null!));
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(60000)]
  public void Optimize_AllCompressionTypes_AllProduceValidOutput() {
    using var bmp = _CreateTestBitmap(8, 8);

    foreach (var compression in new[]
               { TiffCompression.None, TiffCompression.PackBits, TiffCompression.Lzw, TiffCompression.Deflate }) {
      var optimizer = new TiffOptimizer(bmp, new TiffOptimizationOptions(
        [compression],
        [TiffPredictor.None],
        [8],
        false
      ));

      var result = optimizer.OptimizeAsync().AsTask().Result;
      Assert.That(result.FileContents.Length, Is.GreaterThan(0),
        $"Compression {compression} produced empty output");
    }
  }
}
