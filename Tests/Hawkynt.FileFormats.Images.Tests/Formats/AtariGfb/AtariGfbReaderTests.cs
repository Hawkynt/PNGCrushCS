using System;
using System.IO;
using FileFormat.AtariGfb;

namespace FileFormat.AtariGfb.Tests;

[TestFixture]
public sealed class AtariGfbReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_NullFile_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariGfbReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_MissingFile_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gfb"));
    Assert.Throws<FileNotFoundException>(() => AtariGfbReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_NullStream_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariGfbReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NullData_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AtariGfbReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[7679];
    Assert.Throws<InvalidDataException>(() => AtariGfbReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[7681];
    Assert.Throws<InvalidDataException>(() => AtariGfbReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_ParsesSuccessfully() {
    var data = new byte[7680];
    data[0] = 0xAB;
    data[1] = 0xCD;
    data[7679] = 0xEF;

    var result = AtariGfbReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.RawData.Length, Is.EqualTo(7680));
    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
    Assert.That(result.RawData[1], Is.EqualTo(0xCD));
    Assert.That(result.RawData[7679], Is.EqualTo(0xEF));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesSuccessfully() {
    var data = new byte[7680];
    data[0] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = AtariGfbReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DataIsCopied_NotSameReference() {
    var data = new byte[7680];
    data[0] = 0xFF;

    var result = AtariGfbReader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.RawData[0], Is.EqualTo(0xFF));
  }
}
