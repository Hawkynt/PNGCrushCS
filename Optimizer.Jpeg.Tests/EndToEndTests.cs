using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using Crush.TestUtilities;
using FileFormat.Jpeg;
using NUnit.Framework;
using Optimizer.Jpeg;

namespace Optimizer.Jpeg.Tests;

[TestFixture]
public sealed class EndToEndTests {
  private static byte[] _CreateTestJpeg(int width = 16, int height = 16, int quality = 90) {
    using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      bmp.SetPixel(x, y, Color.FromArgb(255, x * 16 % 256, y * 16 % 256, (x + y) * 8 % 256));

    using var ms = new MemoryStream();
    var encoder = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
    var encoderParams = new EncoderParameters(1) {
      Param = { [0] = new EncoderParameter(Encoder.Quality, (long)quality) }
    };
    bmp.Save(ms, encoder, encoderParams);
    return ms.ToArray();
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_Lossless_WriteThenReadBack_ProducesValidJpeg() {
    var jpegBytes = _CreateTestJpeg();
    using var tempInput = new TempFileScope(".jpg");
    using var tempOutput = new TempFileScope(".jpg");
    File.WriteAllBytes(tempInput.Path, jpegBytes);

    var optimizer = JpegOptimizer.FromFile(new FileInfo(tempInput.Path), new JpegOptimizationOptions(
      AllowLossy: false,
      StripMetadata: true
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    File.WriteAllBytes(tempOutput.Path, result.FileContents);

    using var readBack = new Bitmap(tempOutput.Path);
    Assert.That(readBack.Width, Is.EqualTo(16));
    Assert.That(readBack.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_Lossy_ProducesValidJpeg() {
    using var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
    for (var y = 0; y < 16; ++y)
    for (var x = 0; x < 16; ++x)
      bmp.SetPixel(x, y, Color.FromArgb(255, x * 16, y * 16, 128));

    var optimizer = new JpegOptimizer(bmp, new JpegOptimizationOptions(
      AllowLossy: true,
      MinQuality: 75,
      Qualities: [80, 90],
      Subsamplings: [JpegSubsampling.Chroma444]
    ));

    var result = optimizer.OptimizeAsync().AsTask().Result;
    using var tempFile = new TempFileScope(".jpg");
    File.WriteAllBytes(tempFile.Path, result.FileContents);
    using var readBack = new Bitmap(tempFile.Path);
    Assert.That(readBack.Width, Is.EqualTo(16));
    Assert.That(readBack.Height, Is.EqualTo(16));
  }

  [Test]
  public void OptimizeAsync_CancellationRequested_ThrowsOperationCanceledException() {
    using var bmp = new Bitmap(8, 8, PixelFormat.Format32bppArgb);
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x)
      bmp.SetPixel(x, y, Color.Red);

    var optimizer = new JpegOptimizer(bmp, new JpegOptimizationOptions(
      AllowLossy: true,
      Qualities: [90]
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
    var nonExistentFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.jpg"));
    Assert.Throws<FileNotFoundException>(() => JpegOptimizer.FromFile(nonExistentFile));
  }

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JpegOptimizer.FromFile(null!));
  }

  [Test]
  public void Constructor_NullBitmap_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => new JpegOptimizer(null!));
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_Lossy_AllSubsamplings_ProduceValidOutput() {
    using var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
    for (var y = 0; y < 16; ++y)
    for (var x = 0; x < 16; ++x)
      bmp.SetPixel(x, y, Color.FromArgb(255, x * 16, y * 16, 128));

    foreach (var sub in new[] { JpegSubsampling.Chroma444, JpegSubsampling.Chroma420 }) {
      var optimizer = new JpegOptimizer(bmp, new JpegOptimizationOptions(
        AllowLossy: true,
        Modes: [JpegMode.Baseline],
        Qualities: [85],
        Subsamplings: [sub],
        MaxParallelTasks: 1
      ));

      var result = optimizer.OptimizeAsync().AsTask().Result;
      Assert.That(result.FileContents.Length, Is.GreaterThan(0),
        $"Subsampling {sub} produced empty output");
    }
  }
}
