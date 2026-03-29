using System;
using System.IO;
using FileFormat.Picasso64;

namespace FileFormat.Picasso64.Tests;

[TestFixture]
public sealed class Picasso64ReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Picasso64Reader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Picasso64Reader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".p64"));
    Assert.Throws<FileNotFoundException>(() => Picasso64Reader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Picasso64Reader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => Picasso64Reader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongSize_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => Picasso64Reader.FromBytes(new byte[10051]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var data = new byte[Picasso64File.ExpectedFileSize];
    data[0] = 0x00;
    data[1] = 0x60;
    // bitmap at 2, screen at 8002, color at 9002, bg at 10002, border at 10003
    data[10002] = 0x03;
    data[10003] = 0x0E;

    var result = Picasso64Reader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x6000));
    Assert.That(result.BitmapData.Length, Is.EqualTo(8000));
    Assert.That(result.ScreenRam.Length, Is.EqualTo(1000));
    Assert.That(result.ColorRam.Length, Is.EqualTo(1000));
    Assert.That(result.BackgroundColor, Is.EqualTo(0x03));
    Assert.That(result.BorderColor, Is.EqualTo(0x0E));
    Assert.That(result.ExtraData.Length, Is.EqualTo(46));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BorderColor_ParsedCorrectly() {
    var data = new byte[Picasso64File.ExpectedFileSize];
    data[10003] = 0x07;

    var result = Picasso64Reader.FromBytes(data);

    Assert.That(result.BorderColor, Is.EqualTo(0x07));
  }
}
