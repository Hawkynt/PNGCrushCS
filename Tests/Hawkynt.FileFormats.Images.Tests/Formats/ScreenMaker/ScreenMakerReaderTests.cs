using System;
using System.IO;
using FileFormat.ScreenMaker;

namespace FileFormat.ScreenMaker.Tests;

[TestFixture]
public sealed class ScreenMakerReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ScreenMakerReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ScreenMakerReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".smk"));
    Assert.Throws<FileNotFoundException>(() => ScreenMakerReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ScreenMakerReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => ScreenMakerReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_HeaderAndPaletteOnly_TooSmallForPixels_ThrowsInvalidDataException() {
    // 4 header + 768 palette = 772, but width=16 height=16 needs 256 more bytes
    var data = new byte[772];
    data[0] = 16; // width LE low
    data[1] = 0;  // width LE high
    data[2] = 16; // height LE low
    data[3] = 0;  // height LE high
    Assert.Throws<InvalidDataException>(() => ScreenMakerReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = new byte[ScreenMakerFile.HeaderSize + ScreenMakerFile.PaletteDataSize];
    data[0] = 0;
    data[1] = 0;
    data[2] = 1;
    data[3] = 0;
    Assert.Throws<InvalidDataException>(() => ScreenMakerReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = new byte[ScreenMakerFile.HeaderSize + ScreenMakerFile.PaletteDataSize];
    data[0] = 1;
    data[1] = 0;
    data[2] = 0;
    data[3] = 0;
    Assert.Throws<InvalidDataException>(() => ScreenMakerReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesWidth() {
    var data = _BuildValid(16, 8);

    var result = ScreenMakerReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesHeight() {
    var data = _BuildValid(16, 8);

    var result = ScreenMakerReader.FromBytes(data);

    Assert.That(result.Height, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesPalette() {
    var data = _BuildValid(4, 4);
    // Set palette entry 0 to R=10 G=20 B=30
    data[ScreenMakerFile.HeaderSize] = 10;
    data[ScreenMakerFile.HeaderSize + 1] = 20;
    data[ScreenMakerFile.HeaderSize + 2] = 30;

    var result = ScreenMakerReader.FromBytes(data);

    Assert.That(result.Palette.Length, Is.EqualTo(768));
    Assert.That(result.Palette[0], Is.EqualTo(10));
    Assert.That(result.Palette[1], Is.EqualTo(20));
    Assert.That(result.Palette[2], Is.EqualTo(30));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesPixelData() {
    var data = _BuildValid(4, 4);
    var pixelOffset = ScreenMakerFile.HeaderSize + ScreenMakerFile.PaletteDataSize;
    data[pixelOffset] = 0xAB;
    data[pixelOffset + 1] = 0xCD;

    var result = ScreenMakerReader.FromBytes(data);

    Assert.That(result.PixelData.Length, Is.EqualTo(16));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
    Assert.That(result.PixelData[1], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_Parses() {
    var data = _BuildValid(8, 4);

    using var ms = new MemoryStream(data);
    var result = ScreenMakerReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LargeWidth_ParsesCorrectly() {
    var data = _BuildValid(300, 1);

    var result = ScreenMakerReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(300));
    Assert.That(result.PixelData.Length, Is.EqualTo(300));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesPalette_NotReference() {
    var data = _BuildValid(4, 4);
    data[ScreenMakerFile.HeaderSize] = 0xFF;

    var result = ScreenMakerReader.FromBytes(data);
    data[ScreenMakerFile.HeaderSize] = 0x00;

    Assert.That(result.Palette[0], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesPixelData_NotReference() {
    var data = _BuildValid(4, 4);
    var pixelOffset = ScreenMakerFile.HeaderSize + ScreenMakerFile.PaletteDataSize;
    data[pixelOffset] = 0xFF;

    var result = ScreenMakerReader.FromBytes(data);
    data[pixelOffset] = 0x00;

    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
  }

  private static byte[] _BuildValid(int width, int height) {
    var pixelCount = width * height;
    var totalSize = ScreenMakerFile.HeaderSize + ScreenMakerFile.PaletteDataSize + pixelCount;
    var data = new byte[totalSize];
    data[0] = (byte)(width & 0xFF);
    data[1] = (byte)(width >> 8);
    data[2] = (byte)(height & 0xFF);
    data[3] = (byte)(height >> 8);
    return data;
  }
}
