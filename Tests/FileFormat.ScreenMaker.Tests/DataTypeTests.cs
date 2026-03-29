using System;
using FileFormat.Core;
using FileFormat.ScreenMaker;

namespace FileFormat.ScreenMaker.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void ScreenMakerFile_DefaultWidth_IsZero() {
    var file = new ScreenMakerFile();

    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ScreenMakerFile_DefaultHeight_IsZero() {
    var file = new ScreenMakerFile();

    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ScreenMakerFile_DefaultPalette_IsEmpty() {
    var file = new ScreenMakerFile();

    Assert.That(file.Palette, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ScreenMakerFile_DefaultPixelData_IsEmpty() {
    var file = new ScreenMakerFile();

    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ScreenMakerFile_InitWidth_StoresCorrectly() {
    var file = new ScreenMakerFile { Width = 320 };

    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void ScreenMakerFile_InitHeight_StoresCorrectly() {
    var file = new ScreenMakerFile { Height = 200 };

    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void ScreenMakerFile_InitPalette_StoresReference() {
    var palette = new byte[768];
    var file = new ScreenMakerFile { Palette = palette };

    Assert.That(file.Palette, Is.SameAs(palette));
  }

  [Test]
  [Category("Unit")]
  public void ScreenMakerFile_InitPixelData_StoresReference() {
    var pixelData = new byte[] { 1, 2, 3 };
    var file = new ScreenMakerFile { PixelData = pixelData };

    Assert.That(file.PixelData, Is.SameAs(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void ScreenMakerFile_HeaderSize_Is4() {
    Assert.That(ScreenMakerFile.HeaderSize, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ScreenMakerFile_PaletteDataSize_Is768() {
    Assert.That(ScreenMakerFile.PaletteDataSize, Is.EqualTo(768));
  }

  [Test]
  [Category("Unit")]
  public void ScreenMakerFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ScreenMakerFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ScreenMakerFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => ScreenMakerFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ScreenMakerFile_FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 16,
      Height = 16,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[16 * 16 * 3],
    };

    Assert.Throws<NotSupportedException>(() => ScreenMakerFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ScreenMakerFile_ToRawImage_ReturnsRgb24() {
    var file = new ScreenMakerFile {
      Width = 2,
      Height = 2,
      Palette = new byte[768],
      PixelData = new byte[4],
    };

    var raw = ScreenMakerFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ScreenMakerFile_ToRawImage_CorrectDimensions() {
    var file = new ScreenMakerFile {
      Width = 10,
      Height = 5,
      Palette = new byte[768],
      PixelData = new byte[50],
    };

    var raw = ScreenMakerFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(10));
    Assert.That(raw.Height, Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void ScreenMakerFile_ToRawImage_PixelDataSize() {
    var file = new ScreenMakerFile {
      Width = 4,
      Height = 3,
      Palette = new byte[768],
      PixelData = new byte[12],
    };

    var raw = ScreenMakerFile.ToRawImage(file);

    Assert.That(raw.PixelData.Length, Is.EqualTo(4 * 3 * 3));
  }

  [Test]
  [Category("Unit")]
  public void ScreenMakerFile_ToRawImage_UsesCorrectPaletteEntry() {
    var palette = new byte[768];
    palette[3] = 0xAA; // entry 1, R
    palette[4] = 0xBB; // entry 1, G
    palette[5] = 0xCC; // entry 1, B

    var file = new ScreenMakerFile {
      Width = 1,
      Height = 1,
      Palette = palette,
      PixelData = new byte[] { 1 }, // index 1
    };

    var raw = ScreenMakerFile.ToRawImage(file);

    Assert.That(raw.PixelData[0], Is.EqualTo(0xAA));
    Assert.That(raw.PixelData[1], Is.EqualTo(0xBB));
    Assert.That(raw.PixelData[2], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void ScreenMakerFile_RoundTrip_Functional() {
    var file = new ScreenMakerFile {
      Width = 16,
      Height = 16,
      Palette = new byte[768],
      PixelData = new byte[256],
    };

    var bytes = ScreenMakerWriter.ToBytes(file);
    var restored = ScreenMakerReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(16));
  }
}
