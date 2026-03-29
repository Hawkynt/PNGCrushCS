using System;
using System.IO;
using FileFormat.ComputerEyes;

namespace FileFormat.ComputerEyes.Tests;

[TestFixture]
public sealed class ComputerEyesReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ComputerEyesReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ComputerEyesReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ce"));
    Assert.Throws<FileNotFoundException>(() => ComputerEyesReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ComputerEyesReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[2];
    Assert.Throws<InvalidDataException>(() => ComputerEyesReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_ParsesCorrectly() {
    var width = 8;
    var height = 4;
    var pixelBytes = width * height;
    var data = new byte[ComputerEyesFile.HeaderSize + pixelBytes];
    data[0] = (byte)(width & 0xFF);
    data[1] = (byte)((width >> 8) & 0xFF);
    data[2] = (byte)(height & 0xFF);
    data[3] = (byte)((height >> 8) & 0xFF);
    data[ComputerEyesFile.HeaderSize] = 0xFF;
    data[ComputerEyesFile.HeaderSize + 1] = 0x80;

    var result = ComputerEyesReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.PixelData.Length, Is.EqualTo(pixelBytes));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[1], Is.EqualTo(0x80));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var width = 2;
    var height = 2;
    var pixelBytes = width * height;
    var data = new byte[ComputerEyesFile.HeaderSize + pixelBytes];
    data[0] = (byte)width;
    data[2] = (byte)height;
    data[ComputerEyesFile.HeaderSize] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = ComputerEyesReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesData() {
    var width = 5;
    var height = 3;
    var pixelBytes = width * height;
    var pixelData = new byte[pixelBytes];
    for (var i = 0; i < pixelBytes; ++i)
      pixelData[i] = (byte)(i * 13);

    var file = new ComputerEyesFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var written = ComputerEyesWriter.ToBytes(file);
    var reRead = ComputerEyesReader.FromBytes(written);

    Assert.That(reRead.Width, Is.EqualTo(width));
    Assert.That(reRead.Height, Is.EqualTo(height));
    Assert.That(reRead.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ZeroHeight_ThrowsInvalidDataException() {
    var data = new byte[ComputerEyesFile.HeaderSize];
    data[0] = 1;
    Assert.Throws<InvalidDataException>(() => ComputerEyesReader.FromBytes(data));
  }
}
