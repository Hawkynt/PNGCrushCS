using System;
using FileFormat.Core;
using FileFormat.CpcFont;

namespace FileFormat.CpcFont.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void CpcFontFile_DefaultWidth_Is128() {
    var file = new CpcFontFile();

    Assert.That(file.Width, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_DefaultHeight_Is128() {
    var file = new CpcFontFile();

    Assert.That(file.Height, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_DefaultRawData_IsEmpty() {
    var file = new CpcFontFile();

    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_InitRawData_StoresCorrectly() {
    var rawData = new byte[] { 0x3F, 0x2A };
    var file = new CpcFontFile { RawData = rawData };

    Assert.That(file.RawData, Is.SameAs(rawData));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_ExpectedFileSize_Is2048() {
    Assert.That(CpcFontFile.ExpectedFileSize, Is.EqualTo(2048));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_CharCount_Is256() {
    Assert.That(CpcFontFile.CharCount, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_BytesPerChar_Is8() {
    Assert.That(CpcFontFile.BytesPerChar, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_CharWidth_Is8() {
    Assert.That(CpcFontFile.CharWidth, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_CharHeight_Is8() {
    Assert.That(CpcFontFile.CharHeight, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_CharsPerRow_Is16() {
    Assert.That(CpcFontFile.CharsPerRow, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_GridRows_Is16() {
    Assert.That(CpcFontFile.GridRows, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_PixelWidth_Is128() {
    Assert.That(CpcFontFile.PixelWidth, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_PixelHeight_Is128() {
    Assert.That(CpcFontFile.PixelHeight, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_CharCountTimesBytesPerChar_EqualsExpectedFileSize() {
    Assert.That(CpcFontFile.CharCount * CpcFontFile.BytesPerChar, Is.EqualTo(CpcFontFile.ExpectedFileSize));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_GridLayout_MatchesPixelDimensions() {
    Assert.That(CpcFontFile.CharsPerRow * CpcFontFile.CharWidth, Is.EqualTo(CpcFontFile.PixelWidth));
    Assert.That(CpcFontFile.GridRows * CpcFontFile.CharHeight, Is.EqualTo(CpcFontFile.PixelHeight));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcFontFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcFontFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 128,
      Height = 128,
      Format = PixelFormat.Indexed1,
      PixelData = new byte[128 / 8 * 128],
    };

    Assert.Throws<NotSupportedException>(() => CpcFontFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_ToRawImage_ReturnsIndexed1Format() {
    var file = new CpcFontFile { RawData = new byte[2048] };

    var raw = CpcFontFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_ToRawImage_HasCorrectDimensions() {
    var file = new CpcFontFile { RawData = new byte[2048] };

    var raw = CpcFontFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(128));
    Assert.That(raw.Height, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_ToRawImage_HasBlackWhitePalette() {
    var file = new CpcFontFile { RawData = new byte[2048] };

    var raw = CpcFontFile.ToRawImage(file);

    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.PaletteCount, Is.EqualTo(2));
    Assert.That(raw.Palette![0], Is.EqualTo(0));
    Assert.That(raw.Palette[1], Is.EqualTo(0));
    Assert.That(raw.Palette[2], Is.EqualTo(0));
    Assert.That(raw.Palette[3], Is.EqualTo(255));
    Assert.That(raw.Palette[4], Is.EqualTo(255));
    Assert.That(raw.Palette[5], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_ToRawImage_PixelDataSize() {
    var file = new CpcFontFile { RawData = new byte[2048] };

    var raw = CpcFontFile.ToRawImage(file);

    // 128 pixels / 8 bits per byte = 16 bytes per row, 128 rows
    Assert.That(raw.PixelData.Length, Is.EqualTo(16 * 128));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_ToRawImage_ClonesPixelData() {
    var rawData = new byte[2048];
    rawData[0] = 0xFF;
    var file = new CpcFontFile { RawData = rawData };

    var raw1 = CpcFontFile.ToRawImage(file);
    var raw2 = CpcFontFile.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void CpcFontFile_ToRawImage_SetBit_AppearsInOutput() {
    var rawData = new byte[2048];
    // Character 0, row 0: MSB set (leftmost pixel in character)
    rawData[0] = 0x80;
    var file = new CpcFontFile { RawData = rawData };

    var raw = CpcFontFile.ToRawImage(file);

    // Char 0 is at grid position (0,0), pixel (0,0)
    // In Indexed1 MSB-first: byte 0, bit 7 = pixel (0,0)
    Assert.That((raw.PixelData[0] & 0x80) != 0, Is.True);
  }
}
