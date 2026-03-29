using System;
using FileFormat.Core;
using FileFormat.Wmf;

namespace FileFormat.Wmf.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void WmfFile_DefaultPixelData_IsEmptyArray() {
    var file = new WmfFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void WmfFile_DefaultWidth_IsZero() {
    var file = new WmfFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void WmfFile_DefaultHeight_IsZero() {
    var file = new WmfFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void WmfFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new WmfFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void WmfFile_PrimaryExtension_IsWmf() {
    var ext = _GetPrimaryExtension<WmfFile>();
    Assert.That(ext, Is.EqualTo(".wmf"));
  }

  [Test]
  [Category("Unit")]
  public void WmfFile_FileExtensions_ContainsWmf() {
    var exts = _GetFileExtensions<WmfFile>();
    Assert.That(exts, Contains.Item(".wmf"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WmfFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsRgb24() {
    var file = new WmfFile {
      Width = 1,
      Height = 1,
      PixelData = [0xFF, 0x80, 0x40]
    };

    var raw = WmfFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(1));
    Assert.That(raw.Height, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0xFF, 0x80, 0x40 };
    var file = new WmfFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    var raw = WmfFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WmfFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Gray8,
      PixelData = [0xFF]
    };
    Assert.Throws<ArgumentException>(() => WmfFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ValidRgb24_CreatesFile() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = pixels
    };

    var file = WmfFile.FromRawImage(raw);

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
