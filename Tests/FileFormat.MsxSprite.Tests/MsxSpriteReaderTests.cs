using System;
using System.IO;
using FileFormat.MsxSprite;

namespace FileFormat.MsxSprite.Tests;

[TestFixture]
public sealed class MsxSpriteReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxSpriteReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxSpriteReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".spt"));
    Assert.Throws<FileNotFoundException>(() => MsxSpriteReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxSpriteReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => MsxSpriteReader.FromBytes(new byte[1]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => MsxSpriteReader.FromBytes(new byte[2049]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[2048];
    data[0] = 0xAB;
    data[2047] = 0xCD;

    var result = MsxSpriteReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(16));
    Assert.That(result.RawData.Length, Is.EqualTo(2048));
    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
    Assert.That(result.RawData[2047], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[2048];
    data[0] = 0x42;

    using var ms = new MemoryStream(data);
    var result = MsxSpriteReader.FromStream(ms);

    Assert.That(result.RawData[0], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesData_NotReference() {
    var data = new byte[2048];
    data[0] = 0xFF;

    var result = MsxSpriteReader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.RawData[0], Is.EqualTo(0xFF));
  }
}
