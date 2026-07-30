using System;
using System.IO;
using FileFormat.C128Hires;

namespace FileFormat.C128Hires.Tests;

[TestFixture]
public sealed class C128HiresReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C128HiresReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C128HiresReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".c1h"));
    Assert.Throws<FileNotFoundException>(() => C128HiresReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C128HiresReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => C128HiresReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[8001];
    Assert.Throws<InvalidDataException>(() => C128HiresReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Parses() {
    var data = new byte[C128HiresFile.ExpectedFileSize];
    data[0] = 0xAB;
    data[7] = 0xCD;
    data[7999] = 0xEF;

    var result = C128HiresReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.RawData.Length, Is.EqualTo(C128HiresFile.ExpectedFileSize));
    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
    Assert.That(result.RawData[7], Is.EqualTo(0xCD));
    Assert.That(result.RawData[7999], Is.EqualTo(0xEF));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[C128HiresFile.ExpectedFileSize];
    data[0] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = C128HiresReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesData_NotReference() {
    var data = new byte[C128HiresFile.ExpectedFileSize];
    data[0] = 0xFF;

    var result = C128HiresReader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.RawData[0], Is.EqualTo(0xFF));
  }
}
