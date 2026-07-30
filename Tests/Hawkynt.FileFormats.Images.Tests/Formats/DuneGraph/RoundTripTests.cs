using System;
using System.IO;
using FileFormat.Core;
using FileFormat.DuneGraph;

namespace FileFormat.DuneGraph.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Uncompressed_PixelDataPreserved() {
    var palette = new byte[DuneGraphFile.PaletteEntryCount * 3];
    palette[0] = 0xFF;
    palette[1] = 0x80;
    palette[2] = 0x40;

    var pixelData = new byte[DuneGraphFile.PixelDataSize];
    pixelData[0] = 1;
    pixelData[1] = 2;
    pixelData[DuneGraphFile.PixelDataSize - 1] = 255;

    var original = new DuneGraphFile {
      IsCompressed = false,
      Palette = palette,
      PixelData = pixelData,
    };

    var bytes = DuneGraphWriter.ToBytes(original);
    var restored = DuneGraphReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Compressed_PixelDataPreserved() {
    var pixelData = new byte[DuneGraphFile.PixelDataSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var palette = new byte[DuneGraphFile.PaletteEntryCount * 3];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (byte)(i * 3 % 256);

    var original = new DuneGraphFile {
      IsCompressed = true,
      Palette = palette,
      PixelData = pixelData,
    };

    var bytes = DuneGraphWriter.ToBytes(original);
    var restored = DuneGraphReader.FromBytes(bytes);

    Assert.That(restored.IsCompressed, Is.True);
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new DuneGraphFile {
      IsCompressed = false,
      Palette = new byte[DuneGraphFile.PaletteEntryCount * 3],
      PixelData = new byte[DuneGraphFile.PixelDataSize],
    };

    var bytes = DuneGraphWriter.ToBytes(original);
    var restored = DuneGraphReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var palette = new byte[DuneGraphFile.PaletteEntryCount * 3];
    palette[0] = 0x10;
    palette[1] = 0x20;
    palette[2] = 0x30;

    var pixelData = new byte[DuneGraphFile.PixelDataSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new DuneGraphFile {
      IsCompressed = false,
      Palette = palette,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dg1");
    try {
      var bytes = DuneGraphWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = DuneGraphReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var palette = new byte[DuneGraphFile.PaletteEntryCount * 3];
    palette[0] = 0xFF;
    palette[1] = 0x00;
    palette[2] = 0x00;
    palette[3] = 0x00;
    palette[4] = 0xFF;
    palette[5] = 0x00;

    var pixelData = new byte[320 * 200];
    pixelData[0] = 0;
    pixelData[1] = 1;

    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Indexed8,
      PixelData = pixelData,
      Palette = palette,
      PaletteCount = 256,
    };

    var file = DuneGraphFile.FromRawImage(raw);
    var rawBack = DuneGraphFile.ToRawImage(file);

    Assert.That(rawBack.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(rawBack.Width, Is.EqualTo(320));
    Assert.That(rawBack.Height, Is.EqualTo(200));
    Assert.That(rawBack.PixelData[0], Is.EqualTo(0));
    Assert.That(rawBack.PixelData[1], Is.EqualTo(1));
    Assert.That(rawBack.Palette![0], Is.EqualTo(0xFF));
    Assert.That(rawBack.Palette[1], Is.EqualTo(0x00));
    Assert.That(rawBack.Palette[2], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Compressed_WithZeroPixels() {
    var pixelData = new byte[DuneGraphFile.PixelDataSize];
    // Mix of zeros and non-zeros
    pixelData[0] = 0x00;
    pixelData[1] = 0x00;
    pixelData[2] = 0x00;
    pixelData[3] = 0x42;
    pixelData[4] = 0x42;
    pixelData[5] = 0x42;
    pixelData[6] = 0x42;

    var original = new DuneGraphFile {
      IsCompressed = true,
      Palette = new byte[DuneGraphFile.PaletteEntryCount * 3],
      PixelData = pixelData,
    };

    var bytes = DuneGraphWriter.ToBytes(original);
    var restored = DuneGraphReader.FromBytes(bytes);

    Assert.That(restored.PixelData[0], Is.EqualTo(0x00));
    Assert.That(restored.PixelData[1], Is.EqualTo(0x00));
    Assert.That(restored.PixelData[2], Is.EqualTo(0x00));
    Assert.That(restored.PixelData[3], Is.EqualTo(0x42));
    Assert.That(restored.PixelData[4], Is.EqualTo(0x42));
    Assert.That(restored.PixelData[5], Is.EqualTo(0x42));
    Assert.That(restored.PixelData[6], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllMaxValues() {
    var palette = new byte[DuneGraphFile.PaletteEntryCount * 3];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = 0xFF;

    var pixelData = new byte[DuneGraphFile.PixelDataSize];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = 0xFF;

    var original = new DuneGraphFile {
      IsCompressed = false,
      Palette = palette,
      PixelData = pixelData,
    };

    var bytes = DuneGraphWriter.ToBytes(original);
    var restored = DuneGraphReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
