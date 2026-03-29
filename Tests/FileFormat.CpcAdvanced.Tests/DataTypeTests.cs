using System;
using FileFormat.Core;
using FileFormat.CpcAdvanced;

namespace FileFormat.CpcAdvanced.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void CpcAdvancedFile_DefaultWidth_Is160() {
    var file = new CpcAdvancedFile();

    Assert.That(file.Width, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void CpcAdvancedFile_DefaultHeight_Is200() {
    var file = new CpcAdvancedFile();

    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void CpcAdvancedFile_DefaultPixelData_IsEmpty() {
    var file = new CpcAdvancedFile();

    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void CpcAdvancedFile_InitPixelData_StoresCorrectly() {
    var data = new byte[] { 0x3F, 0x2A };
    var file = new CpcAdvancedFile { PixelData = data };

    Assert.That(file.PixelData, Is.SameAs(data));
  }

  [Test]
  [Category("Unit")]
  public void CpcAdvancedFile_ExpectedFileSize_Is16384() {
    Assert.That(CpcAdvancedFile.ExpectedFileSize, Is.EqualTo(16384));
  }

  [Test]
  [Category("Unit")]
  public void CpcAdvancedFile_PixelWidth_Is160() {
    Assert.That(CpcAdvancedFile.PixelWidth, Is.EqualTo(160));
  }

  [Test]
  [Category("Unit")]
  public void CpcAdvancedFile_PixelHeight_Is200() {
    Assert.That(CpcAdvancedFile.PixelHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void CpcAdvancedFile_BytesPerRow_Is80() {
    Assert.That(CpcAdvancedFile.BytesPerRow, Is.EqualTo(80));
  }

  [Test]
  [Category("Unit")]
  public void CpcAdvancedFile_PixelsPerByte_Is2() {
    Assert.That(CpcAdvancedFile.PixelsPerByte, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void CpcAdvancedFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcAdvancedFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void CpcAdvancedFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcAdvancedFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void CpcAdvancedFile_FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 160,
      Height = 200,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[160 * 200],
    };

    Assert.Throws<NotSupportedException>(() => CpcAdvancedFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void CpcAdvancedFile_ToRawImage_ReturnsIndexed8Format() {
    var file = new CpcAdvancedFile { PixelData = new byte[CpcAdvancedFile.PixelHeight * CpcAdvancedFile.BytesPerRow] };

    var raw = CpcAdvancedFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
  }

  [Test]
  [Category("Unit")]
  public void CpcAdvancedFile_ToRawImage_HasCorrectDimensions() {
    var file = new CpcAdvancedFile { PixelData = new byte[CpcAdvancedFile.PixelHeight * CpcAdvancedFile.BytesPerRow] };

    var raw = CpcAdvancedFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(160));
    Assert.That(raw.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void CpcAdvancedFile_ToRawImage_Has16ColorPalette() {
    var file = new CpcAdvancedFile { PixelData = new byte[CpcAdvancedFile.PixelHeight * CpcAdvancedFile.BytesPerRow] };

    var raw = CpcAdvancedFile.ToRawImage(file);

    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.PaletteCount, Is.EqualTo(16));
    Assert.That(raw.Palette!.Length, Is.EqualTo(16 * 3));
  }

  [Test]
  [Category("Unit")]
  public void CpcAdvancedFile_ToRawImage_PixelDataSize() {
    var file = new CpcAdvancedFile { PixelData = new byte[CpcAdvancedFile.PixelHeight * CpcAdvancedFile.BytesPerRow] };

    var raw = CpcAdvancedFile.ToRawImage(file);

    Assert.That(raw.PixelData.Length, Is.EqualTo(160 * 200));
  }

  [Test]
  [Category("Unit")]
  public void CpcAdvancedFile_ToRawImage_ClonesPixelData() {
    var file = new CpcAdvancedFile { PixelData = new byte[CpcAdvancedFile.PixelHeight * CpcAdvancedFile.BytesPerRow] };

    var raw1 = CpcAdvancedFile.ToRawImage(file);
    var raw2 = CpcAdvancedFile.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void CpcAdvancedFile_ToRawImage_PaletteFirstEntryIsBlack() {
    var file = new CpcAdvancedFile { PixelData = new byte[CpcAdvancedFile.PixelHeight * CpcAdvancedFile.BytesPerRow] };

    var raw = CpcAdvancedFile.ToRawImage(file);

    Assert.That(raw.Palette![0], Is.EqualTo(0));
    Assert.That(raw.Palette[1], Is.EqualTo(0));
    Assert.That(raw.Palette[2], Is.EqualTo(0));
  }
}
