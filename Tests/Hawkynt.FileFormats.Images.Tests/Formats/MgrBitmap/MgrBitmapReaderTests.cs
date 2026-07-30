using System;
using System.IO;
using System.Text;
using FileFormat.MgrBitmap;

namespace FileFormat.MgrBitmap.Tests;

[TestFixture]
public sealed class MgrBitmapReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MgrBitmapReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MgrBitmapReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mgr"));
    Assert.Throws<FileNotFoundException>(() => MgrBitmapReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MgrBitmapReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[3];
    Assert.Throws<InvalidDataException>(() => MgrBitmapReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesCorrectly() {
    var header = Encoding.ASCII.GetBytes("16x8\n");
    var bytesPerRow = 2;
    var pixelData = new byte[bytesPerRow * 8];
    pixelData[0] = 0xFF;
    pixelData[1] = 0xAA;

    var data = new byte[header.Length + pixelData.Length];
    Array.Copy(header, 0, data, 0, header.Length);
    Array.Copy(pixelData, 0, data, header.Length, pixelData.Length);

    var result = MgrBitmapReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(16));
    Assert.That(result.Height, Is.EqualTo(8));
    Assert.That(result.PixelData.Length, Is.EqualTo(16));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[1], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoSeparator_ThrowsInvalidDataException() {
    var data = Encoding.ASCII.GetBytes("1234\n\x00\x00");
    Assert.Throws<InvalidDataException>(() => MgrBitmapReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var header = Encoding.ASCII.GetBytes("8x4\n");
    var pixelData = new byte[4];
    pixelData[0] = 0xCD;

    var data = new byte[header.Length + pixelData.Length];
    Array.Copy(header, 0, data, 0, header.Length);
    Array.Copy(pixelData, 0, data, header.Length, pixelData.Length);

    using var ms = new MemoryStream(data);
    var result = MgrBitmapReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.PixelData[0], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesData() {
    var header = Encoding.ASCII.GetBytes("8x2\n");
    var pixelData = new byte[2];
    pixelData[0] = 0b10101010;
    pixelData[1] = 0b01010101;

    var data = new byte[header.Length + pixelData.Length];
    Array.Copy(header, 0, data, 0, header.Length);
    Array.Copy(pixelData, 0, data, header.Length, pixelData.Length);

    var file = MgrBitmapReader.FromBytes(data);
    var written = MgrBitmapWriter.ToBytes(file);
    var reRead = MgrBitmapReader.FromBytes(written);

    Assert.That(reRead.Width, Is.EqualTo(8));
    Assert.That(reRead.Height, Is.EqualTo(2));
    Assert.That(reRead.PixelData, Is.EqualTo(pixelData));
  }
}
