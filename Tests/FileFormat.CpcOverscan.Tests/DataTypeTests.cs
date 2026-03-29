using System;
using FileFormat.Core;
using FileFormat.CpcOverscan;

namespace FileFormat.CpcOverscan.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void CpcOverscanFile_DefaultWidth_Is384() {
    var file = new CpcOverscanFile();

    Assert.That(file.Width, Is.EqualTo(384));
  }

  [Test]
  [Category("Unit")]
  public void CpcOverscanFile_DefaultHeight_Is272() {
    var file = new CpcOverscanFile();

    Assert.That(file.Height, Is.EqualTo(272));
  }

  [Test]
  [Category("Unit")]
  public void CpcOverscanFile_DefaultPixelData_IsEmpty() {
    var file = new CpcOverscanFile();

    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void CpcOverscanFile_InitPixelData_StoresCorrectly() {
    var data = new byte[] { 0x3F, 0x2A };
    var file = new CpcOverscanFile { PixelData = data };

    Assert.That(file.PixelData, Is.SameAs(data));
  }

  [Test]
  [Category("Unit")]
  public void CpcOverscanFile_ExpectedFileSize_Is32768() {
    Assert.That(CpcOverscanFile.ExpectedFileSize, Is.EqualTo(32768));
  }

  [Test]
  [Category("Unit")]
  public void CpcOverscanFile_PixelWidth_Is384() {
    Assert.That(CpcOverscanFile.PixelWidth, Is.EqualTo(384));
  }

  [Test]
  [Category("Unit")]
  public void CpcOverscanFile_PixelHeight_Is272() {
    Assert.That(CpcOverscanFile.PixelHeight, Is.EqualTo(272));
  }

  [Test]
  [Category("Unit")]
  public void CpcOverscanFile_BytesPerRow_Is96() {
    Assert.That(CpcOverscanFile.BytesPerRow, Is.EqualTo(96));
  }

  [Test]
  [Category("Unit")]
  public void CpcOverscanFile_PixelsPerByte_Is4() {
    Assert.That(CpcOverscanFile.PixelsPerByte, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void CpcOverscanFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcOverscanFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void CpcOverscanFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CpcOverscanFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void CpcOverscanFile_FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 384,
      Height = 272,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[384 * 272],
    };

    Assert.Throws<NotSupportedException>(() => CpcOverscanFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void CpcOverscanFile_ToRawImage_ReturnsIndexed8Format() {
    var file = new CpcOverscanFile { PixelData = new byte[CpcOverscanFile.PixelHeight * CpcOverscanFile.BytesPerRow] };

    var raw = CpcOverscanFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
  }

  [Test]
  [Category("Unit")]
  public void CpcOverscanFile_ToRawImage_HasCorrectDimensions() {
    var file = new CpcOverscanFile { PixelData = new byte[CpcOverscanFile.PixelHeight * CpcOverscanFile.BytesPerRow] };

    var raw = CpcOverscanFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(384));
    Assert.That(raw.Height, Is.EqualTo(272));
  }

  [Test]
  [Category("Unit")]
  public void CpcOverscanFile_ToRawImage_Has4ColorPalette() {
    var file = new CpcOverscanFile { PixelData = new byte[CpcOverscanFile.PixelHeight * CpcOverscanFile.BytesPerRow] };

    var raw = CpcOverscanFile.ToRawImage(file);

    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.PaletteCount, Is.EqualTo(4));
    Assert.That(raw.Palette!.Length, Is.EqualTo(4 * 3));
  }

  [Test]
  [Category("Unit")]
  public void CpcOverscanFile_ToRawImage_PixelDataSize() {
    var file = new CpcOverscanFile { PixelData = new byte[CpcOverscanFile.PixelHeight * CpcOverscanFile.BytesPerRow] };

    var raw = CpcOverscanFile.ToRawImage(file);

    Assert.That(raw.PixelData.Length, Is.EqualTo(384 * 272));
  }

  [Test]
  [Category("Unit")]
  public void CpcOverscanFile_ToRawImage_ClonesPixelData() {
    var file = new CpcOverscanFile { PixelData = new byte[CpcOverscanFile.PixelHeight * CpcOverscanFile.BytesPerRow] };

    var raw1 = CpcOverscanFile.ToRawImage(file);
    var raw2 = CpcOverscanFile.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void CpcOverscanFile_ToRawImage_PaletteFirstEntryIsBlack() {
    var file = new CpcOverscanFile { PixelData = new byte[CpcOverscanFile.PixelHeight * CpcOverscanFile.BytesPerRow] };

    var raw = CpcOverscanFile.ToRawImage(file);

    Assert.That(raw.Palette![0], Is.EqualTo(0));
    Assert.That(raw.Palette[1], Is.EqualTo(0));
    Assert.That(raw.Palette[2], Is.EqualTo(0));
  }
}
