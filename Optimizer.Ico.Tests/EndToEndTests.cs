using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileFormat.Ico;
using NUnit.Framework;
using FileFormat.Png;

namespace Optimizer.Ico.Tests;

[TestFixture]
public sealed class EndToEndTests {

  private static byte[] _CreateTestPng(int width, int height) {
    var pngFile = new PngFile {
      Width = width,
      Height = height,
      BitDepth = 8,
      ColorType = PngColorType.RGBA,
      PixelData = Enumerable.Range(0, height).Select(_ => new byte[width * 4]).ToArray()
    };
    return PngWriter.ToBytes(pngFile);
  }

  private static byte[] _CreateTestIcoBytes(params (int width, int height)[] entries) {
    var images = entries.Select(e => {
      var pngBytes = _CreateTestPng(e.width, e.height);
      return new IcoImage {
        Width = e.width,
        Height = e.height,
        BitsPerPixel = 32,
        Format = IcoImageFormat.Png,
        Data = pngBytes
      };
    }).ToArray();

    var icoFile = new IcoFile { Images = images };
    return IcoWriter.ToBytes(icoFile);
  }

  private static string _WriteToTempFile(byte[] data) {
    var path = Path.Combine(Path.GetTempPath(), $"icotest_{Guid.NewGuid():N}.ico");
    File.WriteAllBytes(path, data);
    return path;
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public async Task OptimizeAsync_SinglePngEntry_ProducesResult() {
    var icoBytes = _CreateTestIcoBytes((16, 16));
    var tempPath = _WriteToTempFile(icoBytes);
    try {
      var optimizer = IcoOptimizer.FromFile(new FileInfo(tempPath));
      var result = await optimizer.OptimizeAsync();

      Assert.That(result.FileContents, Is.Not.Null);
      Assert.That(result.FileContents.Length, Is.GreaterThan(0));
      Assert.That(result.CompressedSize, Is.GreaterThan(0));
      Assert.That(result.EntryFormats, Has.Length.EqualTo(1));
    } finally {
      File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public async Task OptimizeAsync_MultipleEntries_ProducesResult() {
    var icoBytes = _CreateTestIcoBytes((16, 16), (32, 32));
    var tempPath = _WriteToTempFile(icoBytes);
    try {
      var optimizer = IcoOptimizer.FromFile(new FileInfo(tempPath));
      var result = await optimizer.OptimizeAsync();

      Assert.That(result.FileContents, Is.Not.Null);
      Assert.That(result.FileContents.Length, Is.GreaterThan(0));
      Assert.That(result.EntryFormats, Has.Length.EqualTo(2));
    } finally {
      File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public async Task OptimizeAsync_ResultIsValidIco() {
    var icoBytes = _CreateTestIcoBytes((16, 16));
    var tempPath = _WriteToTempFile(icoBytes);
    try {
      var optimizer = IcoOptimizer.FromFile(new FileInfo(tempPath));
      var result = await optimizer.OptimizeAsync();

      var parsed = IcoReader.FromBytes(result.FileContents);
      Assert.That(parsed.Images.Count, Is.EqualTo(1));
      Assert.That(parsed.Images[0].Width, Is.EqualTo(16));
      Assert.That(parsed.Images[0].Height, Is.EqualTo(16));
    } finally {
      File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void OptimizeAsync_Cancellation_ThrowsOperationCanceledException() {
    var icoBytes = _CreateTestIcoBytes((16, 16));
    var tempPath = _WriteToTempFile(icoBytes);
    try {
      var optimizer = IcoOptimizer.FromFile(new FileInfo(tempPath));
      using var cts = new CancellationTokenSource();
      cts.Cancel();
      Assert.That(
        async () => await optimizer.OptimizeAsync(cts.Token),
        Throws.InstanceOf<OperationCanceledException>()
      );
    } finally {
      File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public async Task OptimizeAsync_ReportsProgress() {
    var icoBytes = _CreateTestIcoBytes((16, 16));
    var tempPath = _WriteToTempFile(icoBytes);
    try {
      var optimizer = IcoOptimizer.FromFile(new FileInfo(tempPath));
      var progressReported = false;
      var progress = new Progress<Crush.Core.OptimizationProgress>(_ => progressReported = true);

      await optimizer.OptimizeAsync(default, progress);

      // Give the Progress<T> callback time to fire (it posts to SynchronizationContext)
      await Task.Delay(100);
      Assert.That(progressReported, Is.True);
    } finally {
      File.Delete(tempPath);
    }
  }
}
