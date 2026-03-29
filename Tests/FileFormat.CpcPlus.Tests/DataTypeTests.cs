using System;
using FileFormat.Core;
using FileFormat.CpcPlus;

namespace FileFormat.CpcPlus.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_DefaultWidth_Is320() {
    var file = new CpcPlusFile();

    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_DefaultHeight_Is200() {
    var file = new CpcPlusFile();

    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_DefaultPixelData_IsEmpty() {
    var file = new CpcPlusFile();

    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_DefaultPaletteData_IsEmpty() {
    var file = new CpcPlusFile();

    Assert.That(file.PaletteData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_InitPixelData_StoresCorrectly() {
    var data = new byte[] { 0x3F, 0x2A };
    var file = new CpcPlusFile { PixelData = data };

    Assert.That(file.PixelData, Is.SameAs(data));
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_InitPaletteData_StoresCorrectly() {
    var palette = new byte[] { 0x0F, 0x0A, 0x05, 0x00 };
    var file = new CpcPlusFile { PaletteData = palette };

    Assert.That(file.PaletteData, Is.SameAs(palette));
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_ScreenDataSize_Is16384() {
    Assert.That(CpcPlusFile.ScreenDataSize, Is.EqualTo(16384));
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_PaletteDataSize_Is16() {
    Assert.That(CpcPlusFile.PaletteDataSize, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_ExpectedFileSize_Is16400() {
    Assert.That(CpcPlusFile.ExpectedFileSize, Is.EqualTo(16400));
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_ExpectedFileSize_EqualsScreenPlusPalette() {
    Assert.That(CpcPlusFile.ExpectedFileSize, Is.EqualTo(CpcPlusFile.ScreenDataSize + CpcPlusFile.PaletteDataSize));
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_PixelWidth_Is320() {
    Assert.That(CpcPlusFile.PixelWidth, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_PixelHeight_Is200() {
    Assert.That(CpcPlusFile.PixelHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_BytesPerRow_Is80() {
    Assert.That(CpcPlusFile.BytesPerRow, Is.EqualTo(80));
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_PixelsPerByte_Is4() {
    Assert.That(CpcPlusFile.PixelsPerByte, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_PaletteEntries_Is4() {
    Assert.That(CpcPlusFile.PaletteEntries, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcPlusFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcPlusFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 200 * 3],
    };

    Assert.Throws<NotSupportedException>(() => CpcPlusFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_ToRawImage_ReturnsRgb24Format() {
    var file = new CpcPlusFile {
      PixelData = new byte[CpcPlusFile.PixelHeight * CpcPlusFile.BytesPerRow],
      PaletteData = new byte[CpcPlusFile.PaletteDataSize],
    };

    var raw = CpcPlusFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_ToRawImage_HasCorrectDimensions() {
    var file = new CpcPlusFile {
      PixelData = new byte[CpcPlusFile.PixelHeight * CpcPlusFile.BytesPerRow],
      PaletteData = new byte[CpcPlusFile.PaletteDataSize],
    };

    var raw = CpcPlusFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_ToRawImage_PixelDataSize() {
    var file = new CpcPlusFile {
      PixelData = new byte[CpcPlusFile.PixelHeight * CpcPlusFile.BytesPerRow],
      PaletteData = new byte[CpcPlusFile.PaletteDataSize],
    };

    var raw = CpcPlusFile.ToRawImage(file);

    Assert.That(raw.PixelData.Length, Is.EqualTo(320 * 200 * 3));
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_ToRawImage_PaletteExpands12BitToRgb() {
    var paletteData = new byte[CpcPlusFile.PaletteDataSize];
    // Entry 0: R=0x0F (15*17=255), G=0x0A (10*17=170), B=0x05 (5*17=85)
    paletteData[0] = 0x0F;
    paletteData[1] = 0x0A;
    paletteData[2] = 0x05;
    paletteData[3] = 0x00;

    var file = new CpcPlusFile {
      PixelData = new byte[CpcPlusFile.PixelHeight * CpcPlusFile.BytesPerRow],
      PaletteData = paletteData,
    };

    var raw = CpcPlusFile.ToRawImage(file);

    // All zero pixel data means index 0 everywhere, so first pixel should be palette entry 0
    Assert.That(raw.PixelData[0], Is.EqualTo(255));   // R: 0x0F * 17 = 255
    Assert.That(raw.PixelData[1], Is.EqualTo(170));   // G: 0x0A * 17 = 170
    Assert.That(raw.PixelData[2], Is.EqualTo(85));    // B: 0x05 * 17 = 85
  }

  [Test]
  [Category("Unit")]
  public void CpcPlusFile_ToRawImage_ClonesPixelData() {
    var file = new CpcPlusFile {
      PixelData = new byte[CpcPlusFile.PixelHeight * CpcPlusFile.BytesPerRow],
      PaletteData = new byte[CpcPlusFile.PaletteDataSize],
    };

    var raw1 = CpcPlusFile.ToRawImage(file);
    var raw2 = CpcPlusFile.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }
}
