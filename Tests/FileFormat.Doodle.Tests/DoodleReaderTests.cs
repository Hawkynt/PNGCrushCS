using System;
using System.IO;
using FileFormat.Doodle;

namespace FileFormat.Doodle.Tests;

[TestFixture]
public sealed class DoodleReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DoodleReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DoodleReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dd"));
    Assert.Throws<FileNotFoundException>(() => DoodleReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DoodleReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => DoodleReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => DoodleReader.FromBytes(new byte[9219]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = new byte[DoodleFile.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x5C; // 0x5C00 LE

    var result = DoodleReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x5C00));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.ScreenRam.Length, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_LoadAddress_ParsedAsLittleEndian() {
    var data = new byte[DoodleFile.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x5C;

    var result = DoodleReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x5C00));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BitmapData_CopiedCorrectly() {
    var data = new byte[DoodleFile.ExpectedFileSize];
    data[2] = 0xAB;
    data[8001] = 0xCD;

    var result = DoodleReader.FromBytes(data);

    Assert.That(result.BitmapData[0], Is.EqualTo(0xAB));
    Assert.That(result.BitmapData[7999], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    var data = new byte[DoodleFile.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x5C;

    using var ms = new MemoryStream(data);
    var result = DoodleReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x5C00));
  }
}
