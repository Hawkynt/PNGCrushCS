using System;
using System.IO;
using FileFormat.Tga;

namespace FileFormat.Tga.Tests;

[TestFixture]
public sealed class TgaReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => TgaReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => TgaReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var file = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tga"));
    Assert.Throws<FileNotFoundException>(() => TgaReader.FromFile(file));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var data = new byte[10];
    Assert.Throws<InvalidDataException>(() => TgaReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidRgb24_ParsesCorrectly() {
    var header = new byte[18];
    header[2] = 2;            // imageType = uncompressed true-color
    header[12] = 2;           // width low byte
    header[13] = 0;           // width high byte
    header[14] = 2;           // height low byte
    header[15] = 0;           // height high byte
    header[16] = 24;          // bits per pixel
    header[17] = 0x20;        // imageDescriptor: top-left origin

    var pixelData = new byte[2 * 2 * 3]; // 4 pixels * 3 bytes BGR
    for (var i = 0; i < 4; ++i) {
      pixelData[i * 3] = (byte)(i * 10);       // B
      pixelData[i * 3 + 1] = (byte)(i * 20);   // G
      pixelData[i * 3 + 2] = (byte)(i * 30);   // R
    }

    var data = new byte[header.Length + pixelData.Length];
    Array.Copy(header, data, header.Length);
    Array.Copy(pixelData, 0, data, header.Length, pixelData.Length);

    var result = TgaReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.ColorMode, Is.EqualTo(TgaColorMode.Rgb24));
    Assert.That(result.BitsPerPixel, Is.EqualTo(24));
    Assert.That(result.PixelData.Length, Is.EqualTo(12));
  }
}
