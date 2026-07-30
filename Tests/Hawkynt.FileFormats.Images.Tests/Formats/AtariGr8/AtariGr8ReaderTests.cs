using System;
using System.IO;
using FileFormat.AtariGr8;

namespace FileFormat.AtariGr8.Tests;

[TestFixture]
public sealed class AtariGr8ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariGr8Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariGr8Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gr8"));
    Assert.Throws<FileNotFoundException>(() => AtariGr8Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariGr8Reader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => AtariGr8Reader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[7681];
    Assert.Throws<InvalidDataException>(() => AtariGr8Reader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Parses() {
    var data = new byte[7680];
    data[0] = 0xAB;
    data[1] = 0xCD;
    data[7679] = 0xEF;

    var result = AtariGr8Reader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.RawData.Length, Is.EqualTo(7680));
    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
    Assert.That(result.RawData[1], Is.EqualTo(0xCD));
    Assert.That(result.RawData[7679], Is.EqualTo(0xEF));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[7680];
    data[0] = 0x42;

    using var ms = new MemoryStream(data);
    var result = AtariGr8Reader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.RawData[0], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesData_NotReference() {
    var data = new byte[7680];
    data[0] = 0xFF;

    var result = AtariGr8Reader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.RawData[0], Is.EqualTo(0xFF));
  }
}
