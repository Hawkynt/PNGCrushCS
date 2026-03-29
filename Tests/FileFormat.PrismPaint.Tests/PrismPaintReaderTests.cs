using System;
using System.IO;
using FileFormat.PrismPaint;

namespace FileFormat.PrismPaint.Tests;

[TestFixture]
public sealed class PrismPaintReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrismPaintReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrismPaintReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pnt"));
    Assert.Throws<FileNotFoundException>(() => PrismPaintReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PrismPaintReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => PrismPaintReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroDimensions_ThrowsInvalidDataException() {
    var data = new byte[PrismPaintFile.MinFileSize];
    // width=0, height=0
    Assert.Throws<InvalidDataException>(() => PrismPaintReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid320x200_Parses() {
    var width = 320;
    var height = 200;
    var pixelSize = width * height;
    var data = new byte[PrismPaintFile.HeaderSize + PrismPaintFile.PaletteDataSize + pixelSize];

    // LE dimensions
    data[0] = (byte)(width & 0xFF);
    data[1] = (byte)((width >> 8) & 0xFF);
    data[2] = (byte)(height & 0xFF);
    data[3] = (byte)((height >> 8) & 0xFF);

    // Set palette entry 0: R=0xAA, G=0xBB, pad=0x00, B=0xCC
    var palOff = PrismPaintFile.HeaderSize;
    data[palOff] = 0xAA;
    data[palOff + 1] = 0xBB;
    data[palOff + 2] = 0x00;
    data[palOff + 3] = 0xCC;

    // First pixel
    data[PrismPaintFile.HeaderSize + PrismPaintFile.PaletteDataSize] = 42;

    var result = PrismPaintReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.PixelData[0], Is.EqualTo(42));
    Assert.That(result.Palette[0], Is.EqualTo(0xAA));
    Assert.That(result.Palette[1], Is.EqualTo(0xBB));
    Assert.That(result.Palette[2], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid640x480_Parses() {
    var width = 640;
    var height = 480;
    var pixelSize = width * height;
    var data = new byte[PrismPaintFile.HeaderSize + PrismPaintFile.PaletteDataSize + pixelSize];

    data[0] = (byte)(width & 0xFF);
    data[1] = (byte)((width >> 8) & 0xFF);
    data[2] = (byte)(height & 0xFF);
    data[3] = (byte)((height >> 8) & 0xFF);

    var result = PrismPaintReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(480));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var width = 100;
    var height = 50;
    var data = new byte[PrismPaintFile.HeaderSize + PrismPaintFile.PaletteDataSize + width * height];
    data[0] = (byte)(width & 0xFF);
    data[1] = (byte)((width >> 8) & 0xFF);
    data[2] = (byte)(height & 0xFF);
    data[3] = (byte)((height >> 8) & 0xFF);
    data[PrismPaintFile.HeaderSize + PrismPaintFile.PaletteDataSize] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = PrismPaintReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(100));
    Assert.That(result.Height, Is.EqualTo(50));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_FalconPaletteConversion_SkipsPaddingByte() {
    var width = 10;
    var height = 10;
    var data = new byte[PrismPaintFile.HeaderSize + PrismPaintFile.PaletteDataSize + width * height];
    data[0] = (byte)width;
    data[1] = 0;
    data[2] = (byte)height;
    data[3] = 0;

    var palOff = PrismPaintFile.HeaderSize;
    data[palOff] = 0x10;     // R
    data[palOff + 1] = 0x20; // G
    data[palOff + 2] = 0xFF; // padding - ignored
    data[palOff + 3] = 0x30; // B

    var result = PrismPaintReader.FromBytes(data);

    Assert.That(result.Palette[0], Is.EqualTo(0x10));
    Assert.That(result.Palette[1], Is.EqualTo(0x20));
    Assert.That(result.Palette[2], Is.EqualTo(0x30));
  }
}
