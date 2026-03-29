using System;
using FileFormat.Core;
using FileFormat.CpcSprite;

namespace FileFormat.CpcSprite.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void CpcSpriteFile_DefaultWidth_Is16() {
    var file = new CpcSpriteFile();

    Assert.That(file.Width, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void CpcSpriteFile_DefaultHeight_Is16() {
    var file = new CpcSpriteFile();

    Assert.That(file.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void CpcSpriteFile_DefaultRawData_IsEmpty() {
    var file = new CpcSpriteFile();

    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void CpcSpriteFile_InitRawData_StoresCorrectly() {
    var data = new byte[] { 0x3F, 0x2A };
    var file = new CpcSpriteFile { RawData = data };

    Assert.That(file.RawData, Is.SameAs(data));
  }

  [Test]
  [Category("Unit")]
  public void CpcSpriteFile_ExpectedFileSize_Is64() {
    Assert.That(CpcSpriteFile.ExpectedFileSize, Is.EqualTo(64));
  }

  [Test]
  [Category("Unit")]
  public void CpcSpriteFile_PixelWidth_Is16() {
    Assert.That(CpcSpriteFile.PixelWidth, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void CpcSpriteFile_PixelHeight_Is16() {
    Assert.That(CpcSpriteFile.PixelHeight, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void CpcSpriteFile_BytesPerRow_Is4() {
    Assert.That(CpcSpriteFile.BytesPerRow, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void CpcSpriteFile_PixelsPerByte_Is4() {
    Assert.That(CpcSpriteFile.PixelsPerByte, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void CpcSpriteFile_ExpectedFileSize_MatchesDimensions() {
    Assert.That(CpcSpriteFile.ExpectedFileSize, Is.EqualTo(CpcSpriteFile.PixelHeight * CpcSpriteFile.BytesPerRow));
  }

  [Test]
  [Category("Unit")]
  public void CpcSpriteFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcSpriteFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void CpcSpriteFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcSpriteFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void CpcSpriteFile_FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 16,
      Height = 16,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[16 * 16],
    };

    Assert.Throws<NotSupportedException>(() => CpcSpriteFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void CpcSpriteFile_ToRawImage_ReturnsIndexed8Format() {
    var file = new CpcSpriteFile { RawData = new byte[CpcSpriteFile.ExpectedFileSize] };

    var raw = CpcSpriteFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
  }

  [Test]
  [Category("Unit")]
  public void CpcSpriteFile_ToRawImage_HasCorrectDimensions() {
    var file = new CpcSpriteFile { RawData = new byte[CpcSpriteFile.ExpectedFileSize] };

    var raw = CpcSpriteFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(16));
    Assert.That(raw.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void CpcSpriteFile_ToRawImage_Has4ColorPalette() {
    var file = new CpcSpriteFile { RawData = new byte[CpcSpriteFile.ExpectedFileSize] };

    var raw = CpcSpriteFile.ToRawImage(file);

    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.PaletteCount, Is.EqualTo(4));
    Assert.That(raw.Palette!.Length, Is.EqualTo(4 * 3));
  }

  [Test]
  [Category("Unit")]
  public void CpcSpriteFile_ToRawImage_PixelDataSize() {
    var file = new CpcSpriteFile { RawData = new byte[CpcSpriteFile.ExpectedFileSize] };

    var raw = CpcSpriteFile.ToRawImage(file);

    Assert.That(raw.PixelData.Length, Is.EqualTo(16 * 16));
  }

  [Test]
  [Category("Unit")]
  public void CpcSpriteFile_ToRawImage_ClonesPixelData() {
    var file = new CpcSpriteFile { RawData = new byte[CpcSpriteFile.ExpectedFileSize] };

    var raw1 = CpcSpriteFile.ToRawImage(file);
    var raw2 = CpcSpriteFile.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void CpcSpriteFile_ToRawImage_PaletteFirstEntryIsBlack() {
    var file = new CpcSpriteFile { RawData = new byte[CpcSpriteFile.ExpectedFileSize] };

    var raw = CpcSpriteFile.ToRawImage(file);

    Assert.That(raw.Palette![0], Is.EqualTo(0));
    Assert.That(raw.Palette[1], Is.EqualTo(0));
    Assert.That(raw.Palette[2], Is.EqualTo(0));
  }
}
