using System;
using FileFormat.Core;
using FileFormat.MsxSprite;

namespace FileFormat.MsxSprite.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void MsxSpriteFile_DefaultWidth_Is128() {
    var file = new MsxSpriteFile();
    Assert.That(file.Width, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void MsxSpriteFile_DefaultHeight_Is16() {
    var file = new MsxSpriteFile();
    Assert.That(file.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void MsxSpriteFile_DefaultRawData_IsEmpty() {
    var file = new MsxSpriteFile();
    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void MsxSpriteFile_InitRawData_StoresCorrectly() {
    var rawData = new byte[] { 0xAB, 0xCD };
    var file = new MsxSpriteFile { RawData = rawData };
    Assert.That(file.RawData, Is.SameAs(rawData));
  }

  [Test]
  [Category("Unit")]
  public void MsxSpriteFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxSpriteFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void MsxSpriteFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MsxSpriteFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void MsxSpriteFile_FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 128,
      Height = 16,
      Format = PixelFormat.Indexed1,
      PixelData = new byte[128 / 8 * 16],
    };
    Assert.Throws<NotSupportedException>(() => MsxSpriteFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void MsxSpriteFile_ToRawImage_ReturnsIndexed1Format() {
    var file = new MsxSpriteFile { RawData = new byte[2048] };
    var raw = MsxSpriteFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
  }

  [Test]
  [Category("Unit")]
  public void MsxSpriteFile_ToRawImage_HasCorrectDimensions() {
    var file = new MsxSpriteFile { RawData = new byte[2048] };
    var raw = MsxSpriteFile.ToRawImage(file);
    Assert.That(raw.Width, Is.EqualTo(128));
    Assert.That(raw.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void MsxSpriteFile_ToRawImage_HasCorrectPalette() {
    var file = new MsxSpriteFile { RawData = new byte[2048] };
    var raw = MsxSpriteFile.ToRawImage(file);
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
  public void MsxSpriteFile_ToRawImage_PixelDataSize() {
    var file = new MsxSpriteFile { RawData = new byte[2048] };
    var raw = MsxSpriteFile.ToRawImage(file);
    Assert.That(raw.PixelData.Length, Is.EqualTo(128 / 8 * 16));
  }
}
