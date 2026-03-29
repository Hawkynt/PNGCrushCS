using System;
using FileFormat.Core;
using FileFormat.GunPaint;

namespace FileFormat.GunPaint.Tests;

[TestFixture]
public sealed class GunPaintFileTests {

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GunPaintFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ValidFile_ReturnsCorrectDimensions() {
    var file = new GunPaintFile {
      LoadAddress = 0x4000,
      RawData = new byte[GunPaintFile.RawDataSize]
    };

    var raw = GunPaintFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(160));
    Assert.That(raw.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ValidFile_ReturnsRgb24Format() {
    var file = new GunPaintFile {
      LoadAddress = 0x4000,
      RawData = new byte[GunPaintFile.RawDataSize]
    };

    var raw = GunPaintFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ValidFile_PixelDataHasCorrectSize() {
    var file = new GunPaintFile {
      LoadAddress = 0x4000,
      RawData = new byte[GunPaintFile.RawDataSize]
    };

    var raw = GunPaintFile.ToRawImage(file);

    Assert.That(raw.PixelData.Length, Is.EqualTo(160 * 200 * 3));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_AllZeros_ProducesBlackImage() {
    var file = new GunPaintFile {
      LoadAddress = 0x4000,
      RawData = new byte[GunPaintFile.RawDataSize]
    };

    var raw = GunPaintFile.ToRawImage(file);

    for (var i = 0; i < raw.PixelData.Length; ++i)
      Assert.That(raw.PixelData[i], Is.EqualTo(0), $"Byte at index {i} should be zero for all-black image");
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_EmptyRawData_ProducesBlackImage() {
    var file = new GunPaintFile {
      LoadAddress = 0x4000,
      RawData = []
    };

    var raw = GunPaintFile.ToRawImage(file);

    Assert.That(raw.PixelData.Length, Is.EqualTo(160 * 200 * 3));
    for (var i = 0; i < raw.PixelData.Length; ++i)
      Assert.That(raw.PixelData[i], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_NonZeroBitmap_ProducesNonBlackPixels() {
    var rawData = new byte[GunPaintFile.RawDataSize];

    // Set bitmap byte for first char cell, first row: all bit pairs = 01 (0x55)
    // 01 01 01 01 -> 4 pixels using screen RAM upper nybble color
    rawData[GunPaintFile.BitmapDataOffset] = 0x55;

    // Set screen RAM for first cell: upper nybble=1 (white), lower nybble=0
    rawData[GunPaintFile.ScreenRamOffset] = 0x10;

    var file = new GunPaintFile {
      LoadAddress = 0x4000,
      RawData = rawData
    };

    var raw = GunPaintFile.ToRawImage(file);

    // First pixel (0,0) should be white (color index 1 = 0xFFFFFF)
    Assert.That(raw.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(raw.PixelData[1], Is.EqualTo(0xFF));
    Assert.That(raw.PixelData[2], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GunPaintFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ValidImage_ThrowsNotSupportedException() {
    var image = new RawImage {
      Width = 160,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[160 * 200 * 3]
    };

    Assert.Throws<NotSupportedException>(() => GunPaintFile.FromRawImage(image));
  }
}
