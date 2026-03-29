using System;
using System.IO;
using FileFormat.PmBitmap;

namespace FileFormat.PmBitmap.Tests;

[TestFixture]
public sealed class PmBitmapReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PmBitmapReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PmBitmapReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pm1"));
    Assert.Throws<FileNotFoundException>(() => PmBitmapReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PmBitmapReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[8];
    Assert.Throws<InvalidDataException>(() => PmBitmapReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = new byte[PmBitmapFile.HeaderSize + 4];
    data[0] = (byte)'X';
    data[1] = (byte)'Y';
    data[4] = 2;
    data[6] = 2;
    data[8] = 8;
    Assert.Throws<InvalidDataException>(() => PmBitmapReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidDepth_ThrowsInvalidDataException() {
    var data = new byte[PmBitmapFile.HeaderSize + 4];
    data[0] = (byte)'P';
    data[1] = (byte)'M';
    data[2] = 0;
    data[4] = 2;
    data[6] = 2;
    data[8] = 16;
    Assert.Throws<InvalidDataException>(() => PmBitmapReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Grayscale_ParsesCorrectly() {
    var width = 4;
    var height = 2;
    var pixelBytes = width * height;
    var data = new byte[PmBitmapFile.HeaderSize + pixelBytes];
    data[0] = (byte)'P';
    data[1] = (byte)'M';
    data[2] = 0;
    data[3] = 1;
    data[4] = (byte)(width & 0xFF);
    data[5] = (byte)((width >> 8) & 0xFF);
    data[6] = (byte)(height & 0xFF);
    data[7] = (byte)((height >> 8) & 0xFF);
    data[8] = 8;
    data[PmBitmapFile.HeaderSize] = 0xFF;
    data[PmBitmapFile.HeaderSize + 1] = 0x80;

    var result = PmBitmapReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Depth, Is.EqualTo(8));
    Assert.That(result.Version, Is.EqualTo(1));
    Assert.That(result.PixelData.Length, Is.EqualTo(pixelBytes));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Rgb24_ParsesCorrectly() {
    var width = 2;
    var height = 2;
    var pixelBytes = width * height * 3;
    var data = new byte[PmBitmapFile.HeaderSize + pixelBytes];
    data[0] = (byte)'P';
    data[1] = (byte)'M';
    data[2] = 0;
    data[3] = 2;
    data[4] = (byte)width;
    data[6] = (byte)height;
    data[8] = 24;
    data[PmBitmapFile.HeaderSize] = 0xAA;
    data[PmBitmapFile.HeaderSize + 1] = 0xBB;
    data[PmBitmapFile.HeaderSize + 2] = 0xCC;

    var result = PmBitmapReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.Depth, Is.EqualTo(24));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
    Assert.That(result.PixelData[1], Is.EqualTo(0xBB));
    Assert.That(result.PixelData[2], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var width = 2;
    var height = 2;
    var pixelBytes = width * height;
    var data = new byte[PmBitmapFile.HeaderSize + pixelBytes];
    data[0] = (byte)'P';
    data[1] = (byte)'M';
    data[2] = 0;
    data[4] = (byte)width;
    data[6] = (byte)height;
    data[8] = 8;
    data[PmBitmapFile.HeaderSize] = 0xEF;

    using var ms = new MemoryStream(data);
    var result = PmBitmapReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData[0], Is.EqualTo(0xEF));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_Grayscale_PreservesData() {
    var width = 3;
    var height = 2;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 40);

    var file = new PmBitmapFile {
      Width = width,
      Height = height,
      Depth = 8,
      Version = 1,
      PixelData = pixelData,
    };

    var written = PmBitmapWriter.ToBytes(file);
    var reRead = PmBitmapReader.FromBytes(written);

    Assert.That(reRead.Width, Is.EqualTo(width));
    Assert.That(reRead.Height, Is.EqualTo(height));
    Assert.That(reRead.Depth, Is.EqualTo(8));
    Assert.That(reRead.Version, Is.EqualTo(1));
    Assert.That(reRead.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_Rgb24_PreservesData() {
    var width = 2;
    var height = 2;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17);

    var file = new PmBitmapFile {
      Width = width,
      Height = height,
      Depth = 24,
      Version = 2,
      PixelData = pixelData,
    };

    var written = PmBitmapWriter.ToBytes(file);
    var reRead = PmBitmapReader.FromBytes(written);

    Assert.That(reRead.Width, Is.EqualTo(width));
    Assert.That(reRead.Height, Is.EqualTo(height));
    Assert.That(reRead.Depth, Is.EqualTo(24));
    Assert.That(reRead.Version, Is.EqualTo(2));
    Assert.That(reRead.PixelData, Is.EqualTo(pixelData));
  }
}
