using System;
using System.IO;
using FileFormat.Sixel;

namespace FileFormat.Sixel.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_1x6() {
    var original = new SixelFile {
      Width = 1,
      Height = 6,
      PixelData = new byte[6],
      Palette = [255, 0, 0],
      PaletteColorCount = 1,
      AspectRatio = 0,
      BackgroundMode = 0
    };

    var bytes = SixelWriter.ToBytes(original);
    var restored = SixelReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Has.Length.EqualTo(original.PixelData.Length));
      for (var i = 0; i < original.PixelData.Length; ++i)
        Assert.That(restored.PixelData[i], Is.EqualTo(original.PixelData[i]), $"Pixel mismatch at {i}");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiColor() {
    var pixelData = new byte[12];
    for (var i = 0; i < 12; ++i)
      pixelData[i] = (byte)(i % 2);

    var original = new SixelFile {
      Width = 2,
      Height = 6,
      PixelData = pixelData,
      Palette = [255, 0, 0, 0, 255, 0],
      PaletteColorCount = 2,
      AspectRatio = 0,
      BackgroundMode = 0
    };

    var bytes = SixelWriter.ToBytes(original);
    var restored = SixelReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      for (var i = 0; i < original.PixelData.Length; ++i)
        Assert.That(restored.PixelData[i], Is.EqualTo(original.PixelData[i]), $"Pixel mismatch at {i}");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiBand() {
    var pixelData = new byte[24];
    for (var i = 0; i < 24; ++i)
      pixelData[i] = (byte)(i % 3);

    var original = new SixelFile {
      Width = 2,
      Height = 12,
      PixelData = pixelData,
      Palette = [255, 0, 0, 0, 255, 0, 0, 0, 255],
      PaletteColorCount = 3,
      AspectRatio = 0,
      BackgroundMode = 0
    };

    var bytes = SixelWriter.ToBytes(original);
    var restored = SixelReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      for (var i = 0; i < original.PixelData.Length; ++i)
        Assert.That(restored.PixelData[i], Is.EqualTo(original.PixelData[i]), $"Pixel mismatch at {i}");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[12];
    for (var i = 0; i < 12; ++i)
      pixelData[i] = (byte)(i % 2);

    var original = new SixelFile {
      Width = 2,
      Height = 6,
      PixelData = pixelData,
      Palette = [255, 0, 0, 0, 0, 255],
      PaletteColorCount = 2,
      AspectRatio = 0,
      BackgroundMode = 0
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".six");
    try {
      var bytes = SixelWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = SixelReader.FromFile(new FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(original.Width));
        Assert.That(restored.Height, Is.EqualTo(original.Height));
        for (var i = 0; i < original.PixelData.Length; ++i)
          Assert.That(restored.PixelData[i], Is.EqualTo(original.PixelData[i]));
      });
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
