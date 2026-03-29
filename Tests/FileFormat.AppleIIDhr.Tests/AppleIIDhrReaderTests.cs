using System;
using System.IO;
using FileFormat.AppleIIDhr;

namespace FileFormat.AppleIIDhr.Tests;

[TestFixture]
public sealed class AppleIIDhrReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleIIDhrReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleIIDhrReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dhr"));
    Assert.Throws<FileNotFoundException>(() => AppleIIDhrReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleIIDhrReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => AppleIIDhrReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[16385];
    Assert.Throws<InvalidDataException>(() => AppleIIDhrReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Parses() {
    var data = new byte[16384];
    data[0] = 0x7F;
    data[8192] = 0x55;
    data[16383] = 0xAB;

    var result = AppleIIDhrReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(560));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.RawData.Length, Is.EqualTo(16384));
    Assert.That(result.RawData[0], Is.EqualTo(0x7F));
    Assert.That(result.RawData[8192], Is.EqualTo(0x55));
    Assert.That(result.RawData[16383], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[16384];
    data[0] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = AppleIIDhrReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(560));
    Assert.That(result.Height, Is.EqualTo(192));
    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesData_NotReference() {
    var data = new byte[16384];
    data[0] = 0xFF;

    var result = AppleIIDhrReader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.RawData[0], Is.EqualTo(0xFF));
  }
}
