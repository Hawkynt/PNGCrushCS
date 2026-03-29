using System;
using System.IO;
using FileFormat.Core;
using FileFormat.DigiView;

namespace FileFormat.DigiView.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void DigiViewFile_DefaultWidth_IsZero() {
    var file = new DigiViewFile();

    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_DefaultHeight_IsZero() {
    var file = new DigiViewFile();

    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_DefaultChannels_IsZero() {
    var file = new DigiViewFile();

    Assert.That(file.Channels, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_DefaultPixelData_IsEmpty() {
    var file = new DigiViewFile();

    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_InitWidth_StoresCorrectly() {
    var file = new DigiViewFile { Width = 320 };

    Assert.That(file.Width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_InitHeight_StoresCorrectly() {
    var file = new DigiViewFile { Height = 200 };

    Assert.That(file.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_InitChannels_StoresCorrectly() {
    var file = new DigiViewFile { Channels = 3 };

    Assert.That(file.Channels, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_InitPixelData_StoresReference() {
    var pixelData = new byte[] { 1, 2, 3 };
    var file = new DigiViewFile { PixelData = pixelData };

    Assert.That(file.PixelData, Is.SameAs(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_HeaderSize_Is5() {
    Assert.That(DigiViewFile.HeaderSize, Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DigiViewFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => DigiViewFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 16,
      Height = 16,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[16 * 16 * 4],
    };

    Assert.Throws<ArgumentException>(() => DigiViewFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_ToRawImage_Grayscale_ReturnsGray8() {
    var file = new DigiViewFile {
      Width = 4,
      Height = 4,
      Channels = 1,
      PixelData = new byte[16],
    };

    var raw = DigiViewFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_ToRawImage_Rgb_ReturnsRgb24() {
    var file = new DigiViewFile {
      Width = 4,
      Height = 4,
      Channels = 3,
      PixelData = new byte[48],
    };

    var raw = DigiViewFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_ToRawImage_InvalidChannels_ThrowsInvalidDataException() {
    var file = new DigiViewFile {
      Width = 4,
      Height = 4,
      Channels = 2,
      PixelData = new byte[32],
    };

    Assert.Throws<InvalidDataException>(() => DigiViewFile.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_ToRawImage_CorrectDimensions() {
    var file = new DigiViewFile {
      Width = 10,
      Height = 5,
      Channels = 1,
      PixelData = new byte[50],
    };

    var raw = DigiViewFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(10));
    Assert.That(raw.Height, Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_ToRawImage_Gray_PixelDataSize() {
    var file = new DigiViewFile {
      Width = 4,
      Height = 3,
      Channels = 1,
      PixelData = new byte[12],
    };

    var raw = DigiViewFile.ToRawImage(file);

    Assert.That(raw.PixelData.Length, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_ToRawImage_Rgb_PixelDataSize() {
    var file = new DigiViewFile {
      Width = 4,
      Height = 3,
      Channels = 3,
      PixelData = new byte[36],
    };

    var raw = DigiViewFile.ToRawImage(file);

    Assert.That(raw.PixelData.Length, Is.EqualTo(36));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_ToRawImage_ClonesPixelData() {
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
    var file = new DigiViewFile {
      Width = 2,
      Height = 2,
      Channels = 1,
      PixelData = pixelData,
    };

    var raw1 = DigiViewFile.ToRawImage(file);
    var raw2 = DigiViewFile.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_FromRawImage_Gray8_SetsChannels1() {
    var raw = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Gray8,
      PixelData = new byte[16],
    };

    var file = DigiViewFile.FromRawImage(raw);

    Assert.That(file.Channels, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_FromRawImage_Rgb24_SetsChannels3() {
    var raw = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[48],
    };

    var file = DigiViewFile.FromRawImage(raw);

    Assert.That(file.Channels, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_FromRawImage_ClonesPixelData() {
    var pixelData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Gray8,
      PixelData = pixelData,
    };

    var file = DigiViewFile.FromRawImage(raw);

    Assert.That(file.PixelData, Is.Not.SameAs(pixelData));
    Assert.That(file.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void DigiViewFile_RoundTrip_Functional() {
    var file = new DigiViewFile {
      Width = 16,
      Height = 16,
      Channels = 1,
      PixelData = new byte[256],
    };

    var bytes = DigiViewWriter.ToBytes(file);
    var restored = DigiViewReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(16));
  }
}
