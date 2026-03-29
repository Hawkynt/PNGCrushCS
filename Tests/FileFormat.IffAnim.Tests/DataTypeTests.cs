using System;
using FileFormat.Core;
using FileFormat.IffAnim;

namespace FileFormat.IffAnim.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void IffAnimFile_DefaultPixelData_IsEmptyArray() {
    var file = new IffAnimFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void IffAnimFile_DefaultWidth_IsZero() {
    var file = new IffAnimFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void IffAnimFile_DefaultHeight_IsZero() {
    var file = new IffAnimFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void IffAnimFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new IffAnimFile {
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
  public void PrimaryExtension_IsAnim() {
    var ext = _GetPrimaryExtension<IffAnimFile>();
    Assert.That(ext, Is.EqualTo(".anim"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsAnim() {
    var exts = _GetFileExtensions<IffAnimFile>();
    Assert.That(exts, Contains.Item(".anim"));
    Assert.That(exts, Has.Length.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffAnimFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsRgb24() {
    var file = new IffAnimFile {
      Width = 1,
      Height = 1,
      PixelData = [0xAA, 0xBB, 0xCC]
    };

    var raw = IffAnimFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(1));
    Assert.That(raw.Height, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new IffAnimFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    var raw = IffAnimFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffAnimFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Gray8,
      PixelData = [0x80]
    };

    Assert.Throws<ArgumentException>(() => IffAnimFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgb24_Succeeds() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [0xAA, 0xBB, 0xCC]
    };

    var file = IffAnimFile.FromRawImage(raw);

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = pixels
    };

    var file = IffAnimFile.FromRawImage(raw);

    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
