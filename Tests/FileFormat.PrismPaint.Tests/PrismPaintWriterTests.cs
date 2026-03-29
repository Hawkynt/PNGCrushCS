using System;
using FileFormat.PrismPaint;

namespace FileFormat.PrismPaint.Tests;

[TestFixture]
public sealed class PrismPaintWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrismPaintWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ProducesCorrectSize() {
    var width = 320;
    var height = 200;
    var file = new PrismPaintFile {
      Width = width,
      Height = height,
      Palette = new byte[PrismPaintFile.PaletteEntryCount * 3],
      PixelData = new byte[width * height],
    };

    var bytes = PrismPaintWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(PrismPaintFile.HeaderSize + PrismPaintFile.PaletteDataSize + width * height));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsWrittenLE() {
    var file = new PrismPaintFile {
      Width = 640,
      Height = 480,
      Palette = new byte[PrismPaintFile.PaletteEntryCount * 3],
      PixelData = new byte[640 * 480],
    };

    var bytes = PrismPaintWriter.ToBytes(file);

    // 640 = 0x0280 => LE: 0x80, 0x02
    Assert.That(bytes[0], Is.EqualTo(0x80));
    Assert.That(bytes[1], Is.EqualTo(0x02));
    // 480 = 0x01E0 => LE: 0xE0, 0x01
    Assert.That(bytes[2], Is.EqualTo(0xE0));
    Assert.That(bytes[3], Is.EqualTo(0x01));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaletteConvertedToFalconFormat() {
    var palette = new byte[PrismPaintFile.PaletteEntryCount * 3];
    palette[0] = 0xAA;
    palette[1] = 0xBB;
    palette[2] = 0xCC;

    var file = new PrismPaintFile {
      Width = 10,
      Height = 10,
      Palette = palette,
      PixelData = new byte[100],
    };

    var bytes = PrismPaintWriter.ToBytes(file);

    var palOff = PrismPaintFile.HeaderSize;
    Assert.That(bytes[palOff], Is.EqualTo(0xAA));     // R
    Assert.That(bytes[palOff + 1], Is.EqualTo(0xBB)); // G
    Assert.That(bytes[palOff + 2], Is.EqualTo(0x00)); // padding
    Assert.That(bytes[palOff + 3], Is.EqualTo(0xCC)); // B
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[100];
    pixelData[0] = 0xFF;
    pixelData[99] = 0xDE;

    var file = new PrismPaintFile {
      Width = 10,
      Height = 10,
      Palette = new byte[PrismPaintFile.PaletteEntryCount * 3],
      PixelData = pixelData,
    };

    var bytes = PrismPaintWriter.ToBytes(file);

    var pixelOff = PrismPaintFile.HeaderSize + PrismPaintFile.PaletteDataSize;
    Assert.That(bytes[pixelOff], Is.EqualTo(0xFF));
    Assert.That(bytes[pixelOff + 99], Is.EqualTo(0xDE));
  }
}
