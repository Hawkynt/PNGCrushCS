using System;
using FileFormat.Core;
using FileFormat.Pcd;

namespace FileFormat.Pcd.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PcdFile_DefaultPixelData_IsEmptyArray() {
    var file = new PcdFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void PcdFile_DefaultWidth_IsZero() {
    var file = new PcdFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PcdFile_DefaultHeight_IsZero() {
    var file = new PcdFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PcdFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new PcdFile {
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
  public void PcdFile_PrimaryExtension_IsPcd() {
    var ext = GetPrimaryExtension();
    Assert.That(ext, Is.EqualTo(".pcd"));
  }

  [Test]
  [Category("Unit")]
  public void PcdFile_FileExtensions_ContainsPcd() {
    var exts = GetFileExtensions();
    Assert.That(exts, Does.Contain(".pcd"));
  }

  [Test]
  [Category("Unit")]
  public void PcdFile_HeaderSize_Is2060() {
    Assert.That(PcdFile.HeaderSize, Is.EqualTo(2060));
  }

  [Test]
  [Category("Unit")]
  public void PcdFile_PreambleSize_Is2048() {
    Assert.That(PcdFile.PreambleSize, Is.EqualTo(2048));
  }

  [Test]
  [Category("Unit")]
  public void PcdFile_Magic_IsPcdIpi() {
    Assert.That(PcdFile.Magic, Is.EqualTo(new byte[] { 0x50, 0x43, 0x44, 0x5F, 0x49, 0x50, 0x49, 0x00 }));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcdFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcdFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = [0xFF, 0x00, 0x80, 0xFF]
    };
    Assert.Throws<ArgumentException>(() => PcdFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsRgb24Format() {
    var file = new PcdFile {
      Width = 1,
      Height = 1,
      PixelData = [0xFF, 0x00, 0x80]
    };

    var raw = PcdFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(1));
    Assert.That(raw.Height, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new PcdFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    var raw = PcdFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.EqualTo(pixels));
    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
  }

  private static string GetPrimaryExtension() => _GetPrimary<PcdFile>();
  private static string _GetPrimary<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
  private static string[] GetFileExtensions() => _GetExts<PcdFile>();
  private static string[] _GetExts<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
