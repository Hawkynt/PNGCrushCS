using System;
using System.IO;
using FileFormat.C128VDC;

namespace FileFormat.C128VDC.Tests;

[TestFixture]
public sealed class C128VDCReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C128VDCReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C128VDCReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".vdc"));
    Assert.Throws<FileNotFoundException>(() => C128VDCReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => C128VDCReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => C128VDCReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[16001];
    Assert.Throws<InvalidDataException>(() => C128VDCReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Parses() {
    var data = new byte[C128VDCFile.ExpectedFileSize];
    data[0] = 0xAB;
    data[79] = 0xCD;
    data[15999] = 0xEF;

    var result = C128VDCReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.RawData.Length, Is.EqualTo(C128VDCFile.ExpectedFileSize));
    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
    Assert.That(result.RawData[79], Is.EqualTo(0xCD));
    Assert.That(result.RawData[15999], Is.EqualTo(0xEF));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[C128VDCFile.ExpectedFileSize];
    data[0] = 0xAB;

    using var ms = new MemoryStream(data);
    var result = C128VDCReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesData_NotReference() {
    var data = new byte[C128VDCFile.ExpectedFileSize];
    data[0] = 0xFF;

    var result = C128VDCReader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.RawData[0], Is.EqualTo(0xFF));
  }
}
