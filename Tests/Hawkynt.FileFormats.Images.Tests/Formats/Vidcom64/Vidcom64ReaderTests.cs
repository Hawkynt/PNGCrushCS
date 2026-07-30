using System;
using System.IO;
using FileFormat.Vidcom64;

namespace FileFormat.Vidcom64.Tests;

[TestFixture]
public sealed class Vidcom64ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Vidcom64Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Vidcom64Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".vid"));
    Assert.Throws<FileNotFoundException>(() => Vidcom64Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Vidcom64Reader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => Vidcom64Reader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => Vidcom64Reader.FromBytes(new byte[10051]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = new byte[Vidcom64File.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x58;
    // Header starts at offset 2, bitmap at 49, screen at 8049, color at 9049, bg at 10049
    data[10049] = 0x06;

    var result = Vidcom64Reader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x5800));
    Assert.That(result.HeaderData.Length, Is.EqualTo(47));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.ScreenRam.Length, Is.EqualTo(1000));
    Assert.That(result.ColorRam.Length, Is.EqualTo(1000));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x06));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_HeaderData_CopiedCorrectly() {
    var data = new byte[Vidcom64File.ExpectedFileSize];
    data[2] = 0xAA;
    data[48] = 0xBB;

    var result = Vidcom64Reader.FromBytes(data);

    Assert.That(result.HeaderData[0], Is.EqualTo(0xAA));
    Assert.That(result.HeaderData[46], Is.EqualTo(0xBB));
  }
}
