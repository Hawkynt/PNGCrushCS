using System;
using System.IO;
using FileFormat.Trs80;

namespace FileFormat.Trs80.Tests;

[TestFixture]
public sealed class Trs80ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Trs80Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Trs80Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hr"));
    Assert.Throws<FileNotFoundException>(() => Trs80Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Trs80Reader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => Trs80Reader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[6145];
    Assert.Throws<InvalidDataException>(() => Trs80Reader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Parses() {
    var data = new byte[6144];
    data[0] = 0x3F;
    data[1] = 0x2A;
    data[6143] = 0xAB;

    var result = Trs80Reader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(144));
    Assert.That(result.RawData.Length, Is.EqualTo(6144));
    Assert.That(result.RawData[0], Is.EqualTo(0x3F));
    Assert.That(result.RawData[1], Is.EqualTo(0x2A));
    Assert.That(result.RawData[6143], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[6144];
    data[0] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = Trs80Reader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(144));
    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesData_NotReference() {
    var data = new byte[6144];
    data[0] = 0xFF;

    var result = Trs80Reader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.RawData[0], Is.EqualTo(0xFF));
  }
}
