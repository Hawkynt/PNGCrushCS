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
    // A valid header that declares a zero-sized image is still not decodable.
    var data = new byte[RembrandtFile.MinFileSize];
    RembrandtHeader.Write(data, 0, 0);

    Assert.Throws<InvalidDataException>(() => RembrandtReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid320x240_Parses() {
    var width = 320;
    var height = 240;
    var data = new byte[RembrandtHeader.StructSize + width * height * 2];
    RembrandtHeader.Write(data, width, height);

    // First pixel: pure red RGB565 BE = 0xF800
    data[RembrandtHeader.StructSize + 0] = 0xF8;
    data[RembrandtHeader.StructSize + 1] = 0x00;

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
    var data = new byte[RembrandtHeader.StructSize + width * height * 2];
    RembrandtHeader.Write(data, width, height);

    var result = RembrandtReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(480));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var width = 100;
    var height = 50;
    var data = new byte[RembrandtHeader.StructSize + width * height * 2];
    RembrandtHeader.Write(data, width, height);
    data[RembrandtHeader.StructSize + 0] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = RembrandtReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(100));
    Assert.That(result.Height, Is.EqualTo(50));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DimensionsBigEndian() {
    // 0x0140 = 320, 0x00F0 = 240 — stored big-endian at the header's dimension offset.
    var width = 320;
    var height = 240;
    var data = new byte[RembrandtHeader.StructSize + width * height * 2];
    RembrandtHeader.Write(data, width, height);

    Assert.Multiple(() => {
      Assert.That(data[RembrandtHeader.DimensionsOffset], Is.EqualTo(0x01));
      Assert.That(data[RembrandtHeader.DimensionsOffset + 1], Is.EqualTo(0x40));
      Assert.That(data[RembrandtHeader.DimensionsOffset + 2], Is.EqualTo(0x00));
      Assert.That(data[RembrandtHeader.DimensionsOffset + 3], Is.EqualTo(0xF0));
    });

    var result = RembrandtReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(240));
  }
}
