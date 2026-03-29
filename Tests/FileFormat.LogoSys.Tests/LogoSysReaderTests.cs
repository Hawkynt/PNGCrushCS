using System;
using System.IO;
using FileFormat.LogoSys;

namespace FileFormat.LogoSys.Tests;

[TestFixture]
public sealed class LogoSysReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => LogoSysReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => LogoSysReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sys"));
    Assert.Throws<FileNotFoundException>(() => LogoSysReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => LogoSysReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[100];
    Assert.Throws<InvalidDataException>(() => LogoSysReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooLarge_ThrowsInvalidDataException() {
    var tooLarge = new byte[128769];
    Assert.Throws<InvalidDataException>(() => LogoSysReader.FromBytes(tooLarge));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ExactSize_Parses() {
    var data = new byte[128768];
    data[0] = 0xAA; // first palette byte
    data[767] = 0xBB; // last palette byte
    data[768] = 0xCC; // first pixel byte
    data[128767] = 0xDD; // last pixel byte

    var result = LogoSysReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(400));
    Assert.That(result.Palette.Length, Is.EqualTo(768));
    Assert.That(result.PixelData.Length, Is.EqualTo(128000));
    Assert.That(result.Palette[0], Is.EqualTo(0xAA));
    Assert.That(result.Palette[767], Is.EqualTo(0xBB));
    Assert.That(result.PixelData[0], Is.EqualTo(0xCC));
    Assert.That(result.PixelData[127999], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid() {
    var data = new byte[128768];
    data[0] = 0xAB;
    data[768] = 0xCD;

    using var ms = new MemoryStream(data);
    var result = LogoSysReader.FromStream(ms);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(400));
    Assert.That(result.Palette[0], Is.EqualTo(0xAB));
    Assert.That(result.PixelData[0], Is.EqualTo(0xCD));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesPalette_NotReference() {
    var data = new byte[128768];
    data[0] = 0xFF;

    var result = LogoSysReader.FromBytes(data);
    data[0] = 0x00;

    Assert.That(result.Palette[0], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_CopiesPixelData_NotReference() {
    var data = new byte[128768];
    data[768] = 0xFF;

    var result = LogoSysReader.FromBytes(data);
    data[768] = 0x00;

    Assert.That(result.PixelData[0], Is.EqualTo(0xFF));
  }
}
