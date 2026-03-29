using System;
using FileFormat.ScreenMaker;

namespace FileFormat.ScreenMaker.Tests;

[TestFixture]
public sealed class ScreenMakerWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ScreenMakerWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WidthLE_FirstTwoBytes() {
    var file = new ScreenMakerFile {
      Width = 0x0130, // 304
      Height = 1,
      Palette = new byte[768],
      PixelData = new byte[304],
    };

    var bytes = ScreenMakerWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x30));
    Assert.That(bytes[1], Is.EqualTo(0x01));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeightLE_Bytes2And3() {
    var file = new ScreenMakerFile {
      Width = 1,
      Height = 0x00C8, // 200
      Palette = new byte[768],
      PixelData = new byte[200],
    };

    var bytes = ScreenMakerWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0xC8));
    Assert.That(bytes[3], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PalettePresent_At768Bytes() {
    var palette = new byte[768];
    palette[0] = 0xAA;
    palette[1] = 0xBB;
    palette[2] = 0xCC;
    palette[767] = 0xDD;

    var file = new ScreenMakerFile {
      Width = 2,
      Height = 2,
      Palette = palette,
      PixelData = new byte[4],
    };

    var bytes = ScreenMakerWriter.ToBytes(file);

    Assert.That(bytes[ScreenMakerFile.HeaderSize], Is.EqualTo(0xAA));
    Assert.That(bytes[ScreenMakerFile.HeaderSize + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[ScreenMakerFile.HeaderSize + 2], Is.EqualTo(0xCC));
    Assert.That(bytes[ScreenMakerFile.HeaderSize + 767], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPresent_AfterPalette() {
    var file = new ScreenMakerFile {
      Width = 4,
      Height = 2,
      Palette = new byte[768],
      PixelData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 },
    };

    var bytes = ScreenMakerWriter.ToBytes(file);

    var pixelStart = ScreenMakerFile.HeaderSize + ScreenMakerFile.PaletteDataSize;
    Assert.That(bytes[pixelStart], Is.EqualTo(0x01));
    Assert.That(bytes[pixelStart + 7], Is.EqualTo(0x08));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TotalSizeCorrect() {
    var file = new ScreenMakerFile {
      Width = 16,
      Height = 16,
      Palette = new byte[768],
      PixelData = new byte[256],
    };

    var bytes = ScreenMakerWriter.ToBytes(file);

    var expected = ScreenMakerFile.HeaderSize + ScreenMakerFile.PaletteDataSize + 256;
    Assert.That(bytes.Length, Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SmallImage_TotalSizeCorrect() {
    var file = new ScreenMakerFile {
      Width = 1,
      Height = 1,
      Palette = new byte[768],
      PixelData = new byte[1],
    };

    var bytes = ScreenMakerWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(ScreenMakerFile.HeaderSize + ScreenMakerFile.PaletteDataSize + 1));
  }
}
