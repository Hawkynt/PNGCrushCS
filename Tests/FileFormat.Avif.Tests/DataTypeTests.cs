using System;
using FileFormat.Avif;
using FileFormat.Core;

namespace FileFormat.Avif.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void AvifFile_DefaultPixelData_IsEmptyArray() {
    var file = new AvifFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AvifFile_DefaultRawImageData_IsEmptyArray() {
    var file = new AvifFile();
    Assert.That(file.RawImageData, Is.Not.Null);
    Assert.That(file.RawImageData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AvifFile_DefaultBrand_IsAvif() {
    var file = new AvifFile();
    Assert.That(file.Brand, Is.EqualTo("avif"));
  }

  [Test]
  [Category("Unit")]
  public void AvifFile_DefaultWidth_IsZero() {
    var file = new AvifFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AvifFile_DefaultHeight_IsZero() {
    var file = new AvifFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AvifFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var raw = new byte[] { 0x01, 0x02, 0x03 };
    var file = new AvifFile {
      Width = 1,
      Height = 1,
      PixelData = pixels,
      RawImageData = raw,
      Brand = "avis",
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.SameAs(pixels));
    Assert.That(file.RawImageData, Is.SameAs(raw));
    Assert.That(file.Brand, Is.EqualTo("avis"));
  }

  [Test]
  [Category("Unit")]
  public void AvifFile_PrimaryExtension_IsAvif() {
    var ext = _GetPrimaryExtension<AvifFile>();
    Assert.That(ext, Is.EqualTo(".avif"));
  }

  [Test]
  [Category("Unit")]
  public void AvifFile_FileExtensions_ContainsAvif() {
    var exts = _GetFileExtensions<AvifFile>();
    Assert.That(exts, Contains.Item(".avif"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AvifFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_FormatIsRgb24() {
    var file = new AvifFile { Width = 1, Height = 1, PixelData = new byte[3] };
    var raw = AvifFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new AvifFile { Width = 1, Height = 1, PixelData = pixels };
    var raw = AvifFile.ToRawImage(file);
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AvifFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Bgra32, PixelData = new byte[4] };
    Assert.Throws<ArgumentException>(() => AvifFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ValidRgb24_CreatesFile() {
    var pixels = new byte[] { 0x10, 0x20, 0x30 };
    var raw = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Rgb24, PixelData = pixels };
    var file = AvifFile.FromRawImage(raw);
    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0x10, 0x20, 0x30 };
    var raw = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Rgb24, PixelData = pixels };
    var file = AvifFile.FromRawImage(raw);
    Assert.That(file.RawImageData, Is.EqualTo(pixels));
    Assert.That(file.RawImageData, Is.Not.SameAs(pixels));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
