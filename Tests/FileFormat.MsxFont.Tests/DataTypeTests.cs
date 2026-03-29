using System;
using FileFormat.Core;
using FileFormat.MsxFont;

namespace FileFormat.MsxFont.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void MsxFontFile_DefaultWidth_Is128() {
    var file = new MsxFontFile();
    Assert.That(file.Width, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void MsxFontFile_DefaultHeight_Is128() {
    var file = new MsxFontFile();
    Assert.That(file.Height, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void MsxFontFile_DefaultRawData_IsEmpty() {
    var file = new MsxFontFile();
    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void MsxFontFile_InitRawData_StoresCorrectly() {
    var rawData = new byte[] { 0xAB, 0xCD };
    var file = new MsxFontFile { RawData = rawData };
    Assert.That(file.RawData, Is.SameAs(rawData));
  }

  [Test]
  [Category("Unit")]
  public void MsxFontFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxFontFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void MsxFontFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxFontFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void MsxFontFile_FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 128,
      Height = 128,
      Format = PixelFormat.Indexed1,
      PixelData = new byte[128 / 8 * 128],
    };
    Assert.Throws<NotSupportedException>(() => MsxFontFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void MsxFontFile_ToRawImage_ReturnsIndexed1Format() {
    var file = new MsxFontFile { RawData = new byte[2048] };
    var raw = MsxFontFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
  }

  [Test]
  [Category("Unit")]
  public void MsxFontFile_ToRawImage_HasCorrectDimensions() {
    var file = new MsxFontFile { RawData = new byte[2048] };
    var raw = MsxFontFile.ToRawImage(file);
    Assert.That(raw.Width, Is.EqualTo(128));
    Assert.That(raw.Height, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void MsxFontFile_ToRawImage_HasCorrectPalette() {
    var file = new MsxFontFile { RawData = new byte[2048] };
    var raw = MsxFontFile.ToRawImage(file);
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
  public void MsxFontFile_ToRawImage_PixelDataSize() {
    var file = new MsxFontFile { RawData = new byte[2048] };
    var raw = MsxFontFile.ToRawImage(file);
    Assert.That(raw.PixelData.Length, Is.EqualTo(128 / 8 * 128));
  }
}
