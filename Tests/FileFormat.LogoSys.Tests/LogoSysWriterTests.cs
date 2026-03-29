using System;
using FileFormat.LogoSys;

namespace FileFormat.LogoSys.Tests;

[TestFixture]
public sealed class LogoSysWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Produces128768Bytes() {
    var file = new LogoSysFile {
      Palette = new byte[768],
      PixelData = new byte[128000],
    };

    var bytes = LogoSysWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(128768));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => LogoSysWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PaletteFirst_ThenPixelData() {
    var palette = new byte[768];
    palette[0] = 0xAA;
    palette[767] = 0xBB;

    var pixelData = new byte[128000];
    pixelData[0] = 0xCC;
    pixelData[127999] = 0xDD;

    var file = new LogoSysFile {
      Palette = palette,
      PixelData = pixelData,
    };

    var bytes = LogoSysWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xAA));
    Assert.That(bytes[767], Is.EqualTo(0xBB));
    Assert.That(bytes[768], Is.EqualTo(0xCC));
    Assert.That(bytes[128767], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ShortPalette_PadsWithZeros() {
    var file = new LogoSysFile {
      Palette = new byte[10],
      PixelData = new byte[128000],
    };

    var bytes = LogoSysWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(128768));
    Assert.That(bytes[10], Is.EqualTo(0x00));
    Assert.That(bytes[767], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ShortPixelData_PadsWithZeros() {
    var file = new LogoSysFile {
      Palette = new byte[768],
      PixelData = new byte[10],
    };

    var bytes = LogoSysWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(128768));
    Assert.That(bytes[778], Is.EqualTo(0x00));
    Assert.That(bytes[128767], Is.EqualTo(0x00));
  }
}
