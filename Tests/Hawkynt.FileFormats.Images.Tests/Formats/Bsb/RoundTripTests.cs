using System;
using System.IO;
using FileFormat.Bsb;
using FileFormat.Core;

namespace FileFormat.Bsb.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_1x1_SingleColor() {
    var original = new BsbFile {
      Width = 1,
      Height = 1,
      PixelData = [0],
      Palette = [255, 0, 0],
      PaletteCount = 1,
      Depth = 7,
      Name = "Test",
    };

    var bytes = BsbWriter.ToBytes(original);
    var restored = BsbReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.PaletteCount, Is.EqualTo(original.PaletteCount));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleColors() {
    var original = new BsbFile {
      Width = 4,
      Height = 2,
      PixelData = [0, 1, 2, 3, 3, 2, 1, 0],
      Palette = [255, 0, 0, 0, 255, 0, 0, 0, 255, 128, 128, 128],
      PaletteCount = 4,
      Depth = 7,
      Name = "ColorTest",
    };

    var bytes = BsbWriter.ToBytes(original);
    var restored = BsbReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Name, Is.EqualTo(original.Name));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllSameColor() {
    var width = 8;
    var height = 4;
    var pixels = new byte[width * height];

    var original = new BsbFile {
      Width = width,
      Height = height,
      PixelData = pixels,
      Palette = [64, 128, 192],
      PaletteCount = 1,
      Depth = 7,
    };

    var bytes = BsbWriter.ToBytes(original);
    var restored = BsbReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PalettePreserved() {
    var original = new BsbFile {
      Width = 2,
      Height = 1,
      PixelData = [0, 1],
      Palette = [10, 20, 30, 40, 50, 60],
      PaletteCount = 2,
      Depth = 7,
    };

    var bytes = BsbWriter.ToBytes(original);
    var restored = BsbReader.FromBytes(bytes);

    Assert.That(restored.Palette[0], Is.EqualTo(10));
    Assert.That(restored.Palette[1], Is.EqualTo(20));
    Assert.That(restored.Palette[2], Is.EqualTo(30));
    Assert.That(restored.Palette[3], Is.EqualTo(40));
    Assert.That(restored.Palette[4], Is.EqualTo(50));
    Assert.That(restored.Palette[5], Is.EqualTo(60));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".kap");
    try {
      var original = new BsbFile {
        Width = 3,
        Height = 2,
        PixelData = [0, 1, 0, 1, 0, 1],
        Palette = [255, 0, 0, 0, 255, 0],
        PaletteCount = 2,
        Depth = 7,
        Name = "FileTest",
      };

      var bytes = BsbWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = BsbReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var original = new BsbFile {
      Width = 2,
      Height = 2,
      PixelData = [0, 1, 1, 0],
      Palette = [100, 150, 200, 50, 75, 25],
      PaletteCount = 2,
      Depth = 7,
    };

    var raw = BsbFile.ToRawImage(original);
    var fromRaw = BsbFile.FromRawImage(raw);

    Assert.That(fromRaw.Width, Is.EqualTo(original.Width));
    Assert.That(fromRaw.Height, Is.EqualTo(original.Height));
    Assert.That(fromRaw.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(fromRaw.PaletteCount, Is.EqualTo(original.PaletteCount));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var width = 16;
    var height = 8;
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 4);

    var original = new BsbFile {
      Width = width,
      Height = height,
      PixelData = pixels,
      Palette = [0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255],
      PaletteCount = 4,
      Depth = 7,
    };

    var bytes = BsbWriter.ToBytes(original);
    var restored = BsbReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
