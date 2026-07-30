using System;
using System.IO;
using FileFormat.Oric;

namespace FileFormat.Oric.Tests;

[TestFixture]
public sealed class OricReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => OricReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => OricReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".oric"));
    Assert.Throws<FileNotFoundException>(() => OricReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => OricReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    var wrongSize = new byte[8001];
    Assert.Throws<InvalidDataException>(() => OricReader.FromBytes(wrongSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Valid_Parses() {
    var data = new byte[8000];
    data[0] = 0x3F;
    data[1] = 0x40;
    data[7999] = 0xAB;

    var result = OricReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(240));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.ScreenData.Length, Is.EqualTo(8000));
    Assert.That(result.ScreenData[0], Is.EqualTo(0x3F));
    Assert.That(result.ScreenData[1], Is.EqualTo(0x40));
    Assert.That(result.ScreenData[7999], Is.EqualTo(0xAB));
  }
}
