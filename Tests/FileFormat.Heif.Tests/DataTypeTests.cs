using System;
using FileFormat.Core;
using FileFormat.Heif;

namespace FileFormat.Heif.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void HeifFile_DefaultPixelData_IsEmptyArray() {
    var file = new HeifFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void HeifFile_DefaultRawImageData_IsEmptyArray() {
    var file = new HeifFile();
    Assert.That(file.RawImageData, Is.Not.Null);
    Assert.That(file.RawImageData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void HeifFile_DefaultBrand_IsHeic() {
    var file = new HeifFile();
    Assert.That(file.Brand, Is.EqualTo("heic"));
  }

  [Test]
  [Category("Unit")]
  public void HeifFile_DefaultWidth_IsZero() {
    var file = new HeifFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void HeifFile_DefaultHeight_IsZero() {
    var file = new HeifFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void HeifFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var raw = new byte[] { 0xAA, 0xBB };
    var file = new HeifFile {
      Width = 1,
      Height = 1,
      PixelData = pixels,
      RawImageData = raw,
      Brand = "heix",
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.SameAs(pixels));
    Assert.That(file.RawImageData, Is.SameAs(raw));
    Assert.That(file.Brand, Is.EqualTo("heix"));
  }

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsHeic() {
    var ext = GetPrimaryExtension();
    Assert.That(ext, Is.EqualTo(".heic"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsHeicAndHeif() {
    var exts = GetFileExtensions();
    Assert.That(exts, Does.Contain(".heic"));
    Assert.That(exts, Does.Contain(".heif"));
    Assert.That(exts, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HeifFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsRgb24Format() {
    var file = new HeifFile { Width = 1, Height = 1, PixelData = new byte[3] };
    var raw = HeifFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0x11, 0x22, 0x33 };
    var file = new HeifFile { Width = 1, Height = 1, PixelData = pixels };
    var raw = HeifFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.EqualTo(pixels));
    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HeifFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Bgra32, PixelData = new byte[4] };
    Assert.Throws<ArgumentException>(() => HeifFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var raw = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Rgb24, PixelData = pixels };
    var file = HeifFile.FromRawImage(raw);

    Assert.That(file.PixelData, Is.EqualTo(pixels));
    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SetsRawImageData() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var raw = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Rgb24, PixelData = pixels };
    var file = HeifFile.FromRawImage(raw);

    Assert.That(file.RawImageData, Is.EqualTo(pixels));
    Assert.That(file.RawImageData, Is.Not.SameAs(file.PixelData));
  }

  private static string GetPrimaryExtension() => _GetPrimary<HeifFile>();
  private static string[] GetFileExtensions() => _GetExts<HeifFile>();

  private static string _GetPrimary<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
  private static string[] _GetExts<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
