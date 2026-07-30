using System;
using System.IO;
using FileFormat.Core;
using FileFormat.PrismPaint;

namespace FileFormat.PrismPaint.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_320x200_PixelDataPreserved() {
    var palette = new byte[PrismPaintFile.PaletteEntryCount * 3];
    palette[0] = 0xFF;
    palette[1] = 0x80;
    palette[2] = 0x40;

    var pixelData = new byte[320 * 200];
    pixelData[0] = 1;
    pixelData[1] = 2;
    pixelData[320 * 200 - 1] = 255;

    var original = new PrismPaintFile {
      Width = 320,
      Height = 200,
      Palette = palette,
      PixelData = pixelData,
    };

    var bytes = PrismPaintWriter.ToBytes(original);
    var restored = PrismPaintReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_640x480_PixelDataPreserved() {
    var palette = new byte[PrismPaintFile.PaletteEntryCount * 3];
    var pixelData = new byte[640 * 480];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new PrismPaintFile {
      Width = 640,
      Height = 480,
      Palette = palette,
      PixelData = pixelData,
    };

    var bytes = PrismPaintWriter.ToBytes(original);
    var restored = PrismPaintReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(640));
    Assert.That(restored.Height, Is.EqualTo(480));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new PrismPaintFile {
      Width = 100,
      Height = 50,
      Palette = new byte[PrismPaintFile.PaletteEntryCount * 3],
      PixelData = new byte[100 * 50],
    };

    var bytes = PrismPaintWriter.ToBytes(original);
    var restored = PrismPaintReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(100));
    Assert.That(restored.Height, Is.EqualTo(50));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var palette = new byte[PrismPaintFile.PaletteEntryCount * 3];
    palette[0] = 0x10;
    palette[1] = 0x20;
    palette[2] = 0x30;

    var pixelData = new byte[200 * 100];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new PrismPaintFile {
      Width = 200,
      Height = 100,
      Palette = palette,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pnt");
    try {
      var bytes = PrismPaintWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = PrismPaintReader.FromFile(new FileInfo(tempPath));

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
    var palette = new byte[PrismPaintFile.PaletteEntryCount * 3];
    palette[0] = 0xFF;
    palette[1] = 0x00;
    palette[2] = 0x00;
    palette[3] = 0x00;
    palette[4] = 0xFF;
    palette[5] = 0x00;

    var pixelData = new byte[100 * 50];
    pixelData[0] = 0;
    pixelData[1] = 1;

    var raw = new RawImage {
      Width = 100,
      Height = 50,
      Format = PixelFormat.Indexed8,
      PixelData = pixelData,
      Palette = palette,
      PaletteCount = 256,
    };

    var file = PrismPaintFile.FromRawImage(raw);
    var rawBack = PrismPaintFile.ToRawImage(file);

    Assert.That(rawBack.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(rawBack.Width, Is.EqualTo(100));
    Assert.That(rawBack.Height, Is.EqualTo(50));
    Assert.That(rawBack.PixelData[0], Is.EqualTo(0));
    Assert.That(rawBack.PixelData[1], Is.EqualTo(1));
    Assert.That(rawBack.Palette![0], Is.EqualTo(0xFF));
    Assert.That(rawBack.Palette[1], Is.EqualTo(0x00));
    Assert.That(rawBack.Palette[2], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SmallImage_1x1() {
    var palette = new byte[PrismPaintFile.PaletteEntryCount * 3];
    palette[0] = 0x42;
    var pixelData = new byte[1];
    pixelData[0] = 0;

    var original = new PrismPaintFile {
      Width = 1,
      Height = 1,
      Palette = palette,
      PixelData = pixelData,
    };

    var bytes = PrismPaintWriter.ToBytes(original);
    var restored = PrismPaintReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData[0], Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllMaxValues() {
    var palette = new byte[PrismPaintFile.PaletteEntryCount * 3];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = 0xFF;

    var pixelData = new byte[320 * 200];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = 0xFF;

    var original = new PrismPaintFile {
      Width = 320,
      Height = 200,
      Palette = palette,
      PixelData = pixelData,
    };

    var bytes = PrismPaintWriter.ToBytes(original);
    var restored = PrismPaintReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }
}
