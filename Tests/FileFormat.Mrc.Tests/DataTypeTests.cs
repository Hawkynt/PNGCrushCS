using System;
using FileFormat.Core;
using FileFormat.Mrc;

namespace FileFormat.Mrc.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void MrcFile_DefaultWidth_IsZero() {
    var file = new MrcFile();

    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_DefaultHeight_IsZero() {
    var file = new MrcFile();

    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_DefaultSections_IsOne() {
    var file = new MrcFile();

    Assert.That(file.Sections, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_DefaultMode_IsZero() {
    var file = new MrcFile();

    Assert.That(file.Mode, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_DefaultExtendedHeaderSize_IsZero() {
    var file = new MrcFile();

    Assert.That(file.ExtendedHeaderSize, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_DefaultExtendedHeader_IsEmpty() {
    var file = new MrcFile();

    Assert.That(file.ExtendedHeader, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_DefaultPixelData_IsEmpty() {
    var file = new MrcFile();

    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_InitProperties_StoreCorrectly() {
    var pixels = new byte[] { 0xAA, 0xBB };
    var extHeader = new byte[] { 0x01 };

    var file = new MrcFile {
      Width = 2,
      Height = 1,
      Sections = 1,
      Mode = 0,
      ExtendedHeaderSize = 1,
      ExtendedHeader = extHeader,
      PixelData = pixels,
    };

    Assert.That(file.Width, Is.EqualTo(2));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.Sections, Is.EqualTo(1));
    Assert.That(file.Mode, Is.EqualTo(0));
    Assert.That(file.ExtendedHeaderSize, Is.EqualTo(1));
    Assert.That(file.ExtendedHeader, Is.SameAs(extHeader));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_HeaderSize_Is1024() {
    Assert.That(MrcFile.HeaderSize, Is.EqualTo(1024));
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_MachineStampLE_Is0x44() {
    Assert.That(MrcFile.MachineStampLE, Is.EqualTo(0x44));
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_Extensions_ExercisedViaRoundTrip() {
    // Exercise the interface static members indirectly through a round-trip
    var file = new MrcFile {
      Width = 1,
      Height = 1,
      Sections = 1,
      Mode = 0,
      PixelData = new byte[1],
    };
    var bytes = MrcWriter.ToBytes(file);
    var restored = MrcReader.FromBytes(bytes);
    Assert.That(restored.Width, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MrcFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MrcFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[4 * 4 * 3],
    };

    Assert.Throws<ArgumentException>(() => MrcFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_ToRawImage_ReturnsGray8Format() {
    var file = new MrcFile {
      Width = 2,
      Height = 2,
      Sections = 1,
      Mode = 0,
      PixelData = new byte[4],
    };

    var raw = MrcFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_ToRawImage_HasCorrectDimensions() {
    var file = new MrcFile {
      Width = 10,
      Height = 20,
      Sections = 1,
      Mode = 0,
      PixelData = new byte[200],
    };

    var raw = MrcFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(10));
    Assert.That(raw.Height, Is.EqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
    var file = new MrcFile {
      Width = 2,
      Height = 2,
      Sections = 1,
      Mode = 0,
      PixelData = pixels,
    };

    var raw1 = MrcFile.ToRawImage(file);
    var raw2 = MrcFile.ToRawImage(file);

    Assert.That(raw1.PixelData, Is.Not.SameAs(raw2.PixelData));
    Assert.That(raw1.PixelData, Is.EqualTo(raw2.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_ToRawImage_UnsupportedMode_ThrowsNotSupportedException() {
    var file = new MrcFile {
      Width = 2,
      Height = 2,
      Sections = 1,
      Mode = 3,
      PixelData = new byte[8],
    };

    Assert.Throws<NotSupportedException>(() => MrcFile.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_ToRawImage_MultipleSections_ThrowsNotSupportedException() {
    var file = new MrcFile {
      Width = 2,
      Height = 2,
      Sections = 3,
      Mode = 0,
      PixelData = new byte[12],
    };

    Assert.Throws<NotSupportedException>(() => MrcFile.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_FromRawImage_SetsMode0AndSections1() {
    var raw = new RawImage {
      Width = 3,
      Height = 3,
      Format = PixelFormat.Gray8,
      PixelData = new byte[9],
    };

    var file = MrcFile.FromRawImage(raw);

    Assert.That(file.Mode, Is.EqualTo(0));
    Assert.That(file.Sections, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void MrcFile_FromRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0x11, 0x22, 0x33, 0x44 };
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Gray8,
      PixelData = pixels,
    };

    var file = MrcFile.FromRawImage(raw);
    pixels[0] = 0xFF;

    Assert.That(file.PixelData[0], Is.EqualTo(0x11));
  }
}
