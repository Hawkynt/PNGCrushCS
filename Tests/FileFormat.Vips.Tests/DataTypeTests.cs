using System;
using FileFormat.Vips;
using FileFormat.Core;

namespace FileFormat.Vips.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void VipsBandFormat_HasExpectedValues() {
    Assert.That((int)VipsBandFormat.UChar, Is.EqualTo(0));
    Assert.That((int)VipsBandFormat.Char, Is.EqualTo(1));
    Assert.That((int)VipsBandFormat.UShort, Is.EqualTo(2));
    Assert.That((int)VipsBandFormat.Short, Is.EqualTo(3));
    Assert.That((int)VipsBandFormat.UInt, Is.EqualTo(4));
    Assert.That((int)VipsBandFormat.Int, Is.EqualTo(5));
    Assert.That((int)VipsBandFormat.Float, Is.EqualTo(6));
    Assert.That((int)VipsBandFormat.Complex, Is.EqualTo(7));
    Assert.That((int)VipsBandFormat.Double, Is.EqualTo(8));
    Assert.That((int)VipsBandFormat.DpComplex, Is.EqualTo(9));

    var values = Enum.GetValues<VipsBandFormat>();
    Assert.That(values, Has.Length.EqualTo(10));
  }

  [Test]
  [Category("Unit")]
  public void VipsFile_DefaultPixelData_IsEmptyArray() {
    var file = new VipsFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void VipsFile_DefaultWidth_IsZero() {
    var file = new VipsFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void VipsFile_DefaultHeight_IsZero() {
    var file = new VipsFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void VipsFile_DefaultBands_IsThree() {
    var file = new VipsFile();
    Assert.That(file.Bands, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void VipsFile_DefaultBandFormat_IsUChar() {
    var file = new VipsFile();
    Assert.That(file.BandFormat, Is.EqualTo(VipsBandFormat.UChar));
  }

  [Test]
  [Category("Unit")]
  public void VipsFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new VipsFile {
      Width = 1,
      Height = 1,
      Bands = 3,
      BandFormat = VipsBandFormat.UChar,
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.Bands, Is.EqualTo(3));
    Assert.That(file.BandFormat, Is.EqualTo(VipsBandFormat.UChar));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void VipsFile_PrimaryExtension_IsV() {
    var ext = _GetPrimaryExtension<VipsFile>();
    Assert.That(ext, Is.EqualTo(".v"));
  }

  [Test]
  [Category("Unit")]
  public void VipsFile_FileExtensions_ContainsBoth() {
    var exts = _GetFileExtensions<VipsFile>();
    Assert.That(exts, Has.Length.EqualTo(2));
    Assert.That(exts, Does.Contain(".v"));
    Assert.That(exts, Does.Contain(".vips"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => VipsFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => VipsFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = new byte[4]
    };
    Assert.Throws<ArgumentException>(() => VipsFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Gray8_ReturnsGray8Format() {
    var file = new VipsFile {
      Width = 1,
      Height = 1,
      Bands = 1,
      BandFormat = VipsBandFormat.UChar,
      PixelData = [128]
    };

    var raw = VipsFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(raw.Width, Is.EqualTo(1));
    Assert.That(raw.Height, Is.EqualTo(1));
    Assert.That(raw.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb24_ReturnsRgb24Format() {
    var file = new VipsFile {
      Width = 1,
      Height = 1,
      Bands = 3,
      BandFormat = VipsBandFormat.UChar,
      PixelData = [255, 128, 64]
    };

    var raw = VipsFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(1));
    Assert.That(raw.Height, Is.EqualTo(1));
    Assert.That(raw.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgba32_ConvertsToRgb24() {
    var file = new VipsFile {
      Width = 1,
      Height = 1,
      Bands = 4,
      BandFormat = VipsBandFormat.UChar,
      PixelData = [0xAA, 0xBB, 0xCC, 0xFF]
    };

    var raw = VipsFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.PixelData, Is.EqualTo(new byte[] { 0xAA, 0xBB, 0xCC }));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_UnsupportedBandFormat_ThrowsNotSupportedException() {
    var file = new VipsFile {
      Width = 1,
      Height = 1,
      Bands = 3,
      BandFormat = VipsBandFormat.Float,
      PixelData = new byte[12]
    };

    Assert.Throws<NotSupportedException>(() => VipsFile.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 10, 20, 30 };
    var file = new VipsFile {
      Width = 1,
      Height = 1,
      Bands = 3,
      BandFormat = VipsBandFormat.UChar,
      PixelData = pixels
    };

    var raw = VipsFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixels = new byte[] { 10, 20, 30 };
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = pixels
    };

    var file = VipsFile.FromRawImage(raw);

    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T>
    => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T>
    => T.FileExtensions;
}
