using System;
using System.IO;
using FileFormat.DbwRender;

namespace FileFormat.DbwRender.Tests;

[TestFixture]
public sealed class DbwRenderReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DbwRenderReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DbwRenderReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dbw"));
    Assert.Throws<FileNotFoundException>(() => DbwRenderReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DbwRenderReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[5];
    Assert.Throws<InvalidDataException>(() => DbwRenderReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesCorrectly() {
    var width = 4;
    var height = 2;
    var pixelBytes = width * height * 3;
    var data = new byte[DbwRenderFile.HeaderSize + pixelBytes];
    data[0] = (byte)(width & 0xFF);
    data[1] = (byte)((width >> 8) & 0xFF);
    data[2] = (byte)(height & 0xFF);
    data[3] = (byte)((height >> 8) & 0xFF);
    data[DbwRenderFile.HeaderSize] = 0xFF;
    data[DbwRenderFile.HeaderSize + 1] = 0x80;
    data[DbwRenderFile.HeaderSize + 2] = 0x40;

    var result = DbwRenderReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData.Length, Is.EqualTo(pixelBytes));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[1], Is.EqualTo(0x80));
    Assert.That(result.PixelData[2], Is.EqualTo(0x40));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var width = 2;
    var height = 2;
    var pixelBytes = width * height * 3;
    var data = new byte[DbwRenderFile.HeaderSize + pixelBytes];
    data[0] = (byte)width;
    data[2] = (byte)height;
    data[DbwRenderFile.HeaderSize] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = DbwRenderReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesData() {
    var width = 3;
    var height = 2;
    var pixelBytes = width * height * 3;
    var pixelData = new byte[pixelBytes];
    for (var i = 0; i < pixelBytes; ++i)
      pixelData[i] = (byte)(i * 7);

    var file = new DbwRenderFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var written = DbwRenderWriter.ToBytes(file);
    var reRead = DbwRenderReader.FromBytes(written);

    Assert.That(reRead.Width, Is.EqualTo(width));
    Assert.That(reRead.Height, Is.EqualTo(height));
    Assert.That(reRead.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroWidth_ThrowsInvalidDataException() {
    var data = new byte[DbwRenderFile.HeaderSize];
    data[2] = 1;
    Assert.Throws<InvalidDataException>(() => DbwRenderReader.FromBytes(data));
  }
}
