using System;
using System.IO;
using FileFormat.CoCoMax;

namespace FileFormat.CoCoMax.Tests;

[TestFixture]
public sealed class CoCoMaxReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CoCoMaxReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CoCoMaxReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".max"));
    Assert.Throws<FileNotFoundException>(() => CoCoMaxReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CoCoMaxReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => CoCoMaxReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[6145];
    Assert.Throws<InvalidDataException>(() => CoCoMaxReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Parses() {
    var data = new byte[6144];
    data[0] = 0x3F;
    data[1] = 0x2A;
    data[6143] = 0xAB;

    var result = CoCoMaxReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
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
    var result = CoCoMaxReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(256));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesData_NotReference() {
    var data = new byte[6144];
    data[0] = 0xFF;

    var result = CoCoMaxReader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.RawData[0], Is.EqualTo(0xFF));
  }
}
