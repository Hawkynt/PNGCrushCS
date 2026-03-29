using System;
using System.IO;
using System.Text;
using FileFormat.AndrewToolkit;

namespace FileFormat.AndrewToolkit.Tests;

[TestFixture]
public sealed class AndrewToolkitReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AndrewToolkitReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AndrewToolkitReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".atk"));
    Assert.Throws<FileNotFoundException>(() => AndrewToolkitReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AndrewToolkitReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[4];
    Assert.Throws<InvalidDataException>(() => AndrewToolkitReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidHeader_ParsesCorrectly() {
    var header = Encoding.ASCII.GetBytes("width = 4\nheight = 3\n\n");
    var pixelData = new byte[12];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 10);

    var data = new byte[header.Length + pixelData.Length];
    Array.Copy(header, 0, data, 0, header.Length);
    Array.Copy(pixelData, 0, data, header.Length, pixelData.Length);

    var result = AndrewToolkitReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.RawData.Length, Is.EqualTo(12));
    Assert.That(result.RawData[0], Is.EqualTo(0));
    Assert.That(result.RawData[1], Is.EqualTo(10));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoDimensions_ThrowsInvalidDataException() {
    var header = Encoding.ASCII.GetBytes("somekey = value\n\n");
    Assert.Throws<InvalidDataException>(() => AndrewToolkitReader.FromBytes(header));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var header = Encoding.ASCII.GetBytes("width = 2\nheight = 2\n\n");
    var pixelData = new byte[4];
    pixelData[0] = 0xAB;

    var data = new byte[header.Length + pixelData.Length];
    Array.Copy(header, 0, data, 0, header.Length);
    Array.Copy(pixelData, 0, data, header.Length, pixelData.Length);

    using var ms = new MemoryStream(data);
    var result = AndrewToolkitReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesDimensions() {
    var header = Encoding.ASCII.GetBytes("width = 8\nheight = 4\n\n");
    var pixelData = new byte[32];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)i;

    var data = new byte[header.Length + pixelData.Length];
    Array.Copy(header, 0, data, 0, header.Length);
    Array.Copy(pixelData, 0, data, header.Length, pixelData.Length);

    var file = AndrewToolkitReader.FromBytes(data);
    var written = AndrewToolkitWriter.ToBytes(file);
    var reRead = AndrewToolkitReader.FromBytes(written);

    Assert.That(reRead.Width, Is.EqualTo(8));
    Assert.That(reRead.Height, Is.EqualTo(4));
    Assert.That(reRead.RawData, Is.EqualTo(pixelData));
  }
}
