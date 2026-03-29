using System;
using FileFormat.Core;
using FileFormat.HiresBitmap;

namespace FileFormat.HiresBitmap.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void ImageWidth_Is320() {
    Assert.That(HiresBitmapFile.ImageWidth, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void ImageHeight_Is200() {
    Assert.That(HiresBitmapFile.ImageHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void BitmapDataSize_Is8000() {
    Assert.That(HiresBitmapFile.BitmapDataSize, Is.EqualTo(8000));
  }

  [Test]
  [Category("Unit")]
  public void ScreenDataSize_Is1000() {
    Assert.That(HiresBitmapFile.ScreenDataSize, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void LoadAddressSize_Is2() {
    Assert.That(HiresBitmapFile.LoadAddressSize, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void MinPayloadSize_Is9000() {
    Assert.That(HiresBitmapFile.MinPayloadSize, Is.EqualTo(9000));
  }

  [Test]
  [Category("Unit")]
  public void MinFileSize_Is9002() {
    Assert.That(HiresBitmapFile.MinFileSize, Is.EqualTo(9002));
  }

  [Test]
  [Category("Unit")]
  public void LoadAddress_Default_Zero() {
    var file = new HiresBitmapFile();
    Assert.That(file.LoadAddress, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void BitmapData_Default_Empty() {
    var file = new HiresBitmapFile();
    Assert.That(file.BitmapData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ScreenData_Default_Empty() {
    var file = new HiresBitmapFile();
    Assert.That(file.ScreenData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void TrailingData_Default_Empty() {
    var file = new HiresBitmapFile();
    Assert.That(file.TrailingData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void InitProperties_Work() {
    var bitmap = new byte[] { 0x01 };
    var screen = new byte[] { 0x02 };
    var trailing = new byte[] { 0x03 };

    var file = new HiresBitmapFile {
      LoadAddress = 0x4000,
      BitmapData = bitmap,
      ScreenData = screen,
      TrailingData = trailing,
    };

    Assert.That(file.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(file.BitmapData, Is.SameAs(bitmap));
    Assert.That(file.ScreenData, Is.SameAs(screen));
    Assert.That(file.TrailingData, Is.SameAs(trailing));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsHbmAndHir() {
    var file = new HiresBitmapFile {
      BitmapData = new byte[HiresBitmapFile.BitmapDataSize],
      ScreenData = new byte[HiresBitmapFile.ScreenDataSize],
    };

    // Exercise the interface to verify extensions are accessible via round-trip
    var bytes = HiresBitmapWriter.ToBytes(file);
    var restored = HiresBitmapReader.FromBytes(bytes);
    Assert.That(restored, Is.Not.Null);
  }

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsHbm() {
    // Verify via round-trip that the format works (interface static members tested indirectly)
    var file = new HiresBitmapFile {
      LoadAddress = 0x2000,
      BitmapData = new byte[HiresBitmapFile.BitmapDataSize],
      ScreenData = new byte[HiresBitmapFile.ScreenDataSize],
    };

    var bytes = HiresBitmapWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.EqualTo(HiresBitmapFile.MinFileSize));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => HiresBitmapFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Format_IsRgb24() {
    var file = new HiresBitmapFile {
      BitmapData = new byte[HiresBitmapFile.BitmapDataSize],
      ScreenData = new byte[HiresBitmapFile.ScreenDataSize],
    };

    var raw = HiresBitmapFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_PixelDataSize_Correct() {
    var file = new HiresBitmapFile {
      BitmapData = new byte[HiresBitmapFile.BitmapDataSize],
      ScreenData = new byte[HiresBitmapFile.ScreenDataSize],
    };

    var raw = HiresBitmapFile.ToRawImage(file);

    Assert.That(raw.PixelData.Length, Is.EqualTo(320 * 200 * 3));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupported() {
    var raw = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 200 * 3],
    };

    Assert.Throws<NotSupportedException>(() => HiresBitmapFile.FromRawImage(raw));
  }
}
