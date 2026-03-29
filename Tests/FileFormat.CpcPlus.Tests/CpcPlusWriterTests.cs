using System;
using FileFormat.CpcPlus;

namespace FileFormat.CpcPlus.Tests;

[TestFixture]
public sealed class CpcPlusWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcPlusWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidFile_OutputIsExactly16400Bytes() {
    var file = new CpcPlusFile {
      PixelData = new byte[CpcPlusFile.PixelHeight * CpcPlusFile.BytesPerRow],
      PaletteData = new byte[CpcPlusFile.PaletteDataSize],
    };

    var bytes = CpcPlusWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(CpcPlusFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_InterleavesScanlines() {
    var linearData = new byte[CpcPlusFile.PixelHeight * CpcPlusFile.BytesPerRow];
    linearData[0] = 0xAA;
    linearData[CpcPlusFile.BytesPerRow] = 0xBB;

    var file = new CpcPlusFile {
      PixelData = linearData,
      PaletteData = new byte[CpcPlusFile.PaletteDataSize],
    };

    var bytes = CpcPlusWriter.ToBytes(file);

    // Line 0: address = 0
    Assert.That(bytes[0], Is.EqualTo(0xAA));
    // Line 1: address = 2048
    Assert.That(bytes[2048], Is.EqualTo(0xBB));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesPaletteAfterScreenData() {
    var file = new CpcPlusFile {
      PixelData = new byte[CpcPlusFile.PixelHeight * CpcPlusFile.BytesPerRow],
      PaletteData = [0x0F, 0x0A, 0x05, 0x00, 0x0C, 0x08, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
    };

    var bytes = CpcPlusWriter.ToBytes(file);

    Assert.That(bytes[CpcPlusFile.ScreenDataSize], Is.EqualTo(0x0F));
    Assert.That(bytes[CpcPlusFile.ScreenDataSize + 1], Is.EqualTo(0x0A));
    Assert.That(bytes[CpcPlusFile.ScreenDataSize + 2], Is.EqualTo(0x05));
    Assert.That(bytes[CpcPlusFile.ScreenDataSize + 4], Is.EqualTo(0x0C));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Line8_MapsToCorrectOffset() {
    var linearData = new byte[CpcPlusFile.PixelHeight * CpcPlusFile.BytesPerRow];
    linearData[8 * CpcPlusFile.BytesPerRow] = 0xDD;

    var file = new CpcPlusFile {
      PixelData = linearData,
      PaletteData = new byte[CpcPlusFile.PaletteDataSize],
    };

    var bytes = CpcPlusWriter.ToBytes(file);

    // Line 8: address = ((8/8)*80) + ((8%8)*2048) = 80
    Assert.That(bytes[80], Is.EqualTo(0xDD));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyPixelData_ScreenRegionAllZeros() {
    var file = new CpcPlusFile {
      PixelData = [],
      PaletteData = new byte[CpcPlusFile.PaletteDataSize],
    };

    var bytes = CpcPlusWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(CpcPlusFile.ExpectedFileSize));
    for (var i = 0; i < CpcPlusFile.ScreenDataSize; ++i)
      Assert.That(bytes[i], Is.EqualTo(0), $"Byte at offset {i} should be zero");
  }
}
