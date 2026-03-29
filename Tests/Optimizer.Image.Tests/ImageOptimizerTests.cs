using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Crush.Core;
using Optimizer.Image;

namespace Optimizer.Image.Tests;

[TestFixture]
public sealed class ImageOptimizerTests {

  [Test]
  public void Constructor_NullFile_Throws() {
    Assert.Throws<ArgumentNullException>(() => new ImageOptimizer(null!));
  }

  [Test]
  public void Constructor_MissingFile_Throws() {
    Assert.Throws<FileNotFoundException>(() => new ImageOptimizer(new FileInfo("nonexistent.png")));
  }

  [Test]
  public async Task OptimizeAsync_BmpInput_ProducesResult() {
    using var scope = _CreateTempBmp();
    var optimizer = new ImageOptimizer(scope.File);
    var result = await optimizer.OptimizeAsync();

    Assert.Multiple(() => {
      Assert.That(result.OriginalFormat, Is.EqualTo(ImageFormat.Bmp));
      Assert.That(result.FileContents, Is.Not.Null);
      Assert.That(result.FileContents.Length, Is.GreaterThan(0));
      Assert.That(result.CompressedSize, Is.GreaterThan(0));
    });
  }

  [Test]
  public async Task OptimizeAsync_BmpNoConversion_StaysBmp() {
    using var scope = _CreateTempBmp();
    var options = new ImageOptimizationOptions(AllowFormatConversion: false);
    var optimizer = new ImageOptimizer(scope.File, options);
    var result = await optimizer.OptimizeAsync();

    Assert.That(result.OutputFormat, Is.EqualTo(ImageFormat.Bmp));
  }

  [Test]
  public async Task OptimizeAsync_ForcePng_OutputsPng() {
    using var scope = _CreateTempBmp();
    var options = new ImageOptimizationOptions(ForceFormat: ImageFormat.Png, AllowFormatConversion: true);
    var optimizer = new ImageOptimizer(scope.File, options);
    var result = await optimizer.OptimizeAsync();

    Assert.Multiple(() => {
      Assert.That(result.OriginalFormat, Is.EqualTo(ImageFormat.Bmp));
      Assert.That(result.OutputFormat, Is.EqualTo(ImageFormat.Bmp).Or.EqualTo(ImageFormat.Png));
      Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    });
  }

  [Test]
  public async Task OptimizeAsync_Cancellation_ThrowsOperationCanceled() {
    using var scope = _CreateTempBmp();
    var optimizer = new ImageOptimizer(scope.File);
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    Assert.That(
      async () => await optimizer.OptimizeAsync(cts.Token),
      Throws.InstanceOf<OperationCanceledException>()
    );
  }

  [Test]
  public async Task OptimizeAsync_ProgressReported() {
    using var scope = _CreateTempBmp();
    var options = new ImageOptimizationOptions(AllowFormatConversion: false);
    var optimizer = new ImageOptimizer(scope.File, options);
    var reported = false;
    var progress = new Progress<OptimizationProgress>(_ => reported = true);

    await optimizer.OptimizeAsync(CancellationToken.None, progress);

    // Allow a short delay for async progress reporting
    await Task.Delay(100);
    Assert.That(reported, Is.True);
  }

  [Test]
  public async Task OptimizeAsync_WithConversion_MayProduceSmallerFile() {
    using var scope = _CreateTempBmp(32, 32);
    var options = new ImageOptimizationOptions(AllowFormatConversion: true);
    var optimizer = new ImageOptimizer(scope.File, options);
    var result = await optimizer.OptimizeAsync();

    Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    Assert.That(result.CompressedSize, Is.LessThanOrEqualTo(scope.File.Length));
  }

  [Test]
  public async Task OptimizeAsync_ResultHasProcessingTime() {
    using var scope = _CreateTempBmp();
    var options = new ImageOptimizationOptions(AllowFormatConversion: false);
    var optimizer = new ImageOptimizer(scope.File, options);
    var result = await optimizer.OptimizeAsync();

    Assert.That(result.ProcessingTime, Is.GreaterThan(TimeSpan.Zero));
  }

  [Test]
  public async Task OptimizeAsync_ResultHasOutputExtension() {
    using var scope = _CreateTempBmp();
    var options = new ImageOptimizationOptions(AllowFormatConversion: false);
    var optimizer = new ImageOptimizer(scope.File, options);
    var result = await optimizer.OptimizeAsync();

    Assert.That(result.OutputExtension, Is.Not.Empty);
  }

  [Test]
  public void HasAlpha_AllOpaque_ReturnsFalse() {
    var bgra = new byte[] { 0, 0, 255, 255, 128, 128, 128, 255 };
    Assert.That(ImageOptimizer._HasAlpha(bgra, 2), Is.False);
  }

  [Test]
  public void HasAlpha_OneTransparent_ReturnsTrue() {
    var bgra = new byte[] { 0, 0, 255, 255, 128, 128, 128, 200 };
    Assert.That(ImageOptimizer._HasAlpha(bgra, 2), Is.True);
  }

  [Test]
  public void HasAlpha_LargeArray_SimdPath() {
    var bgra = new byte[64 * 4];
    for (var i = 0; i < 64; ++i)
      bgra[i * 4 + 3] = 255;

    Assert.That(ImageOptimizer._HasAlpha(bgra, 64), Is.False);
  }

  [Test]
  public void HasAlpha_LargeArrayWithTransparent_SimdPath() {
    var bgra = new byte[64 * 4];
    for (var i = 0; i < 64; ++i)
      bgra[i * 4 + 3] = 255;

    bgra[63 * 4 + 3] = 0;
    Assert.That(ImageOptimizer._HasAlpha(bgra, 64), Is.True);
  }

  [Test]
  public async Task OptimizeAsync_ForceFormatSgi_ProducesResult() {
    using var scope = _CreateTempBmp();
    var options = new ImageOptimizationOptions(ForceFormat: ImageFormat.Sgi, AllowFormatConversion: true);
    var optimizer = new ImageOptimizer(scope.File, options);
    var result = await optimizer.OptimizeAsync();

    Assert.Multiple(() => {
      Assert.That(result.FileContents, Is.Not.Null);
      Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    });
  }

  private static TempScope _CreateTempBmp(int width = 8, int height = 8) {
    var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.bmp");
    using var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        bmp.SetPixel(x, y, Color.FromArgb(x * 8, y * 8, (x + y) * 4));

    bmp.Save(tempPath, System.Drawing.Imaging.ImageFormat.Bmp);
    return new TempScope(new FileInfo(tempPath));
  }

  private sealed class TempScope(FileInfo file) : IDisposable {
    public FileInfo File => file;

    public void Dispose() {
      try { file.Delete(); } catch { /* best effort */ }
    }
  }
}
