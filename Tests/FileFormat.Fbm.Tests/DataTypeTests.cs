using System;
using FileFormat.Fbm;
using FileFormat.Core;

namespace FileFormat.Fbm.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void FbmFile_DefaultWidth_IsZero() {
    var file = new FbmFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FbmFile_DefaultHeight_IsZero() {
    var file = new FbmFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FbmFile_DefaultBands_IsZero() {
    var file = new FbmFile();
    Assert.That(file.Bands, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FbmFile_DefaultPixelData_IsEmpty() {
    var file = new FbmFile();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void FbmFile_DefaultTitle_IsEmpty() {
    var file = new FbmFile();
    Assert.That(file.Title, Is.EqualTo(string.Empty));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Grayscale_ReturnsGray8() {
    var file = new FbmFile {
      Width = 1,
      Height = 1,
      Bands = 1,
      PixelData = [128]
    };

    var raw = FbmFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(raw.Width, Is.EqualTo(1));
    Assert.That(raw.Height, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb_ReturnsRgb24() {
    var file = new FbmFile {
      Width = 1,
      Height = 1,
      Bands = 3,
      PixelData = [255, 128, 64]
    };

    var raw = FbmFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Gray8_Bands1() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Gray8,
      PixelData = [10, 20, 30, 40]
    };

    var file = FbmFile.FromRawImage(raw);

    Assert.That(file.Bands, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgb24_Bands3() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [255, 128, 64]
    };

    var file = FbmFile.FromRawImage(raw);

    Assert.That(file.Bands, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_Throws() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = [255, 128, 64, 255]
    };

    Assert.Throws<ArgumentException>(() => FbmFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => FbmFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => FbmFile.ToRawImage(null!));
  }
}
