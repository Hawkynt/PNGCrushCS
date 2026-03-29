using System;
using FileFormat.Core;
using FileFormat.DjVu;

namespace FileFormat.DjVu.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void DjVuFile_DefaultPixelData_IsEmptyArray() {
    var file = new DjVuFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DjVuFile_DefaultRawChunks_IsEmptyList() {
    var file = new DjVuFile();
    Assert.That(file.RawChunks, Is.Not.Null);
    Assert.That(file.RawChunks, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DjVuFile_DefaultDpi_Is300() {
    var file = new DjVuFile();
    Assert.That(file.Dpi, Is.EqualTo(300));
  }

  [Test]
  [Category("Unit")]
  public void DjVuFile_DefaultGamma_Is22() {
    var file = new DjVuFile();
    Assert.That(file.Gamma, Is.EqualTo(22));
  }

  [Test]
  [Category("Unit")]
  public void DjVuFile_DefaultWidth_IsZero() {
    var file = new DjVuFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void DjVuFile_DefaultHeight_IsZero() {
    var file = new DjVuFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void DjVuFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new DjVuFile {
      Width = 1,
      Height = 1,
      Dpi = 150,
      Gamma = 33,
      VersionMajor = 1,
      VersionMinor = 26,
      Flags = 0x01,
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.Dpi, Is.EqualTo(150));
    Assert.That(file.Gamma, Is.EqualTo(33));
    Assert.That(file.VersionMajor, Is.EqualTo(1));
    Assert.That(file.VersionMinor, Is.EqualTo(26));
    Assert.That(file.Flags, Is.EqualTo(0x01));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void DjVuFile_PrimaryExtension_IsDjvu() {
    var ext = _GetPrimaryExtension<DjVuFile>();
    Assert.That(ext, Is.EqualTo(".djvu"));
  }

  [Test]
  [Category("Unit")]
  public void DjVuFile_FileExtensions_ContainsBoth() {
    var exts = _GetFileExtensions<DjVuFile>();
    Assert.That(exts, Does.Contain(".djvu"));
    Assert.That(exts, Does.Contain(".djv"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => DjVuFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => DjVuFile.FromRawImage(null!));
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

    Assert.Throws<ArgumentException>(() => DjVuFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsRgb24() {
    var file = new DjVuFile {
      Width = 2,
      Height = 1,
      PixelData = [0xFF, 0x00, 0x80, 0x11, 0x22, 0x33]
    };

    var raw = DjVuFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(2));
    Assert.That(raw.Height, Is.EqualTo(1));
    Assert.That(raw.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new DjVuFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    var raw = DjVuFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = pixels
    };

    var file = DjVuFile.FromRawImage(raw);

    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
