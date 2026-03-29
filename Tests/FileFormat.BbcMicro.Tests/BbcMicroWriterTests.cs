using System;
using FileFormat.BbcMicro;

namespace FileFormat.BbcMicro.Tests;

[TestFixture]
public sealed class BbcMicroWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BbcMicroWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Mode1_OutputIsExactly20480Bytes() {
    var file = new BbcMicroFile {
      Width = 320,
      Height = 256,
      Mode = BbcMicroMode.Mode1,
      PixelData = new byte[256 * 80]
    };

    var bytes = BbcMicroWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(BbcMicroFile.ScreenSizeModes012));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Mode0_OutputIsExactly20480Bytes() {
    var file = new BbcMicroFile {
      Width = 640,
      Height = 256,
      Mode = BbcMicroMode.Mode0,
      PixelData = new byte[256 * 80]
    };

    var bytes = BbcMicroWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(BbcMicroFile.ScreenSizeModes012));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Mode4_OutputIsExactly10240Bytes() {
    var file = new BbcMicroFile {
      Width = 320,
      Height = 256,
      Mode = BbcMicroMode.Mode4,
      PixelData = new byte[256 * 40]
    };

    var bytes = BbcMicroWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(BbcMicroFile.ScreenSizeModes45));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Mode5_OutputIsExactly10240Bytes() {
    var file = new BbcMicroFile {
      Width = 160,
      Height = 256,
      Mode = BbcMicroMode.Mode5,
      PixelData = new byte[256 * 40]
    };

    var bytes = BbcMicroWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(BbcMicroFile.ScreenSizeModes45));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CharacterBlockLayout_BytesArrangedCorrectly() {
    var charCols = 80;
    var linearData = new byte[256 * charCols];
    // Write a recognizable byte at scanline 0, column 0
    linearData[0] = 0xAA;
    // Write a recognizable byte at scanline 1, column 0
    linearData[charCols] = 0xBB;

    var file = new BbcMicroFile {
      Width = 320,
      Height = 256,
      Mode = BbcMicroMode.Mode1,
      PixelData = linearData
    };

    var bytes = BbcMicroWriter.ToBytes(file);

    // Character (col=0, row=0) starts at offset 0
    // Byte 0 within that character = pixel row 0, byte 1 = pixel row 1
    Assert.That(bytes[0], Is.EqualTo(0xAA));
    Assert.That(bytes[1], Is.EqualTo(0xBB));
  }
}
