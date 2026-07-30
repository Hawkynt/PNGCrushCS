using System;
using System.IO;
using FileFormat.AppleIIHgr;

namespace FileFormat.AppleIIHgr.Tests;

[TestFixture]
public sealed class AppleIIHgrReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleIIHgrReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleIIHgrReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hgr"));
    Assert.Throws<FileNotFoundException>(() => AppleIIHgrReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleIIHgrReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => AppleIIHgrReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[8193];
    Assert.Throws<InvalidDataException>(() => AppleIIHgrReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Parses() {
    var data = new byte[8192];
    data[0] = 0x7F;
    data[1] = 0x2A;
    data[8191] = 0xAB;

    var result = AppleIIHgrReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(280));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.RawData.Length, Is.EqualTo(8192));
    Assert.That(result.RawData[0], Is.EqualTo(0x7F));
    Assert.That(result.RawData[1], Is.EqualTo(0x2A));
    Assert.That(result.RawData[8191], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[8192];
    data[0] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = AppleIIHgrReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(280));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesData_NotReference() {
    var data = new byte[8192];
    data[0] = 0xFF;

    var result = AppleIIHgrReader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.RawData[0], Is.EqualTo(0xFF));
  }
}
