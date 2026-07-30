using System;
using System.IO;
using System.Text;
using FileFormat.Xpm;

namespace FileFormat.Xpm.Tests;

[TestFixture]
public sealed class XpmReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XpmReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XpmReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var file = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xpm"));
    Assert.Throws<FileNotFoundException>(() => XpmReader.FromFile(file));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XpmReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var data = new byte[5];
    Assert.Throws<InvalidDataException>(() => XpmReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var data = Encoding.UTF8.GetBytes("/* NOT XPM */\nstatic char *img[] = {};");
    Assert.Throws<InvalidDataException>(() => XpmReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var xpm = """
              /* XPM */
              static char *test[] = {
              "2 2 2 1",
              ". c #FF0000",
              "# c #00FF00",
              ".#",
              "#."
              };
              """;
    var data = Encoding.UTF8.GetBytes(xpm);
    var result = XpmReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(2));
    Assert.That(result.Height, Is.EqualTo(2));
    Assert.That(result.PaletteColorCount, Is.EqualTo(2));
    Assert.That(result.CharsPerPixel, Is.EqualTo(1));
    Assert.That(result.Palette[0], Is.EqualTo(0xFF), "R of first color");
    Assert.That(result.Palette[1], Is.EqualTo(0x00), "G of first color");
    Assert.That(result.Palette[2], Is.EqualTo(0x00), "B of first color");
    Assert.That(result.Palette[3], Is.EqualTo(0x00), "R of second color");
    Assert.That(result.Palette[4], Is.EqualTo(0xFF), "G of second color");
    Assert.That(result.Palette[5], Is.EqualTo(0x00), "B of second color");
    Assert.That(result.PixelData[0], Is.EqualTo(0));
    Assert.That(result.PixelData[1], Is.EqualTo(1));
    Assert.That(result.PixelData[2], Is.EqualTo(1));
    Assert.That(result.PixelData[3], Is.EqualTo(0));
  }
}
