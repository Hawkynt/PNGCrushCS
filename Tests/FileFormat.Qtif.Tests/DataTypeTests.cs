using System;
using FileFormat.Core;
using FileFormat.Qtif;

namespace FileFormat.Qtif.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void QtifFile_DefaultPixelData_IsEmptyArray() {
    var file = new QtifFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void QtifFile_DefaultDimensions_AreZero() {
    var file = new QtifFile();
    Assert.That(file.Width, Is.EqualTo(0));
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void QtifFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new QtifFile {
      Width = 1,
      Height = 1,
      PixelData = pixels,
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void QtifFile_PrimaryExtension_IsQtif() {
    var ext = _GetPrimaryExtension<QtifFile>();
    Assert.That(ext, Is.EqualTo(".qtif"));
  }

  [Test]
  [Category("Unit")]
  public void QtifFile_FileExtensions_ContainsQtifAndQti() {
    var extensions = _GetFileExtensions<QtifFile>();
    Assert.That(extensions, Does.Contain(".qtif"));
    Assert.That(extensions, Does.Contain(".qti"));
    Assert.That(extensions, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => QtifFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => QtifFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = new byte[4],
    };
    Assert.Throws<ArgumentException>(() => QtifFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsRgb24Format() {
    var file = new QtifFile {
      Width = 1,
      Height = 1,
      PixelData = [10, 20, 30],
    };

    var raw = QtifFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(1));
    Assert.That(raw.Height, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 10, 20, 30 };
    var file = new QtifFile {
      Width = 1,
      Height = 1,
      PixelData = pixels,
    };

    var raw = QtifFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T>
    => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T>
    => T.FileExtensions;
}
