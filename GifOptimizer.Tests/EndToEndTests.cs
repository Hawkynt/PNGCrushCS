using System;
using System.IO;
using System.Threading;
using Hawkynt.GifFileFormat;
using NUnit.Framework;

namespace GifOptimizer.Tests;

[TestFixture]
public sealed class EndToEndTests {
  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_SingleFrame_ProducesValidGif() {
    var tempFile = _CreateTestGifFile(4, 4, 1);
    try {
      var optimizer = GifOptimizer.FromFile(tempFile, new GifOptimizationOptions(
        [PaletteReorderStrategy.Original, PaletteReorderStrategy.FrequencySorted]
      ));

      var result = optimizer.OptimizeAsync().AsTask().Result;

      Assert.That(result.FileContents.Length, Is.GreaterThan(0));
      Assert.That(result.FrameCount, Is.EqualTo(1));

      using var ms = new MemoryStream(result.FileContents);
      var readBack = Reader.FromStream(ms);
      Assert.That(readBack.Frames.Count, Is.EqualTo(1));
      Assert.That(readBack.Frames[0].Size.Width, Is.EqualTo(4));
      Assert.That(readBack.Frames[0].Size.Height, Is.EqualTo(4));
    } finally {
      tempFile.Delete();
    }
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_MultiFrame_PreservesFrameCount() {
    var tempFile = _CreateTestGifFile(4, 4, 3);
    try {
      var optimizer = GifOptimizer.FromFile(tempFile, new GifOptimizationOptions(
        [PaletteReorderStrategy.Original]
      ));

      var result = optimizer.OptimizeAsync().AsTask().Result;

      Assert.That(result.FrameCount, Is.EqualTo(3));

      using var ms = new MemoryStream(result.FileContents);
      var readBack = Reader.FromStream(ms);
      Assert.That(readBack.Frames.Count, Is.EqualTo(3));
    } finally {
      tempFile.Delete();
    }
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_WithTransparency_PreservesTransparentIndex() {
    var tempFile = _CreateTestGifFile(4, 4, 1, true);
    try {
      var optimizer = GifOptimizer.FromFile(tempFile, new GifOptimizationOptions(
        [PaletteReorderStrategy.Original],
        TrimMargins: false
      ));

      var result = optimizer.OptimizeAsync().AsTask().Result;

      using var ms = new MemoryStream(result.FileContents);
      var readBack = Reader.FromStream(ms);
      Assert.That(readBack.Frames[0].TransparentColorIndex, Is.Not.Null);
    } finally {
      tempFile.Delete();
    }
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(30000)]
  public void Optimize_PixelDataPreserved() {
    var tempFile = _CreateTestGifFile(4, 4, 1);
    try {
      var originalGif = Reader.FromFile(tempFile);
      var originalPixels = originalGif.Frames[0].IndexedPixels;
      var originalPalette = originalGif.GlobalColorTable!;

      var optimizer = GifOptimizer.FromFile(tempFile, new GifOptimizationOptions(
        [PaletteReorderStrategy.Original]
      ));

      var result = optimizer.OptimizeAsync().AsTask().Result;

      using var ms = new MemoryStream(result.FileContents);
      var optimized = Reader.FromStream(ms);
      var optimizedPixels = optimized.Frames[0].IndexedPixels;
      var optimizedPalette = optimized.Frames[0].LocalColorTable ?? optimized.GlobalColorTable;

      Assert.That(optimizedPixels.Length, Is.EqualTo(originalPixels.Length));

      for (var i = 0; i < originalPixels.Length; ++i) {
        var origColor = originalPalette[originalPixels[i]].ToArgb();
        var optColor = optimizedPalette![optimizedPixels[i]].ToArgb();
        Assert.That(optColor, Is.EqualTo(origColor), $"Color mismatch at pixel {i}");
      }
    } finally {
      tempFile.Delete();
    }
  }

  [Test]
  [Category("EndToEnd")]
  [CancelAfter(60000)]
  public void Optimize_AllStrategies_AllProduceValidOutput() {
    var tempFile = _CreateTestGifFile(8, 8, 2);
    try {
      foreach (var strategy in Enum.GetValues<PaletteReorderStrategy>()) {
        var optimizer = GifOptimizer.FromFile(tempFile, new GifOptimizationOptions(
          [strategy]
        ));

        var result = optimizer.OptimizeAsync().AsTask().Result;
        Assert.That(result.FileContents.Length, Is.GreaterThan(0),
          $"Strategy {strategy} produced empty output");

        using var ms = new MemoryStream(result.FileContents);
        var readBack = Reader.FromStream(ms);
        Assert.That(readBack.Frames.Count, Is.EqualTo(2), $"Strategy {strategy} lost frames");
      }
    } finally {
      tempFile.Delete();
    }
  }

  [Test]
  public void FromFile_NonExistentFile_ThrowsFileNotFoundException() {
    var nonExistentFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.gif"));
    Assert.Throws<FileNotFoundException>(() => GifOptimizer.FromFile(nonExistentFile));
  }

  [Test]
  public void FromFile_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GifOptimizer.FromFile(null!));
  }

  [Test]
  public void FromFile_CorruptFile_ThrowsDescriptiveException() {
    var tempFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"corrupt_{Guid.NewGuid():N}.gif"));
    try {
      File.WriteAllBytes(tempFile.FullName, [0x00, 0x01, 0x02, 0x03]);
      Assert.Throws<InvalidOperationException>(() => GifOptimizer.FromFile(tempFile));
    } finally {
      if (tempFile.Exists) tempFile.Delete();
    }
  }

  [Test]
  public void OptimizeAsync_CancellationRequested_ThrowsOperationCanceledException() {
    var tempFile = _CreateTestGifFile(4, 4, 2);
    try {
      var optimizer = GifOptimizer.FromFile(tempFile);
      using var cts = new CancellationTokenSource();
      cts.Cancel();
      Assert.That(
        async () => await optimizer.OptimizeAsync(cts.Token),
        Throws.InstanceOf<OperationCanceledException>()
      );
    } finally {
      tempFile.Delete();
    }
  }

  private static FileInfo _CreateTestGifFile(int width, int height, int frameCount, bool useTransparency = false) {
    var tempFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"e2e_{Guid.NewGuid():N}.gif"));

    using var ms = new MemoryStream();
    using var writer = new BinaryWriter(ms);

    writer.Write("GIF89a"u8);
    writer.Write((ushort)width);
    writer.Write((ushort)height);
    writer.Write((byte)0xF1); // GCT flag, 4-entry GCT
    writer.Write((byte)0);
    writer.Write((byte)0);

    // 4-entry GCT
    writer.Write(new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255 });

    if (frameCount > 1) {
      writer.Write((byte)0x21);
      writer.Write((byte)0xFF);
      writer.Write((byte)0x0B);
      writer.Write("NETSCAPE"u8);
      writer.Write("2.0"u8);
      writer.Write((byte)0x03);
      writer.Write((byte)0x01);
      writer.Write((ushort)0);
      writer.Write((byte)0x00);
    }

    for (var f = 0; f < frameCount; ++f) {
      writer.Write((byte)0x21);
      writer.Write((byte)0xF9);
      writer.Write((byte)0x04);
      var gcePacked = (byte)((f == 0 ? 0 : 2) << 2);
      if (useTransparency)
        gcePacked |= 0x01;
      writer.Write(gcePacked);
      writer.Write((ushort)10);
      writer.Write((byte)0);
      writer.Write((byte)0x00);

      writer.Write((byte)0x2C);
      writer.Write((ushort)0);
      writer.Write((ushort)0);
      writer.Write((ushort)width);
      writer.Write((ushort)height);
      writer.Write((byte)0x00);

      var pixels = new byte[width * height];
      for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        pixels[y * width + x] = (byte)((x + y + f) % 4);

      byte minCodeSize = 2;
      var compressed = LzwCompressor.Compress(pixels, minCodeSize);
      writer.Write(minCodeSize);
      var offset = 0;
      while (offset < compressed.Length) {
        var blockSize = Math.Min(255, compressed.Length - offset);
        writer.Write((byte)blockSize);
        writer.Write(compressed, offset, blockSize);
        offset += blockSize;
      }

      writer.Write((byte)0x00);
    }

    writer.Write((byte)0x3B);
    writer.Flush();

    File.WriteAllBytes(tempFile.FullName, ms.ToArray());
    return tempFile;
  }
}
