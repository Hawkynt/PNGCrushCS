using System;
using FileFormat.Ptif;
using FileFormat.Core;

namespace FileFormat.Ptif.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PtifFile_DefaultPixelData_IsEmptyArray() {
    var file = new PtifFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void PtifFile_DefaultWidth_IsZero() {
    var file = new PtifFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PtifFile_DefaultHeight_IsZero() {
    var file = new PtifFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PtifFile_DefaultSamplesPerPixel_IsZero() {
    var file = new PtifFile();
    Assert.That(file.SamplesPerPixel, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PtifFile_DefaultBitsPerSample_IsZero() {
    var file = new PtifFile();
    Assert.That(file.BitsPerSample, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PtifFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new PtifFile {
      Width = 1,
      Height = 1,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.SamplesPerPixel, Is.EqualTo(3));
    Assert.That(file.BitsPerSample, Is.EqualTo(8));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PtifFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PtifFile.FromRawImage(null!));
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

    Assert.Throws<ArgumentException>(() => PtifFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_UnsupportedConfig_ThrowsNotSupportedException() {
    var file = new PtifFile {
      Width = 1,
      Height = 1,
      SamplesPerPixel = 2,
      BitsPerSample = 8,
      PixelData = new byte[2]
    };

    Assert.Throws<NotSupportedException>(() => PtifFile.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Gray8_Format() {
    var file = new PtifFile {
      Width = 1,
      Height = 1,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      PixelData = [0x80]
    };

    var raw = PtifFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb24_Format() {
    var file = new PtifFile {
      Width = 1,
      Height = 1,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      PixelData = [0xAA, 0xBB, 0xCC]
    };

    var raw = PtifFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgba32_Format() {
    var file = new PtifFile {
      Width = 1,
      Height = 1,
      SamplesPerPixel = 4,
      BitsPerSample = 8,
      PixelData = [0xAA, 0xBB, 0xCC, 0xDD]
    };

    var raw = PtifFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgba32));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0x42 };
    var file = new PtifFile {
      Width = 1,
      Height = 1,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      PixelData = pixels
    };

    var raw = PtifFile.ToRawImage(file);
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

    var file = PtifFile.FromRawImage(raw);
    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }
}
