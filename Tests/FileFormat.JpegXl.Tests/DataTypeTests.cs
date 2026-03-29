using System;
using FileFormat.JpegXl;
using FileFormat.Core;

namespace FileFormat.JpegXl.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void JpegXlFile_DefaultPixelData_IsEmptyArray() {
    var file = new JpegXlFile { Width = 1, Height = 1 };
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Has.Length.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void JpegXlFile_DefaultComponentCount_Is3() {
    var file = new JpegXlFile { Width = 1, Height = 1 };
    Assert.That(file.ComponentCount, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void JpegXlFile_DefaultBrand_IsJxlSpace() {
    var file = new JpegXlFile { Width = 1, Height = 1 };
    Assert.That(file.Brand, Is.EqualTo("jxl "));
  }

  [Test]
  [Category("Unit")]
  public void JpegXlFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 1, 2, 3, 4, 5, 6 };
    var file = new JpegXlFile {
      Width = 10,
      Height = 20,
      ComponentCount = 1,
      PixelData = pixels,
      Brand = "jxl "
    };

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(10));
      Assert.That(file.Height, Is.EqualTo(20));
      Assert.That(file.ComponentCount, Is.EqualTo(1));
      Assert.That(file.PixelData, Is.SameAs(pixels));
      Assert.That(file.Brand, Is.EqualTo("jxl "));
    });
  }

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsJxl() {
    var ext = GetPrimaryExtension();
    Assert.That(ext, Is.EqualTo(".jxl"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsJxl() {
    var exts = GetFileExtensions();
    Assert.That(exts, Contains.Item(".jxl"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_HasOneEntry() {
    var exts = GetFileExtensions();
    Assert.That(exts, Has.Length.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JpegXlFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JpegXlFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = new byte[4]
    };
    Assert.Throws<ArgumentException>(() => JpegXlFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb_ClonesData() {
    var pixels = new byte[] { 1, 2, 3 };
    var file = new JpegXlFile {
      Width = 1,
      Height = 1,
      ComponentCount = 3,
      PixelData = pixels
    };

    var raw = JpegXlFile.ToRawImage(file);
    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Gray_ReturnsGray8Format() {
    var file = new JpegXlFile {
      Width = 1,
      Height = 1,
      ComponentCount = 1,
      PixelData = new byte[] { 128 }
    };

    var raw = JpegXlFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb_ReturnsRgb24Format() {
    var file = new JpegXlFile {
      Width = 1,
      Height = 1,
      ComponentCount = 3,
      PixelData = new byte[] { 1, 2, 3 }
    };

    var raw = JpegXlFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesData() {
    var pixels = new byte[] { 10, 20, 30 };
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = pixels
    };

    var file = JpegXlFile.FromRawImage(raw);
    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Gray8_SetsComponentCount1() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Gray8,
      PixelData = new byte[4]
    };

    var file = JpegXlFile.FromRawImage(raw);
    Assert.That(file.ComponentCount, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgb24_SetsComponentCount3() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[3]
    };

    var file = JpegXlFile.FromRawImage(raw);
    Assert.That(file.ComponentCount, Is.EqualTo(3));
  }

  // Helper methods that use the interface to access static members
  private static string GetPrimaryExtension() {
    // Access via the interface constraint pattern
    return Access<JpegXlFile>.PrimaryExtension;
  }

  private static string[] GetFileExtensions() {
    return Access<JpegXlFile>.FileExtensions;
  }

  private static class Access<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}
