using System;
using System.IO;
using FileFormat.Cp8Gray;

namespace FileFormat.Cp8Gray.Tests;

[TestFixture]
public sealed class Cp8GrayReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Cp8GrayReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Cp8GrayReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cp8"));
    Assert.Throws<FileNotFoundException>(() => Cp8GrayReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Cp8GrayReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Empty_ThrowsInvalidDataException() {
    var empty = Array.Empty<byte>();
    Assert.Throws<InvalidDataException>(() => Cp8GrayReader.FromBytes(empty));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NotPerfectSquare_ThrowsInvalidDataException() {
    var data = new byte[5];
    Assert.Throws<InvalidDataException>(() => Cp8GrayReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PerfectSquare_ParsesCorrectly() {
    var data = new byte[16];
    data[0] = 0xFF;
    data[15] = 0xAA;

    var result = Cp8GrayReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.PixelData.Length, Is.EqualTo(16));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[15], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SinglePixel_ParsesCorrectly() {
    var data = new byte[] { 0x42 };

    var result = Cp8GrayReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(1));
    Assert.That(result.Height, Is.EqualTo(1));
    Assert.That(result.PixelData[0], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[9];
    data[0] = 0xCD;

    using var ms = new MemoryStream(data);
    var result = Cp8GrayReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(3));
    Assert.That(result.Height, Is.EqualTo(3));
    Assert.That(result.PixelData[0], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesData() {
    var data = new byte[25];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 10);

    var file = Cp8GrayReader.FromBytes(data);
    var written = Cp8GrayWriter.ToBytes(file);
    var reRead = Cp8GrayReader.FromBytes(written);

    Assert.That(reRead.Width, Is.EqualTo(5));
    Assert.That(reRead.Height, Is.EqualTo(5));
    Assert.That(reRead.PixelData, Is.EqualTo(data));
  }
}
