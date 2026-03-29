using System;
using System.IO;
using FileFormat.Rembrandt;

namespace FileFormat.Rembrandt.Tests;

[TestFixture]
public sealed class RembrandtReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RembrandtReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RembrandtReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tcp"));
    Assert.Throws<FileNotFoundException>(() => RembrandtReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => RembrandtReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[3];
    Assert.Throws<InvalidDataException>(() => RembrandtReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroDimensions_ThrowsInvalidDataException() {
    var data = new byte[RembrandtFile.MinFileSize];
    // width=0, height=0
    Assert.Throws<InvalidDataException>(() => RembrandtReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid320x240_Parses() {
    var width = 320;
    var height = 240;
    var data = new byte[RembrandtFile.HeaderSize + width * height * 2];

    // BE dimensions
    data[0] = (byte)((width >> 8) & 0xFF);
    data[1] = (byte)(width & 0xFF);
    data[2] = (byte)((height >> 8) & 0xFF);
    data[3] = (byte)(height & 0xFF);

    // First pixel: pure red RGB565 BE = 0xF800
    data[4] = 0xF8;
    data[5] = 0x00;

    var result = RembrandtReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(240));
    Assert.That(result.PixelData[0], Is.EqualTo(0xF8));
    Assert.That(result.PixelData[1], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid640x480_Parses() {
    var width = 640;
    var height = 480;
    var data = new byte[RembrandtFile.HeaderSize + width * height * 2];

    data[0] = (byte)((width >> 8) & 0xFF);
    data[1] = (byte)(width & 0xFF);
    data[2] = (byte)((height >> 8) & 0xFF);
    data[3] = (byte)(height & 0xFF);

    var result = RembrandtReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(480));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var width = 100;
    var height = 50;
    var data = new byte[RembrandtFile.HeaderSize + width * height * 2];
    data[0] = 0;
    data[1] = (byte)width;
    data[2] = 0;
    data[3] = (byte)height;
    data[4] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = RembrandtReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(100));
    Assert.That(result.Height, Is.EqualTo(50));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DimensionsBigEndian() {
    // 0x0140 = 320, 0x00F0 = 240
    var width = 320;
    var height = 240;
    var data = new byte[RembrandtFile.HeaderSize + width * height * 2];
    data[0] = 0x01;
    data[1] = 0x40;
    data[2] = 0x00;
    data[3] = 0xF0;

    var result = RembrandtReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(240));
  }
}
