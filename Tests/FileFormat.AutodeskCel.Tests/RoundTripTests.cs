using System;
using System.IO;
using FileFormat.AutodeskCel;
using FileFormat.Core;

namespace FileFormat.AutodeskCel.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SimpleIndexed_PixelDataPreserved() {
    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new AutodeskCelFile {
      Width = 4,
      Height = 3,
      PixelData = pixelData,
    };

    var bytes = AutodeskCelWriter.ToBytes(original);
    var restored = AutodeskCelReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithCustomPalette_PalettePreserved() {
    var palette = new byte[AutodeskCelFile.PaletteSize];
    for (var i = 0; i < AutodeskCelFile.PaletteEntryCount; ++i) {
      // Use values that are multiples of 4 so 6-bit round-trip is exact
      palette[i * 3] = (byte)(i % 64 * 4);
      palette[i * 3 + 1] = (byte)((255 - i) / 4 * 4);
      palette[i * 3 + 2] = (byte)(i / 2 / 4 * 4);
    }

    var original = new AutodeskCelFile {
      Width = 2,
      Height = 2,
      PixelData = [0, 1, 2, 3],
      Palette = palette,
    };

    var bytes = AutodeskCelWriter.ToBytes(original);
    var restored = AutodeskCelReader.FromBytes(bytes);

    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Offsets_Preserved() {
    var original = new AutodeskCelFile {
      Width = 2,
      Height = 2,
      XOffset = 100,
      YOffset = 50,
      PixelData = [0, 1, 2, 3],
    };

    var bytes = AutodeskCelWriter.ToBytes(original);
    var restored = AutodeskCelReader.FromBytes(bytes);

    Assert.That(restored.XOffset, Is.EqualTo(100));
    Assert.That(restored.YOffset, Is.EqualTo(50));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new AutodeskCelFile {
      Width = 8,
      Height = 4,
      PixelData = new byte[8 * 4],
    };

    var bytes = AutodeskCelWriter.ToBytes(original);
    var restored = AutodeskCelReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllSameValue() {
    var pixelData = new byte[10 * 5];
    Array.Fill(pixelData, (byte)128);

    var original = new AutodeskCelFile {
      Width = 10,
      Height = 5,
      PixelData = pixelData,
    };

    var bytes = AutodeskCelWriter.ToBytes(original);
    var restored = AutodeskCelReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixel() {
    var original = new AutodeskCelFile {
      Width = 1,
      Height = 1,
      PixelData = [42],
    };

    var bytes = AutodeskCelWriter.ToBytes(original);
    var restored = AutodeskCelReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData[0], Is.EqualTo(42));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    var pixelData = new byte[320 * 200];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new AutodeskCelFile {
      Width = 320,
      Height = 200,
      PixelData = pixelData,
    };

    var bytes = AutodeskCelWriter.ToBytes(original);
    var restored = AutodeskCelReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(200));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[4 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new AutodeskCelFile {
      Width = 4,
      Height = 2,
      XOffset = 5,
      YOffset = 10,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cel");
    try {
      var bytes = AutodeskCelWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = AutodeskCelReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.XOffset, Is.EqualTo(original.XOffset));
      Assert.That(restored.YOffset, Is.EqualTo(original.YOffset));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var pixelData = new byte[3 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var palette = new byte[AutodeskCelFile.PaletteSize];
    for (var i = 0; i < AutodeskCelFile.PaletteEntryCount; ++i) {
      palette[i * 3] = (byte)(i % 64 * 4);
      palette[i * 3 + 1] = (byte)(i % 32 * 4);
      palette[i * 3 + 2] = (byte)(i % 16 * 4);
    }

    var original = new AutodeskCelFile {
      Width = 3,
      Height = 2,
      PixelData = pixelData,
      Palette = palette,
    };

    var raw = AutodeskCelFile.ToRawImage(original);
    var restored = AutodeskCelFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BitsPerPixel_Preserved() {
    var original = new AutodeskCelFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 8,
      PixelData = [0, 1, 2, 3],
    };

    var bytes = AutodeskCelWriter.ToBytes(original);
    var restored = AutodeskCelReader.FromBytes(bytes);

    Assert.That(restored.BitsPerPixel, Is.EqualTo(8));
  }
}
