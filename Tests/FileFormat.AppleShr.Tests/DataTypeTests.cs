using System;
using FileFormat.Core;
using FileFormat.AppleShr;

namespace FileFormat.AppleShr.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void AppleShrFile_DefaultWidth_Is320() {
    var file = new AppleShrFile();

    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_DefaultHeight_Is200() {
    var file = new AppleShrFile();

    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_DefaultPixelData_IsEmpty() {
    var file = new AppleShrFile();

    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_DefaultScanlineControl_IsEmpty() {
    var file = new AppleShrFile();

    Assert.That(file.ScanlineControl, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_DefaultPalette_IsEmpty() {
    var file = new AppleShrFile();

    Assert.That(file.Palette, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_InitPixelData_StoresCorrectly() {
    var data = new byte[] { 0x12, 0x34 };
    var file = new AppleShrFile { PixelData = data };

    Assert.That(file.PixelData, Is.SameAs(data));
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_InitScanlineControl_StoresCorrectly() {
    var data = new byte[] { 0x05, 0x0A };
    var file = new AppleShrFile { ScanlineControl = data };

    Assert.That(file.ScanlineControl, Is.SameAs(data));
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_InitPalette_StoresCorrectly() {
    var data = new byte[] { 0xAB, 0xCD };
    var file = new AppleShrFile { Palette = data };

    Assert.That(file.Palette, Is.SameAs(data));
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_FixedWidth_Is320() {
    Assert.That(AppleShrFile.FixedWidth, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_FixedHeight_Is200() {
    Assert.That(AppleShrFile.FixedHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_ExpectedFileSize_Is32768() {
    Assert.That(AppleShrFile.ExpectedFileSize, Is.EqualTo(32768));
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_SectionSizes_SumToExpectedFileSize() {
    var sum = AppleShrFile.PixelDataSize + AppleShrFile.ScbSize + AppleShrFile.PaddingSize + AppleShrFile.PaletteSize;
    Assert.That(sum, Is.EqualTo(AppleShrFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_PixelDataSize_Is32000() {
    Assert.That(AppleShrFile.PixelDataSize, Is.EqualTo(32000));
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_ScbSize_Is200() {
    Assert.That(AppleShrFile.ScbSize, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_PaddingSize_Is56() {
    Assert.That(AppleShrFile.PaddingSize, Is.EqualTo(56));
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_PaletteSize_Is512() {
    Assert.That(AppleShrFile.PaletteSize, Is.EqualTo(512));
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleShrFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleShrFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 200 * 3],
    };

    Assert.Throws<NotSupportedException>(() => AppleShrFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_ToRawImage_ReturnsRgb24Format() {
    var file = new AppleShrFile {
      PixelData = new byte[32000],
      ScanlineControl = new byte[200],
      Palette = new byte[512]
    };

    var raw = AppleShrFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_ToRawImage_HasCorrectDimensions() {
    var file = new AppleShrFile {
      PixelData = new byte[32000],
      ScanlineControl = new byte[200],
      Palette = new byte[512]
    };

    var raw = AppleShrFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(320));
    Assert.That(raw.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_ToRawImage_PixelDataSize() {
    var file = new AppleShrFile {
      PixelData = new byte[32000],
      ScanlineControl = new byte[200],
      Palette = new byte[512]
    };

    var raw = AppleShrFile.ToRawImage(file);

    Assert.That(raw.PixelData.Length, Is.EqualTo(320 * 200 * 3));
  }

  [Test]
  [Category("Unit")]
  public void AppleShrFile_ToRawImage_PaletteColorApplied() {
    var file = new AppleShrFile {
      PixelData = new byte[32000],
      ScanlineControl = new byte[200],
      Palette = new byte[512]
    };

    // Set palette entry 0 in palette 0 to 0x0FFF (white in 4-bit RGB)
    // Palette offset for palette 0, entry 0 = 0
    file.Palette[0] = 0xFF; // low byte
    file.Palette[1] = 0x0F; // high byte: 0x0FFF -> R=F, G=F, B=F

    // Pixel (0,0) uses high nibble of PixelData[0], which is 0 -> color index 0
    // Scanline 0 uses palette (scb[0] & 0x0F) = 0

    var raw = AppleShrFile.ToRawImage(file);

    // R = 0x0F * 17 = 255, G = 0x0F * 17 = 255, B = 0x0F * 17 = 255
    Assert.That(raw.PixelData[0], Is.EqualTo(255)); // R
    Assert.That(raw.PixelData[1], Is.EqualTo(255)); // G
    Assert.That(raw.PixelData[2], Is.EqualTo(255)); // B
  }
}
