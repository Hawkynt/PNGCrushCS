using System;
using System.IO;
using FileFormat.Core;
using FileFormat.PcPaint;

namespace FileFormat.PcPaint.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_BasicIndexedImage() {
    var palette = new byte[PcPaintFile.PaletteSize];
    palette[0] = 255; palette[1] = 0; palette[2] = 0;
    palette[3] = 0; palette[4] = 255; palette[5] = 0;
    palette[6] = 0; palette[7] = 0; palette[8] = 255;

    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 3);

    var original = new PcPaintFile {
      Width = 4,
      Height = 3,
      Planes = 1,
      BitsPerPixel = 8,
      Palette = palette,
      PixelData = pixelData,
    };

    var bytes = PcPaintWriter.ToBytes(original);
    var restored = PcPaintReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var palette = new byte[PcPaintFile.PaletteSize];
    for (var i = 0; i < PcPaintFile.PaletteSize; ++i)
      palette[i] = (byte)(i % 256);

    var pixelData = new byte[10 * 10];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new PcPaintFile {
      Width = 10,
      Height = 10,
      Planes = 1,
      BitsPerPixel = 8,
      Palette = palette,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pic");
    try {
      var bytes = PcPaintWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = PcPaintReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var palette = new byte[PcPaintFile.PaletteSize];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(255 - i);
      palette[i * 3 + 2] = (byte)(i / 2);
    }

    var pixelData = new byte[8 * 6];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new PcPaintFile {
      Width = 8,
      Height = 6,
      Planes = 1,
      BitsPerPixel = 8,
      Palette = palette,
      PixelData = pixelData,
    };

    var raw = PcPaintFile.ToRawImage(original);
    var restored = PcPaintFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new PcPaintFile {
      Width = 2,
      Height = 2,
      Planes = 1,
      BitsPerPixel = 8,
      Palette = new byte[PcPaintFile.PaletteSize],
      PixelData = new byte[4],
    };

    var bytes = PcPaintWriter.ToBytes(original);
    var restored = PcPaintReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeRunsUsingExtendedCount() {
    var pixelData = new byte[500];
    for (var i = 0; i < 500; ++i)
      pixelData[i] = 99;

    var original = new PcPaintFile {
      Width = 500,
      Height = 1,
      Planes = 1,
      BitsPerPixel = 8,
      Palette = new byte[PcPaintFile.PaletteSize],
      PixelData = pixelData,
    };

    var bytes = PcPaintWriter.ToBytes(original);
    var restored = PcPaintReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_OffsetsPreserved() {
    var original = new PcPaintFile {
      Width = 2,
      Height = 2,
      XOffset = 100,
      YOffset = 200,
      Planes = 1,
      BitsPerPixel = 8,
      Palette = new byte[PcPaintFile.PaletteSize],
      PixelData = new byte[4],
    };

    var bytes = PcPaintWriter.ToBytes(original);
    var restored = PcPaintReader.FromBytes(bytes);

    Assert.That(restored.XOffset, Is.EqualTo(100));
    Assert.That(restored.YOffset, Is.EqualTo(200));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AspectPreserved() {
    var original = new PcPaintFile {
      Width = 2,
      Height = 2,
      XAspect = 1,
      YAspect = 2,
      Planes = 1,
      BitsPerPixel = 8,
      Palette = new byte[PcPaintFile.PaletteSize],
      PixelData = new byte[4],
    };

    var bytes = PcPaintWriter.ToBytes(original);
    var restored = PcPaintReader.FromBytes(bytes);

    Assert.That(restored.XAspect, Is.EqualTo(1));
    Assert.That(restored.YAspect, Is.EqualTo(2));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_GradientPixels() {
    var pixelData = new byte[256];
    for (var i = 0; i < 256; ++i)
      pixelData[i] = (byte)i;

    var palette = new byte[PcPaintFile.PaletteSize];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)i;
      palette[i * 3 + 2] = (byte)i;
    }

    var original = new PcPaintFile {
      Width = 16,
      Height = 16,
      Planes = 1,
      BitsPerPixel = 8,
      Palette = palette,
      PixelData = pixelData,
    };

    var bytes = PcPaintWriter.ToBytes(original);
    var restored = PcPaintReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }
}
