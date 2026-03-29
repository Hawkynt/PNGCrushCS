using System;
using FileFormat.BioRadPic;
using FileFormat.Core;

namespace FileFormat.BioRadPic.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void BioRadPicFile_DefaultWidth_IsZero() {
    var file = new BioRadPicFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void BioRadPicFile_DefaultHeight_IsZero() {
    var file = new BioRadPicFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void BioRadPicFile_DefaultNumImages_IsOne() {
    var file = new BioRadPicFile();
    Assert.That(file.NumImages, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void BioRadPicFile_DefaultByteFormat_IsTrue() {
    var file = new BioRadPicFile();
    Assert.That(file.ByteFormat, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void BioRadPicFile_DefaultName_IsEmpty() {
    var file = new BioRadPicFile();
    Assert.That(file.Name, Is.EqualTo(""));
  }

  [Test]
  [Category("Unit")]
  public void BioRadPicFile_DefaultLens_IsZero() {
    var file = new BioRadPicFile();
    Assert.That(file.Lens, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void BioRadPicFile_DefaultMagFactor_IsZero() {
    var file = new BioRadPicFile();
    Assert.That(file.MagFactor, Is.EqualTo(0f));
  }

  [Test]
  [Category("Unit")]
  public void BioRadPicFile_DefaultPixelData_IsEmpty() {
    var file = new BioRadPicFile();
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_8Bit_ProducesGray8() {
    var file = new BioRadPicFile {
      Width = 2,
      Height = 2,
      ByteFormat = true,
      PixelData = new byte[] { 10, 20, 30, 40 }
    };

    var raw = BioRadPicFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(raw.Width, Is.EqualTo(2));
    Assert.That(raw.Height, Is.EqualTo(2));
    Assert.That(raw.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_16Bit_ProducesGray16() {
    var file = new BioRadPicFile {
      Width = 2,
      Height = 2,
      ByteFormat = false,
      PixelData = new byte[8]
    };

    var raw = BioRadPicFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray16));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Gray8_Creates8BitFile() {
    var raw = new RawImage {
      Width = 3,
      Height = 3,
      Format = PixelFormat.Gray8,
      PixelData = new byte[9]
    };

    var file = BioRadPicFile.FromRawImage(raw);

    Assert.That(file.Width, Is.EqualTo(3));
    Assert.That(file.Height, Is.EqualTo(3));
    Assert.That(file.ByteFormat, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Gray16_Creates16BitFile() {
    var raw = new RawImage {
      Width = 3,
      Height = 3,
      Format = PixelFormat.Gray16,
      PixelData = new byte[18]
    };

    var file = BioRadPicFile.FromRawImage(raw);

    Assert.That(file.Width, Is.EqualTo(3));
    Assert.That(file.ByteFormat, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_Throws() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[12]
    };

    Assert.Throws<ArgumentException>(() => BioRadPicFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BioRadPicFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BioRadPicFile.FromRawImage(null!));
  }
}
