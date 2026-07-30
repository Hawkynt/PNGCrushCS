using System;
using System.IO;
using FileFormat.CpcFont;

namespace FileFormat.CpcFont.Tests;

[TestFixture]
public sealed class CpcFontReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcFontReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcFontReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpf"));
    Assert.Throws<FileNotFoundException>(() => CpcFontReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcFontReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => CpcFontReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[2049];
    Assert.Throws<InvalidDataException>(() => CpcFontReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Parses() {
    var data = new byte[2048];
    data[0] = 0x3F;
    data[1] = 0x2A;
    data[2047] = 0xAB;

    var result = CpcFontReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(128));
    Assert.That(result.RawData.Length, Is.EqualTo(2048));
    Assert.That(result.RawData[0], Is.EqualTo(0x3F));
    Assert.That(result.RawData[1], Is.EqualTo(0x2A));
    Assert.That(result.RawData[2047], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[2048];
    data[0] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = CpcFontReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(128));
    Assert.That(result.Height, Is.EqualTo(128));
    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesData_NotReference() {
    var data = new byte[2048];
    data[0] = 0xFF;

    var result = CpcFontReader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.RawData[0], Is.EqualTo(0xFF));
  }
}
