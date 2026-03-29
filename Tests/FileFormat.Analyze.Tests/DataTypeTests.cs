using System;
using FileFormat.Analyze;
using FileFormat.Core;

namespace FileFormat.Analyze.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void AnalyzeDataType_UInt8_Is2() {
    Assert.That((short)AnalyzeDataType.UInt8, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void AnalyzeDataType_Int16_Is4() {
    Assert.That((short)AnalyzeDataType.Int16, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void AnalyzeDataType_Int32_Is8() {
    Assert.That((short)AnalyzeDataType.Int32, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void AnalyzeDataType_Float32_Is16() {
    Assert.That((short)AnalyzeDataType.Float32, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void AnalyzeDataType_Rgb24_Is128() {
    Assert.That((short)AnalyzeDataType.Rgb24, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void AnalyzeDataType_HasFiveValues() {
    var values = Enum.GetValues<AnalyzeDataType>();
    Assert.That(values.Length, Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void AnalyzeFile_DefaultPixelData_IsEmptyArray() {
    var file = new AnalyzeFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AnalyzeFile_DefaultWidth_IsZero() {
    var file = new AnalyzeFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AnalyzeFile_DefaultHeight_IsZero() {
    var file = new AnalyzeFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AnalyzeFile_DefaultBitsPerPixel_IsZero() {
    var file = new AnalyzeFile();
    Assert.That(file.BitsPerPixel, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AnalyzeFile_DefaultDataType_IsZero() {
    var file = new AnalyzeFile();
    Assert.That((short)file.DataType, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AnalyzeFile_PrimaryExtension_IsHdr() {
    Assert.That(_GetPrimaryExtension<AnalyzeFile>(), Is.EqualTo(".hdr"));
  }

  [Test]
  [Category("Unit")]
  public void AnalyzeFile_FileExtensions_ContainsHdr() {
    Assert.That(_GetFileExtensions<AnalyzeFile>(), Does.Contain(".hdr"));
  }

  [Test]
  [Category("Unit")]
  public void AnalyzeFile_FileExtensions_ContainsImg() {
    Assert.That(_GetFileExtensions<AnalyzeFile>(), Does.Contain(".img"));
  }

  [Test]
  [Category("Unit")]
  public void AnalyzeFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new AnalyzeFile {
      Width = 1,
      Height = 1,
      DataType = AnalyzeDataType.Rgb24,
      BitsPerPixel = 24,
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.DataType, Is.EqualTo(AnalyzeDataType.Rgb24));
    Assert.That(file.BitsPerPixel, Is.EqualTo(24));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AnalyzeFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AnalyzeFile.FromRawImage(null!));
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

    Assert.Throws<ArgumentException>(() => AnalyzeFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_UnsupportedDataType_ThrowsNotSupportedException() {
    var file = new AnalyzeFile {
      Width = 1,
      Height = 1,
      DataType = AnalyzeDataType.Float32,
      BitsPerPixel = 32,
      PixelData = new byte[4]
    };

    Assert.Throws<NotSupportedException>(() => AnalyzeFile.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Gray8_Format() {
    var file = new AnalyzeFile {
      Width = 1,
      Height = 1,
      DataType = AnalyzeDataType.UInt8,
      BitsPerPixel = 8,
      PixelData = [0x80]
    };

    var raw = AnalyzeFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb24_Format() {
    var file = new AnalyzeFile {
      Width = 1,
      Height = 1,
      DataType = AnalyzeDataType.Rgb24,
      BitsPerPixel = 24,
      PixelData = [0xAA, 0xBB, 0xCC]
    };

    var raw = AnalyzeFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0x42 };
    var file = new AnalyzeFile {
      Width = 1,
      Height = 1,
      DataType = AnalyzeDataType.UInt8,
      BitsPerPixel = 8,
      PixelData = pixels
    };

    var raw = AnalyzeFile.ToRawImage(file);
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

    var file = AnalyzeFile.FromRawImage(raw);
    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Gray8_SetsDataType() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Gray8,
      PixelData = [0x80]
    };

    var file = AnalyzeFile.FromRawImage(raw);
    Assert.That(file.DataType, Is.EqualTo(AnalyzeDataType.UInt8));
    Assert.That(file.BitsPerPixel, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgb24_SetsDataType() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [0xAA, 0xBB, 0xCC]
    };

    var file = AnalyzeFile.FromRawImage(raw);
    Assert.That(file.DataType, Is.EqualTo(AnalyzeDataType.Rgb24));
    Assert.That(file.BitsPerPixel, Is.EqualTo(24));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
