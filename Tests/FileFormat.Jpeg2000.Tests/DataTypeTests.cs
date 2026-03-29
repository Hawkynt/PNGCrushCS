using System;
using FileFormat.Core;
using FileFormat.Jpeg2000;

namespace FileFormat.Jpeg2000.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void Jpeg2000File_DefaultPixelData_IsEmptyArray() {
    var file = new Jpeg2000File { Width = 1, Height = 1 };
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Has.Length.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Jpeg2000File_DefaultComponentCount_Is3() {
    var file = new Jpeg2000File { Width = 1, Height = 1 };
    Assert.That(file.ComponentCount, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void Jpeg2000File_DefaultBitsPerComponent_Is8() {
    var file = new Jpeg2000File { Width = 1, Height = 1 };
    Assert.That(file.BitsPerComponent, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void Jpeg2000File_DefaultDecompositionLevels_Is3() {
    var file = new Jpeg2000File { Width = 1, Height = 1 };
    Assert.That(file.DecompositionLevels, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void Jpeg2000File_InitProperties_RoundTrip() {
    var pixels = new byte[] { 1, 2, 3, 4, 5, 6 };
    var file = new Jpeg2000File {
      Width = 2,
      Height = 1,
      ComponentCount = 3,
      BitsPerComponent = 8,
      DecompositionLevels = 2,
      PixelData = pixels,
    };

    Assert.That(file.Width, Is.EqualTo(2));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.ComponentCount, Is.EqualTo(3));
    Assert.That(file.BitsPerComponent, Is.EqualTo(8));
    Assert.That(file.DecompositionLevels, Is.EqualTo(2));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void Jpeg2000File_PrimaryExtension_IsJp2() {
    var ext = _GetPrimaryExtension<Jpeg2000File>();
    Assert.That(ext, Is.EqualTo(".jp2"));
  }

  [Test]
  [Category("Unit")]
  public void Jpeg2000File_FileExtensions_ContainsExpected() {
    var exts = _GetFileExtensions<Jpeg2000File>();
    Assert.That(exts, Does.Contain(".jp2"));
    Assert.That(exts, Does.Contain(".j2k"));
    Assert.That(exts, Does.Contain(".j2c"));
    Assert.That(exts, Does.Contain(".jpx"));
    Assert.That(exts, Does.Contain(".jpc"));
    Assert.That(exts, Does.Contain(".jpf"));
    Assert.That(exts, Does.Contain(".jpt"));
    Assert.That(exts, Does.Contain(".jpm"));
    Assert.That(exts, Has.Length.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Jpeg2000File.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Jpeg2000File.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = new byte[4],
    };
    Assert.Throws<ArgumentException>(() => Jpeg2000File.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb_ReturnsRgb24Format() {
    var file = new Jpeg2000File {
      Width = 2,
      Height = 2,
      ComponentCount = 3,
      BitsPerComponent = 8,
      PixelData = new byte[2 * 2 * 3],
    };
    var raw = Jpeg2000File.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Grayscale_ReturnsGray8Format() {
    var file = new Jpeg2000File {
      Width = 2,
      Height = 2,
      ComponentCount = 1,
      BitsPerComponent = 8,
      PixelData = new byte[2 * 2],
    };
    var raw = Jpeg2000File.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
    var file = new Jpeg2000File {
      Width = 2,
      Height = 2,
      ComponentCount = 3,
      BitsPerComponent = 8,
      PixelData = pixels,
    };
    var raw = Jpeg2000File.ToRawImage(file);
    Assert.That(raw.PixelData, Is.Not.SameAs(file.PixelData));
    Assert.That(raw.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixels = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
    var file = Jpeg2000File.FromRawImage(raw);
    Assert.That(file.PixelData, Is.Not.SameAs(raw.PixelData));
    Assert.That(file.PixelData, Is.EqualTo(raw.PixelData));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T>
    => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T>
    => T.FileExtensions;
}
