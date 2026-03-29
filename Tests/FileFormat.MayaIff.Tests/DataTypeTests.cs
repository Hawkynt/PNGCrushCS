using System;
using FileFormat.Core;
using FileFormat.MayaIff;

namespace FileFormat.MayaIff.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void MayaIffFile_DefaultPixelData_IsEmptyArray() {
    var file = new MayaIffFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void MayaIffFile_DefaultWidth_IsZero() {
    var file = new MayaIffFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void MayaIffFile_DefaultHeight_IsZero() {
    var file = new MayaIffFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void MayaIffFile_DefaultHasAlpha_IsFalse() {
    var file = new MayaIffFile();
    Assert.That(file.HasAlpha, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void MayaIffFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80, 0x40 };
    var file = new MayaIffFile {
      Width = 1,
      Height = 1,
      HasAlpha = true,
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.HasAlpha, Is.True);
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void MayaIffFile_PrimaryExtension_IsIff() {
    var ext = _GetPrimaryExtension<MayaIffFile>();
    Assert.That(ext, Is.EqualTo(".iff"));
  }

  [Test]
  [Category("Unit")]
  public void MayaIffFile_FileExtensions_ContainsBoth() {
    var exts = _GetFileExtensions<MayaIffFile>();
    Assert.That(exts, Does.Contain(".iff"));
    Assert.That(exts, Does.Contain(".maya"));
  }

  [Test]
  [Category("Unit")]
  public void MayaIffFile_FileExtensions_Count() {
    var exts = _GetFileExtensions<MayaIffFile>();
    Assert.That(exts, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => MayaIffFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => MayaIffFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = new byte[4]
    };

    Assert.Throws<ArgumentException>(() => MayaIffFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgba_ReturnsRgba32() {
    var file = new MayaIffFile {
      Width = 1,
      Height = 1,
      HasAlpha = true,
      PixelData = [0xFF, 0x00, 0x80, 0x40]
    };

    var raw = MayaIffFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(raw.Width, Is.EqualTo(1));
    Assert.That(raw.Height, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb_ReturnsRgb24() {
    var file = new MayaIffFile {
      Width = 1,
      Height = 1,
      HasAlpha = false,
      PixelData = [0xFF, 0x00, 0x80]
    };

    var raw = MayaIffFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80, 0x40 };
    var file = new MayaIffFile {
      Width = 1,
      Height = 1,
      HasAlpha = true,
      PixelData = pixels
    };

    var raw = MayaIffFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgba_ClonesPixelData() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80, 0x40 };
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = pixels
    };

    var file = MayaIffFile.FromRawImage(raw);

    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgb_ClonesPixelData() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = pixels
    };

    var file = MayaIffFile.FromRawImage(raw);

    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
    Assert.That(file.HasAlpha, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgba_SetsHasAlpha() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[4]
    };

    var file = MayaIffFile.FromRawImage(raw);

    Assert.That(file.HasAlpha, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Gray8_Throws() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Gray8,
      PixelData = new byte[1]
    };

    Assert.Throws<ArgumentException>(() => MayaIffFile.FromRawImage(raw));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
