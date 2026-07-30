using System;
using System.IO;
using FileFormat.SbigCcd;

namespace FileFormat.SbigCcd.Tests;

[TestFixture]
public sealed class SbigCcdReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SbigCcdReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SbigCcdReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".st4"));
    Assert.Throws<FileNotFoundException>(() => SbigCcdReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SbigCcdReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => SbigCcdReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesCorrectly() {
    var width = 4;
    var height = 3;
    var pixelBytes = width * height * 2;
    var data = new byte[SbigCcdFile.HeaderSize + pixelBytes];
    data[0] = (byte)(width & 0xFF);
    data[1] = (byte)((width >> 8) & 0xFF);
    data[2] = (byte)(height & 0xFF);
    data[3] = (byte)((height >> 8) & 0xFF);
    data[SbigCcdFile.HeaderSize] = 0x00;
    data[SbigCcdFile.HeaderSize + 1] = 0xFF;

    var result = SbigCcdReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.PixelData.Length, Is.EqualTo(pixelBytes));
    Assert.That(result.PixelData[0], Is.EqualTo(0x00));
    Assert.That(result.PixelData[1], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var width = 2;
    var height = 2;
    var pixelBytes = width * height * 2;
    var data = new byte[SbigCcdFile.HeaderSize + pixelBytes];
    data[0] = (byte)width;
    data[2] = (byte)height;
    data[SbigCcdFile.HeaderSize] = 0xCD;

    using var ms = new MemoryStream(data);
    var result = SbigCcdReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData[0], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesData() {
    var width = 3;
    var height = 2;
    var pixelBytes = width * height * 2;
    var pixelData = new byte[pixelBytes];
    for (var i = 0; i < pixelBytes; ++i)
      pixelData[i] = (byte)(i * 11);

    var file = new SbigCcdFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var written = SbigCcdWriter.ToBytes(file);
    var reRead = SbigCcdReader.FromBytes(written);

    Assert.That(reRead.Width, Is.EqualTo(width));
    Assert.That(reRead.Height, Is.EqualTo(height));
    Assert.That(reRead.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ScalesTo8Bit() {
    var width = 2;
    var height = 1;
    var file = new SbigCcdFile {
      Width = width,
      Height = height,
      PixelData = [0x00, 0x80, 0xFF, 0xFF],
    };

    var raw = SbigCcdFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(2));
    Assert.That(raw.Height, Is.EqualTo(1));
    Assert.That(raw.PixelData[0], Is.EqualTo(0x80));
    Assert.That(raw.PixelData[3], Is.EqualTo(0xFF));
  }
}
