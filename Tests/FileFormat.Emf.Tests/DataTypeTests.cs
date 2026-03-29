using System;
using FileFormat.Core;
using FileFormat.Emf;

namespace FileFormat.Emf.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void EmfFile_DefaultPixelData_IsEmptyArray() {
    var file = new EmfFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void EmfFile_DefaultWidth_IsZero() {
    var file = new EmfFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void EmfFile_DefaultHeight_IsZero() {
    var file = new EmfFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void EmfFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new EmfFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void EmfFile_PrimaryExtension_IsEmf() {
    var ext = _GetPrimaryExtension<EmfFile>();
    Assert.That(ext, Is.EqualTo(".emf"));
  }

  [Test]
  [Category("Unit")]
  public void EmfFile_FileExtensions_ContainsEmf() {
    var exts = _GetFileExtensions<EmfFile>();
    Assert.That(exts, Does.Contain(".emf"));
  }

  [Test]
  [Category("Unit")]
  public void EmfFile_ToRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => EmfFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void EmfFile_FromRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => EmfFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void EmfFile_FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = new byte[4]
    };
    Assert.Throws<ArgumentException>(() => EmfFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void EmfFile_ToRawImage_Format_IsRgb24() {
    var file = new EmfFile {
      Width = 1,
      Height = 1,
      PixelData = [0xAA, 0xBB, 0xCC]
    };

    var raw = EmfFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void EmfFile_ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new EmfFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    var raw = EmfFile.ToRawImage(file);
    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
