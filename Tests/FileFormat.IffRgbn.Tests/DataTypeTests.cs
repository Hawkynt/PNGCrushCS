using System;
using FileFormat.Core;
using FileFormat.IffRgbn;

namespace FileFormat.IffRgbn.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void IffRgbnFile_DefaultPixelData_IsEmptyArray() {
    var file = new IffRgbnFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void IffRgbnFile_DefaultWidth_IsZero() {
    var file = new IffRgbnFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void IffRgbnFile_DefaultHeight_IsZero() {
    var file = new IffRgbnFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void IffRgbnFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new IffRgbnFile {
      Width = 1,
      Height = 1,
      PixelData = pixels,
    };

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(1));
      Assert.That(file.Height, Is.EqualTo(1));
      Assert.That(file.PixelData, Is.SameAs(pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void IffRgbnFile_PrimaryExtension_IsRgbn() {
    var ext = _GetPrimaryExtension<IffRgbnFile>();
    Assert.That(ext, Is.EqualTo(".rgbn"));
  }

  [Test]
  [Category("Unit")]
  public void IffRgbnFile_FileExtensions_ContainsExpected() {
    var extensions = _GetFileExtensions<IffRgbnFile>();
    Assert.Multiple(() => {
      Assert.That(extensions, Has.Length.EqualTo(2));
      Assert.That(extensions, Does.Contain(".rgbn"));
      Assert.That(extensions, Does.Contain(".iff"));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffRgbnFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsRgb24() {
    var file = new IffRgbnFile {
      Width = 1,
      Height = 1,
      PixelData = [0xFF, 0x00, 0x80],
    };

    var raw = IffRgbnFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(raw.Width, Is.EqualTo(1));
      Assert.That(raw.Height, Is.EqualTo(1));
      Assert.That(raw.PixelData, Is.EqualTo(new byte[] { 0xFF, 0x00, 0x80 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0x11, 0x22, 0x33 };
    var file = new IffRgbnFile {
      Width = 1,
      Height = 1,
      PixelData = pixels,
    };

    var raw = IffRgbnFile.ToRawImage(file);
    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffRgbnFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Gray8,
      PixelData = [0x80],
    };

    Assert.Throws<ArgumentException>(() => IffRgbnFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgb24_Succeeds() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };

    var file = IffRgbnFile.FromRawImage(raw);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(1));
      Assert.That(file.Height, Is.EqualTo(1));
      Assert.That(file.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0x11, 0x22, 0x33 };
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };

    var file = IffRgbnFile.FromRawImage(raw);
    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
