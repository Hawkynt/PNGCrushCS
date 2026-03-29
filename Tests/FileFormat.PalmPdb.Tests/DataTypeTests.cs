using System;
using FileFormat.Core;
using FileFormat.PalmPdb;

namespace FileFormat.PalmPdb.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PalmPdbFile_DefaultPixelData_IsEmptyArray() {
    var file = new PalmPdbFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void PalmPdbFile_DefaultName_IsImage() {
    var file = new PalmPdbFile();
    Assert.That(file.Name, Is.EqualTo("Image"));
  }

  [Test]
  [Category("Unit")]
  public void PalmPdbFile_DefaultWidth_IsZero() {
    var file = new PalmPdbFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PalmPdbFile_DefaultHeight_IsZero() {
    var file = new PalmPdbFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PalmPdbFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new PalmPdbFile {
      Width = 1,
      Height = 1,
      Name = "Test",
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.Name, Is.EqualTo("Test"));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void PalmPdbFile_PrimaryExtension_IsPdb() {
    var ext = _GetPrimaryExtension<PalmPdbFile>();
    Assert.That(ext, Is.EqualTo(".pdb"));
  }

  [Test]
  [Category("Unit")]
  public void PalmPdbFile_FileExtensions_ContainsPdb() {
    var exts = _GetFileExtensions<PalmPdbFile>();
    Assert.That(exts, Has.Length.EqualTo(1));
    Assert.That(exts[0], Is.EqualTo(".pdb"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PalmPdbFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PalmPdbFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsRgb24() {
    var file = new PalmPdbFile {
      Width = 1,
      Height = 1,
      PixelData = [0xAA, 0xBB, 0xCC]
    };

    var raw = PalmPdbFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(1));
    Assert.That(raw.Height, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgb24_Accepted() {
    var raw = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66]
    };

    var file = PalmPdbFile.FromRawImage(raw);

    Assert.That(file.Width, Is.EqualTo(2));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_PixelDataCloned() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new PalmPdbFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    var raw = PalmPdbFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T>
    => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T>
    => T.FileExtensions;
}
