using System;
using System.IO;
using FileFormat.Psp;

namespace FileFormat.Psp.Tests;

[TestFixture]
public sealed class PspReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PspReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PspReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".psp"));
    Assert.Throws<FileNotFoundException>(() => PspReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PspReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var data = new byte[10];
    Assert.Throws<InvalidDataException>(() => PspReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[36];
    data[0] = 0xFF;
    Assert.Throws<InvalidDataException>(() => PspReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesDimensions() {
    var file = new PspFile {
      Width = 4,
      Height = 3,
      PixelData = new byte[4 * 3 * 3]
    };
    var bytes = PspWriter.ToBytes(file);
    var result = PspReader.FromBytes(bytes);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesBitDepth() {
    var file = new PspFile {
      Width = 2,
      Height = 2,
      BitDepth = 24,
      PixelData = new byte[2 * 2 * 3]
    };
    var bytes = PspWriter.ToBytes(file);
    var result = PspReader.FromBytes(bytes);

    Assert.That(result.BitDepth, Is.EqualTo(24));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesPixelData() {
    var pixels = new byte[2 * 1 * 3];
    pixels[0] = 255;
    pixels[1] = 128;
    pixels[2] = 64;
    pixels[3] = 32;
    pixels[4] = 16;
    pixels[5] = 8;

    var file = new PspFile {
      Width = 2,
      Height = 1,
      PixelData = pixels
    };
    var bytes = PspWriter.ToBytes(file);
    var result = PspReader.FromBytes(bytes);

    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidFile_ParsesCorrectly() {
    var file = new PspFile {
      Width = 3,
      Height = 2,
      PixelData = new byte[3 * 2 * 3]
    };
    var bytes = PspWriter.ToBytes(file);

    using var ms = new MemoryStream(bytes);
    var result = PspReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesVersion() {
    var file = new PspFile {
      Width = 1,
      Height = 1,
      MajorVersion = 6,
      MinorVersion = 1,
      PixelData = new byte[3]
    };
    var bytes = PspWriter.ToBytes(file);
    var result = PspReader.FromBytes(bytes);

    Assert.That(result.MajorVersion, Is.EqualTo(6));
    Assert.That(result.MinorVersion, Is.EqualTo(1));
  }
}
