using System;
using FileFormat.Core;
using FileFormat.AppleIIHgr;

namespace FileFormat.AppleIIHgr.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void AppleIIHgrFile_DefaultWidth_Is280() {
    var file = new AppleIIHgrFile();

    Assert.That(file.Width, Is.EqualTo(280));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIHgrFile_DefaultHeight_Is192() {
    var file = new AppleIIHgrFile();

    Assert.That(file.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIHgrFile_DefaultRawData_IsEmpty() {
    var file = new AppleIIHgrFile();

    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AppleIIHgrFile_InitRawData_StoresCorrectly() {
    var rawData = new byte[] { 0x7F, 0x2A };
    var file = new AppleIIHgrFile { RawData = rawData };

    Assert.That(file.RawData, Is.SameAs(rawData));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIHgrFile_FileSize_Is8192() {
    Assert.That(AppleIIHgrFile.FileSize, Is.EqualTo(8192));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIHgrFile_PixelWidth_Is280() {
    Assert.That(AppleIIHgrFile.PixelWidth, Is.EqualTo(280));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIHgrFile_PixelHeight_Is192() {
    Assert.That(AppleIIHgrFile.PixelHeight, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIHgrFile_BytesPerRow_Is40() {
    Assert.That(AppleIIHgrFile.BytesPerRow, Is.EqualTo(40));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIHgrFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleIIHgrFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIHgrFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AppleIIHgrFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIHgrFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 280,
      Height = 192,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[280 * 192 * 3],
    };

    Assert.Throws<ArgumentException>(() => AppleIIHgrFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIHgrFile_FromRawImage_WrongDimensions_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Indexed1,
      PixelData = new byte[320 / 8 * 200],
    };

    Assert.Throws<ArgumentException>(() => AppleIIHgrFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIHgrFile_ToRawImage_ReturnsIndexed1Format() {
    var file = new AppleIIHgrFile { RawData = new byte[8192] };

    var raw = AppleIIHgrFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed1));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIHgrFile_ToRawImage_HasCorrectDimensions() {
    var file = new AppleIIHgrFile { RawData = new byte[8192] };

    var raw = AppleIIHgrFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(280));
    Assert.That(raw.Height, Is.EqualTo(192));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIHgrFile_ToRawImage_HasCorrectPalette() {
    var file = new AppleIIHgrFile { RawData = new byte[8192] };

    var raw = AppleIIHgrFile.ToRawImage(file);

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
  public void AppleIIHgrFile_ToRawImage_PixelDataSize() {
    var file = new AppleIIHgrFile { RawData = new byte[8192] };

    var raw = AppleIIHgrFile.ToRawImage(file);

    // 280 pixels / 8 = 35 bytes per row, 192 rows
    Assert.That(raw.PixelData.Length, Is.EqualTo(35 * 192));
  }

  [Test]
  [Category("Unit")]
  public void AppleIIHgrFile_ToRawImage_ClonesPixelData() {
    var rawData = new byte[8192];
    rawData[0] = 0x7F;
    var file = new AppleIIHgrFile { RawData = rawData };

    var raw1 = AppleIIHgrFile.ToRawImage(file);
    var raw2 = AppleIIHgrFile.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }
}
