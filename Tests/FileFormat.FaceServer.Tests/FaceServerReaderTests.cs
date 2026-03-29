using System;
using System.IO;
using FileFormat.FaceServer;

namespace FileFormat.FaceServer.Tests;

[TestFixture]
public sealed class FaceServerReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FaceServerReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FaceServerReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fac"));
    Assert.Throws<FileNotFoundException>(() => FaceServerReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FaceServerReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => FaceServerReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactPixelData_ParsesCorrectly() {
    var data = new byte[FaceServerFile.PixelCount];
    data[0] = 0xFF;
    data[1] = 0x80;
    data[2] = 0x40;

    var result = FaceServerReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(48));
    Assert.That(result.Height, Is.EqualTo(48));
    Assert.That(result.PixelData.Length, Is.EqualTo(FaceServerFile.PixelCount));
    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(result.PixelData[1], Is.EqualTo(0x80));
    Assert.That(result.PixelData[2], Is.EqualTo(0x40));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[FaceServerFile.PixelCount];
    data[0] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = FaceServerReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(48));
    Assert.That(result.Height, Is.EqualTo(48));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesPixelData() {
    var data = new byte[FaceServerFile.PixelCount];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i & 0xFF);

    var file = FaceServerReader.FromBytes(data);
    var written = FaceServerWriter.ToBytes(file);
    var reRead = FaceServerReader.FromBytes(written);

    Assert.That(reRead.Width, Is.EqualTo(48));
    Assert.That(reRead.Height, Is.EqualTo(48));
    Assert.That(reRead.PixelData, Is.EqualTo(data));
  }
}
