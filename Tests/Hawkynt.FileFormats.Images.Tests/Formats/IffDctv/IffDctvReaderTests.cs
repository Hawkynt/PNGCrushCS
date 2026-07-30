using System;
using System.IO;
using FileFormat.IffDctv;

namespace FileFormat.IffDctv.Tests;

[TestFixture]
public sealed class IffDctvReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffDctvReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffDctvReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dctv"));
    Assert.Throws<FileNotFoundException>(() => IffDctvReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffDctvReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => IffDctvReader.FromBytes(new byte[1]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MinSize_Succeeds() {
    var data = new byte[12];
    data[0] = 0xAB;

    var result = IffDctvReader.FromBytes(data);

    Assert.That(result.RawData.Length, Is.EqualTo(12));
    Assert.That(result.RawData[0], Is.EqualTo(0xAB));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_DefaultDimensions_WhenNoBmhd() {
    var data = new byte[20];

    var result = IffDctvReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[16];
    data[0] = 0x42;

    using var ms = new MemoryStream(data);
    var result = IffDctvReader.FromStream(ms);

    Assert.That(result.RawData[0], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesData_NotReference() {
    var data = new byte[12];
    data[0] = 0xFF;

    var result = IffDctvReader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.RawData[0], Is.EqualTo(0xFF));
  }
}
