using System;
using FileFormat.Core;
using FileFormat.XvThumbnail;

namespace FileFormat.XvThumbnail.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void XvThumbnailFile_DefaultWidth_IsZero() {
    var file = new XvThumbnailFile();

    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void XvThumbnailFile_DefaultHeight_IsZero() {
    var file = new XvThumbnailFile();

    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void XvThumbnailFile_DefaultPixelData_IsEmpty() {
    var file = new XvThumbnailFile();

    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void XvThumbnailFile_InitProperties_StoreCorrectly() {
    var pixels = new byte[] { 0xAB, 0xCD };
    var file = new XvThumbnailFile {
      Width = 2,
      Height = 1,
      PixelData = pixels,
    };

    Assert.That(file.Width, Is.EqualTo(2));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void XvThumbnailFile_FileExtensions_ContainsXv() {
    var file = new XvThumbnailFile { Width = 1, Height = 1, PixelData = [0] };
    var bytes = XvThumbnailWriter.ToBytes(file);
    var restored = XvThumbnailReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void XvThumbnailFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XvThumbnailFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void XvThumbnailFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XvThumbnailFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void XvThumbnailFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Gray8,
      PixelData = new byte[4],
    };

    Assert.Throws<ArgumentException>(() => XvThumbnailFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void XvThumbnailFile_ToRawImage_ReturnsRgb24() {
    var file = new XvThumbnailFile {
      Width = 2,
      Height = 1,
      PixelData = [0x00, 0xFF],
    };

    var raw = XvThumbnailFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void XvThumbnailFile_ToRawImage_CorrectDimensions() {
    var file = new XvThumbnailFile {
      Width = 5,
      Height = 3,
      PixelData = new byte[15],
    };

    var raw = XvThumbnailFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(5));
    Assert.That(raw.Height, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void XvThumbnailFile_ToRawImage_CorrectPixelDataSize() {
    var file = new XvThumbnailFile {
      Width = 4,
      Height = 3,
      PixelData = new byte[12],
    };

    var raw = XvThumbnailFile.ToRawImage(file);

    Assert.That(raw.PixelData.Length, Is.EqualTo(4 * 3 * 3));
  }

  [Test]
  [Category("Unit")]
  public void XvThumbnailFile_ToRawImage_332Expansion_Red() {
    // 0xE0 = 111_000_00 => R=7*255/7=255, G=0, B=0
    var file = new XvThumbnailFile {
      Width = 1,
      Height = 1,
      PixelData = [0xE0],
    };

    var raw = XvThumbnailFile.ToRawImage(file);

    Assert.That(raw.PixelData[0], Is.EqualTo(255));
    Assert.That(raw.PixelData[1], Is.EqualTo(0));
    Assert.That(raw.PixelData[2], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void XvThumbnailFile_ToRawImage_332Expansion_Green() {
    // 0x1C = 000_111_00 => R=0, G=7*255/7=255, B=0
    var file = new XvThumbnailFile {
      Width = 1,
      Height = 1,
      PixelData = [0x1C],
    };

    var raw = XvThumbnailFile.ToRawImage(file);

    Assert.That(raw.PixelData[0], Is.EqualTo(0));
    Assert.That(raw.PixelData[1], Is.EqualTo(255));
    Assert.That(raw.PixelData[2], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void XvThumbnailFile_ToRawImage_332Expansion_Blue() {
    // 0x03 = 000_000_11 => R=0, G=0, B=3*255/3=255
    var file = new XvThumbnailFile {
      Width = 1,
      Height = 1,
      PixelData = [0x03],
    };

    var raw = XvThumbnailFile.ToRawImage(file);

    Assert.That(raw.PixelData[0], Is.EqualTo(0));
    Assert.That(raw.PixelData[1], Is.EqualTo(0));
    Assert.That(raw.PixelData[2], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void XvThumbnailFile_ToRawImage_332Expansion_MidValues() {
    // 0x49 = 010_010_01 => R=2*255/7=72 (floor), G=2*255/7=72, B=1*255/3=85
    var file = new XvThumbnailFile {
      Width = 1,
      Height = 1,
      PixelData = [0x49],
    };

    var raw = XvThumbnailFile.ToRawImage(file);

    Assert.That(raw.PixelData[0], Is.EqualTo(2 * 255 / 7));
    Assert.That(raw.PixelData[1], Is.EqualTo(2 * 255 / 7));
    Assert.That(raw.PixelData[2], Is.EqualTo(1 * 255 / 3));
  }
}
