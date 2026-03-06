using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileFormat.Cur;
using FileFormat.Ico;
using NUnit.Framework;
using FileFormat.Png;

namespace Optimizer.Cur.Tests;

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

  private static byte[] _CreateTestCurBytes(params (int width, int height, ushort hotX, ushort hotY)[] entries) {
    var images = entries.Select(e => {
      var pngBytes = _CreateTestPng(e.width, e.height);
      return new CurImage {
        Width = e.width,
        Height = e.height,
        BitsPerPixel = 32,
        Format = IcoImageFormat.Png,
        Data = pngBytes,
        HotspotX = e.hotX,
        HotspotY = e.hotY
      };
    }).ToArray();

    var curFile = new CurFile { Images = images };
    return CurWriter.ToBytes(curFile);
  }

  private static string _WriteToTempFile(byte[] data) {
    var path = Path.Combine(Path.GetTempPath(), $"curtest_{Guid.NewGuid():N}.cur");
    File.WriteAllBytes(path, data);
    return path;
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public async Task OptimizeAsync_SingleEntry_ProducesResult() {
    var curBytes = _CreateTestCurBytes((16, 16, 5, 10));
    var tempPath = _WriteToTempFile(curBytes);
    try {
      var optimizer = CurOptimizer.FromFile(new FileInfo(tempPath));
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
  public async Task OptimizeAsync_ResultIsValidCur() {
    var curBytes = _CreateTestCurBytes((16, 16, 3, 7));
    var tempPath = _WriteToTempFile(curBytes);
    try {
      var optimizer = CurOptimizer.FromFile(new FileInfo(tempPath));
      var result = await optimizer.OptimizeAsync();

      var parsed = CurReader.FromBytes(result.FileContents);
      Assert.That(parsed.Images.Count, Is.EqualTo(1));
      Assert.That(parsed.Images[0].Width, Is.EqualTo(16));
      Assert.That(parsed.Images[0].Height, Is.EqualTo(16));
    } finally {
      File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public async Task OptimizeAsync_HotspotPreserved() {
    var curBytes = _CreateTestCurBytes((32, 32, 12, 24));
    var tempPath = _WriteToTempFile(curBytes);
    try {
      var optimizer = CurOptimizer.FromFile(new FileInfo(tempPath));
      var result = await optimizer.OptimizeAsync();

      var parsed = CurReader.FromBytes(result.FileContents);
      Assert.That(parsed.Images[0].HotspotX, Is.EqualTo(12));
      Assert.That(parsed.Images[0].HotspotY, Is.EqualTo(24));
    } finally {
      File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void OptimizeAsync_Cancellation_ThrowsOperationCanceledException() {
    var curBytes = _CreateTestCurBytes((16, 16, 0, 0));
    var tempPath = _WriteToTempFile(curBytes);
    try {
      var optimizer = CurOptimizer.FromFile(new FileInfo(tempPath));
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
}
