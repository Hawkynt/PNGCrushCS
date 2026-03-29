using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileFormat.Ani;
using FileFormat.Ico;
using FileFormat.Png;

namespace Optimizer.Ani.Tests;

[TestFixture]
public sealed class EndToEndTests {

  private static IcoFile _CreateTestIcoFrame(int width, int height) {
    var pngFile = new PngFile {
      Width = width, Height = height, BitDepth = 8, ColorType = PngColorType.RGBA,
      PixelData = Enumerable.Range(0, height).Select(_ => new byte[width * 4]).ToArray()
    };
    var pngBytes = PngWriter.ToBytes(pngFile);
    return new IcoFile {
      Images = [new IcoImage { Width = width, Height = height, BitsPerPixel = 32, Format = IcoImageFormat.Png, Data = pngBytes }]
    };
  }

  private static byte[] _CreateTestAniBytes(int frameCount) {
    var frames = Enumerable.Range(0, frameCount).Select(_ => _CreateTestIcoFrame(16, 16)).ToArray();
    var aniFile = new AniFile {
      Header = new AniHeader(
        CbSize: 36,
        NumFrames: frameCount,
        NumSteps: frameCount,
        Width: 16,
        Height: 16,
        BitCount: 32,
        NumPlanes: 1,
        DisplayRate: 10,
        Flags: 2
      ),
      Frames = frames
    };
    return AniWriter.ToBytes(aniFile);
  }

  private static string _WriteToTempFile(byte[] data) {
    var path = Path.Combine(Path.GetTempPath(), $"anitest_{Guid.NewGuid():N}.ani");
    File.WriteAllBytes(path, data);
    return path;
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public async Task OptimizeAsync_SingleFrame_ProducesResult() {
    var aniBytes = _CreateTestAniBytes(1);
    var tempPath = _WriteToTempFile(aniBytes);
    try {
      var optimizer = AniOptimizer.FromFile(new FileInfo(tempPath));
      var result = await optimizer.OptimizeAsync();

      Assert.That(result.FileContents, Is.Not.Null);
      Assert.That(result.FileContents.Length, Is.GreaterThan(0));
      Assert.That(result.CompressedSize, Is.GreaterThan(0));
    } finally {
      File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public async Task OptimizeAsync_MultipleFrames_ProducesResult() {
    var aniBytes = _CreateTestAniBytes(3);
    var tempPath = _WriteToTempFile(aniBytes);
    try {
      var optimizer = AniOptimizer.FromFile(new FileInfo(tempPath));
      var result = await optimizer.OptimizeAsync();

      Assert.That(result.FileContents, Is.Not.Null);
      Assert.That(result.FileContents.Length, Is.GreaterThan(0));
    } finally {
      File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  [CancelAfter(30000)]
  public async Task OptimizeAsync_ResultIsValidAni() {
    var aniBytes = _CreateTestAniBytes(2);
    var tempPath = _WriteToTempFile(aniBytes);
    try {
      var optimizer = AniOptimizer.FromFile(new FileInfo(tempPath));
      var result = await optimizer.OptimizeAsync();

      var parsed = AniReader.FromBytes(result.FileContents);
      Assert.That(parsed.Header.NumFrames, Is.EqualTo(2));
      Assert.That(parsed.Frames.Count, Is.EqualTo(2));
    } finally {
      File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void OptimizeAsync_Cancellation_ThrowsOperationCanceledException() {
    var aniBytes = _CreateTestAniBytes(1);
    var tempPath = _WriteToTempFile(aniBytes);
    try {
      var optimizer = AniOptimizer.FromFile(new FileInfo(tempPath));
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
