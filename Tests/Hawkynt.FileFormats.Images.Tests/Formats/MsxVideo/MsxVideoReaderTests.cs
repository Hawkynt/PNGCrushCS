using System;
using System.IO;
using FileFormat.MsxVideo;

namespace FileFormat.MsxVideo.Tests;

[TestFixture]
public sealed class MsxVideoReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxVideoReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxVideoReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mvi"));
    Assert.Throws<FileNotFoundException>(() => MsxVideoReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxVideoReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => MsxVideoReader.FromBytes(new byte[1]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => MsxVideoReader.FromBytes(new byte[54273]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[54272];
    data[0] = 0xAB;
    data[54271] = 0xCD;

    var result = MsxVideoReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(212));
    Assert.That(result.PixelData.Length, Is.EqualTo(54272));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
    Assert.That(result.PixelData[54271], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[54272];
    data[0] = 0x42;

    using var ms = new MemoryStream(data);
    var result = MsxVideoReader.FromStream(ms);

    Assert.That(result.PixelData[0], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesData_NotReference() {
    var data = new byte[54272];
    data[0] = 0xFF;

    var result = MsxVideoReader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
  }
}
