using System;
using System.IO;
using System.Text;
using FileFormat.Mtv;

namespace FileFormat.Mtv.Tests;

[TestFixture]
public sealed class MtvReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MtvReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MtvReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mtv"));
    Assert.Throws<FileNotFoundException>(() => MtvReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MtvReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_NoNewline_ThrowsInvalidDataException() {
    var data = Encoding.ASCII.GetBytes("2 2");
    Assert.Throws<InvalidDataException>(() => MtvReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoNewline_Throws() {
    var data = Encoding.ASCII.GetBytes("100 200");
    Assert.Throws<InvalidDataException>(() => MtvReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidDimensions_Throws() {
    var data = Encoding.ASCII.GetBytes("0 5\n");
    Assert.Throws<InvalidDataException>(() => MtvReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid() {
    var header = Encoding.ASCII.GetBytes("2 1\n");
    var pixels = new byte[] { 255, 0, 0, 0, 255, 0 };
    var data = new byte[header.Length + pixels.Length];
    Array.Copy(header, 0, data, 0, header.Length);
    Array.Copy(pixels, 0, data, header.Length, pixels.Length);

    var result = MtvReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var header = Encoding.ASCII.GetBytes("1 1\n");
    var pixels = new byte[] { 42, 84, 126 };
    var data = new byte[header.Length + pixels.Length];
    Array.Copy(header, 0, data, 0, header.Length);
    Array.Copy(pixels, 0, data, header.Length, pixels.Length);

    using var ms = new MemoryStream(data);
    var result = MtvReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(1));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData, Is.EqualTo(pixels));
  }
}
