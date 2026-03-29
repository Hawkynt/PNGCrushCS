using System;
using FileFormat.Core;
using FileFormat.Eps;

namespace FileFormat.Eps.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void EpsFile_DefaultPixelData_IsEmptyArray() {
    var file = new EpsFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void EpsFile_DefaultWidth_IsZero() {
    var file = new EpsFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void EpsFile_DefaultHeight_IsZero() {
    var file = new EpsFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void EpsFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new EpsFile {
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
  public void EpsFile_PrimaryExtension_IsEps() {
    var ext = _GetPrimaryExtension<EpsFile>();
    Assert.That(ext, Is.EqualTo(".eps"));
  }

  [Test]
  [Category("Unit")]
  public void EpsFile_FileExtensions_ContainsAllVariants() {
    var exts = _GetFileExtensions<EpsFile>();
    Assert.That(exts, Does.Contain(".eps"));
    Assert.That(exts, Does.Contain(".epsf"));
    Assert.That(exts, Does.Contain(".epsi"));
    Assert.That(exts, Does.Contain(".epi"));
    Assert.That(exts, Does.Contain(".ept"));
    Assert.That(exts.Length, Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EpsFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => EpsFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Gray8,
      PixelData = new byte[1]
    };
    Assert.Throws<ArgumentException>(() => EpsFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsRgb24() {
    var file = new EpsFile {
      Width = 1,
      Height = 1,
      PixelData = [0xAA, 0xBB, 0xCC]
    };

    var raw = EpsFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(1));
    Assert.That(raw.Height, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new EpsFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    var raw = EpsFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = pixels
    };

    var file = EpsFile.FromRawImage(raw);

    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T>
    => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T>
    => T.FileExtensions;
}
