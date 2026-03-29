using System;
using FileFormat.Core;
using FileFormat.PhotoPaint;

namespace FileFormat.PhotoPaint.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PhotoPaintFile_DefaultWidth_IsZero() {
    var file = new PhotoPaintFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PhotoPaintFile_DefaultHeight_IsZero() {
    var file = new PhotoPaintFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PhotoPaintFile_DefaultPixelData_IsEmptyArray() {
    var file = new PhotoPaintFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void PhotoPaintFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80, 0x40, 0x20, 0x10 };
    var file = new PhotoPaintFile {
      Width = 2,
      Height = 1,
      PixelData = pixels,
    };

    Assert.That(file.Width, Is.EqualTo(2));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void PhotoPaintFile_HeaderSize_Is24() {
    Assert.That(PhotoPaintFile.HeaderSize, Is.EqualTo(24));
  }

  [Test]
  [Category("Unit")]
  public void PhotoPaintFile_Version_Is1() {
    Assert.That(PhotoPaintFile.Version, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void PhotoPaintFile_BitDepth_Is24() {
    Assert.That(PhotoPaintFile.BitDepth, Is.EqualTo(24));
  }

  [Test]
  [Category("Unit")]
  public void PhotoPaintFile_Magic_IsCptNull() {
    Assert.That(PhotoPaintFile.Magic[0], Is.EqualTo((byte)'C'));
    Assert.That(PhotoPaintFile.Magic[1], Is.EqualTo((byte)'P'));
    Assert.That(PhotoPaintFile.Magic[2], Is.EqualTo((byte)'T'));
    Assert.That(PhotoPaintFile.Magic[3], Is.EqualTo(0x00));
    Assert.That(PhotoPaintFile.Magic.Length, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void PhotoPaintFile_PrimaryExtension_IsCpt() {
    Assert.That(_GetPrimaryExtension<PhotoPaintFile>(), Is.EqualTo(".cpt"));
  }

  [Test]
  [Category("Unit")]
  public void PhotoPaintFile_FileExtensions_ContainsCpt() {
    var extensions = _GetFileExtensions<PhotoPaintFile>();
    Assert.That(extensions, Does.Contain(".cpt"));
    Assert.That(extensions, Has.Length.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PhotoPaintFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PhotoPaintFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = new byte[4],
    };
    Assert.Throws<ArgumentException>(() => PhotoPaintFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsRgb24Format() {
    var file = new PhotoPaintFile {
      Width = 1,
      Height = 1,
      PixelData = [10, 20, 30],
    };

    var raw = PhotoPaintFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(1));
    Assert.That(raw.Height, Is.EqualTo(1));
    Assert.That(raw.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 10, 20, 30 };
    var file = new PhotoPaintFile {
      Width = 1,
      Height = 1,
      PixelData = pixels,
    };

    var raw = PhotoPaintFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
