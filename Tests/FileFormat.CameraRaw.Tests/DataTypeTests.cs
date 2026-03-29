using System;
using FileFormat.CameraRaw;
using FileFormat.Core;

namespace FileFormat.CameraRaw.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void CameraRawManufacturer_HasTenValues() {
    var values = Enum.GetValues<CameraRawManufacturer>();
    Assert.That(values, Has.Length.EqualTo(10));
  }

  [Test]
  [Category("Unit")]
  [TestCase(CameraRawManufacturer.Unknown, 0)]
  [TestCase(CameraRawManufacturer.Canon, 1)]
  [TestCase(CameraRawManufacturer.Nikon, 2)]
  [TestCase(CameraRawManufacturer.Sony, 3)]
  [TestCase(CameraRawManufacturer.Olympus, 4)]
  [TestCase(CameraRawManufacturer.Panasonic, 5)]
  [TestCase(CameraRawManufacturer.Pentax, 6)]
  [TestCase(CameraRawManufacturer.Fujifilm, 7)]
  [TestCase(CameraRawManufacturer.Samsung, 8)]
  [TestCase(CameraRawManufacturer.Generic, 9)]
  public void CameraRawManufacturer_Values(CameraRawManufacturer value, int expected) {
    Assert.That((int)value, Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void CameraRawFile_DefaultPixelData_IsEmptyArray() {
    var file = new CameraRawFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void CameraRawFile_DefaultWidth_IsZero() {
    var file = new CameraRawFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void CameraRawFile_DefaultHeight_IsZero() {
    var file = new CameraRawFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void CameraRawFile_DefaultManufacturer_IsUnknown() {
    var file = new CameraRawFile();
    Assert.That(file.Manufacturer, Is.EqualTo(CameraRawManufacturer.Unknown));
  }

  [Test]
  [Category("Unit")]
  public void CameraRawFile_DefaultModel_IsEmpty() {
    var file = new CameraRawFile();
    Assert.That(file.Model, Is.EqualTo(""));
  }

  [Test]
  [Category("Unit")]
  public void CameraRawFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new CameraRawFile {
      Width = 1,
      Height = 1,
      PixelData = pixels,
      Manufacturer = CameraRawManufacturer.Canon,
      Model = "EOS R5",
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.SameAs(pixels));
    Assert.That(file.Manufacturer, Is.EqualTo(CameraRawManufacturer.Canon));
    Assert.That(file.Model, Is.EqualTo("EOS R5"));
  }

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsCr2() {
    var ext = _GetPrimaryExtension<CameraRawFile>();
    Assert.That(ext, Is.EqualTo(".cr2"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsTenExtensions() {
    var exts = _GetFileExtensions<CameraRawFile>();
    Assert.That(exts, Has.Length.EqualTo(10));
  }

  [Test]
  [Category("Unit")]
  [TestCase(".cr2")]
  [TestCase(".nef")]
  [TestCase(".arw")]
  [TestCase(".orf")]
  [TestCase(".rw2")]
  [TestCase(".pef")]
  [TestCase(".raf")]
  [TestCase(".raw")]
  [TestCase(".srw")]
  [TestCase(".dcs")]
  public void FileExtensions_Contains(string ext) {
    var exts = _GetFileExtensions<CameraRawFile>();
    Assert.That(exts, Does.Contain(ext));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CameraRawFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => CameraRawFile.FromRawImage(null!));
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

    Assert.Throws<ArgumentException>(() => CameraRawFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Format_IsRgb24() {
    var file = new CameraRawFile {
      Width = 1,
      Height = 1,
      PixelData = [0xAA, 0xBB, 0xCC],
    };

    var raw = CameraRawFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0x42, 0x43, 0x44 };
    var file = new CameraRawFile {
      Width = 1,
      Height = 1,
      PixelData = pixels,
    };

    var raw = CameraRawFile.ToRawImage(file);
    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0x42, 0x43, 0x44 };
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };

    var file = CameraRawFile.FromRawImage(raw);
    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SetsManufacturer_Generic() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[3],
    };

    var file = CameraRawFile.FromRawImage(raw);
    Assert.That(file.Manufacturer, Is.EqualTo(CameraRawManufacturer.Generic));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T>
    => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T>
    => T.FileExtensions;
}
