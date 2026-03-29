using System;
using System.IO;
using FileFormat.Pagefox;

namespace FileFormat.Pagefox.Tests;

[TestFixture]
public sealed class PagefoxReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PagefoxReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PagefoxReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pfx"));
    Assert.Throws<FileNotFoundException>(() => PagefoxReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PagefoxReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => PagefoxReader.FromBytes(new byte[1]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Succeeds() {
    var data = new byte[16384];
    data[0] = 0xAB;
    data[16383] = 0xCD;

    var result = PagefoxReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.RawData.Length, Is.EqualTo(16384));
    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
    Assert.That(result.RawData[16383], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[16384];
    data[0] = 0x42;

    using var ms = new MemoryStream(data);
    var result = PagefoxReader.FromStream(ms);

    Assert.That(result.RawData[0], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesData_NotReference() {
    var data = new byte[16384];
    data[0] = 0xFF;

    var result = PagefoxReader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.RawData[0], Is.EqualTo(0xFF));
  }
}
