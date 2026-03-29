using System;
using FileFormat.Dng;
using FileFormat.Core;

namespace FileFormat.Dng.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void DngPhotometric_BlackIsZero_Is1() {
    Assert.That((int)DngPhotometric.BlackIsZero, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void DngPhotometric_Rgb_Is2() {
    Assert.That((int)DngPhotometric.Rgb, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void DngPhotometric_Cfa_Is32803() {
    Assert.That((int)DngPhotometric.Cfa, Is.EqualTo(32803));
  }

  [Test]
  [Category("Unit")]
  public void DngPhotometric_HasThreeValues() {
    var values = Enum.GetValues<DngPhotometric>();
    Assert.That(values.Length, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void DngFile_DefaultPixelData_IsEmptyArray() {
    var file = new DngFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DngFile_DefaultWidth_IsZero() {
    var file = new DngFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void DngFile_DefaultHeight_IsZero() {
    var file = new DngFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void DngFile_DefaultBitsPerSample_Is8() {
    var file = new DngFile();
    Assert.That(file.BitsPerSample, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void DngFile_DefaultSamplesPerPixel_Is3() {
    var file = new DngFile();
    Assert.That(file.SamplesPerPixel, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void DngFile_DefaultDngVersion_Is1400() {
    var file = new DngFile();
    Assert.That(file.DngVersion, Is.EqualTo(new byte[] { 1, 4, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void DngFile_DefaultCameraModel_IsEmpty() {
    var file = new DngFile();
    Assert.That(file.CameraModel, Is.EqualTo(""));
  }

  [Test]
  [Category("Unit")]
  public void DngFile_PrimaryExtension_IsDng() {
    Assert.That(_GetPrimaryExtension<DngFile>(), Is.EqualTo(".dng"));
  }

  [Test]
  [Category("Unit")]
  public void DngFile_FileExtensions_ContainsDng() {
    Assert.That(_GetFileExtensions<DngFile>(), Does.Contain(".dng"));
  }

  [Test]
  [Category("Unit")]
  public void DngFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new DngFile {
      Width = 1,
      Height = 1,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      Photometric = DngPhotometric.Rgb,
      CameraModel = "Test",
      DngVersion = [1, 5, 0, 0],
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.SamplesPerPixel, Is.EqualTo(3));
    Assert.That(file.BitsPerSample, Is.EqualTo(8));
    Assert.That(file.Photometric, Is.EqualTo(DngPhotometric.Rgb));
    Assert.That(file.CameraModel, Is.EqualTo("Test"));
    Assert.That(file.DngVersion, Is.EqualTo(new byte[] { 1, 5, 0, 0 }));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DngFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DngFile.FromRawImage(null!));
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

    Assert.Throws<ArgumentException>(() => DngFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_UnsupportedConfig_ThrowsNotSupportedException() {
    var file = new DngFile {
      Width = 1,
      Height = 1,
      SamplesPerPixel = 2,
      BitsPerSample = 8,
      PixelData = new byte[2]
    };

    Assert.Throws<NotSupportedException>(() => DngFile.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Gray8_Format() {
    var file = new DngFile {
      Width = 1,
      Height = 1,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      PixelData = [0x80]
    };

    var raw = DngFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb24_Format() {
    var file = new DngFile {
      Width = 1,
      Height = 1,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      PixelData = [0xAA, 0xBB, 0xCC]
    };

    var raw = DngFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0x42 };
    var file = new DngFile {
      Width = 1,
      Height = 1,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      PixelData = pixels
    };

    var raw = DngFile.ToRawImage(file);
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
      PixelData = pixels
    };

    var file = DngFile.FromRawImage(raw);
    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Gray8_SetsPhotometric() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Gray8,
      PixelData = [0x80]
    };

    var file = DngFile.FromRawImage(raw);
    Assert.That(file.Photometric, Is.EqualTo(DngPhotometric.BlackIsZero));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgb24_SetsPhotometric() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [0xAA, 0xBB, 0xCC]
    };

    var file = DngFile.FromRawImage(raw);
    Assert.That(file.Photometric, Is.EqualTo(DngPhotometric.Rgb));
  }

  // Helpers to access static interface members
  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
