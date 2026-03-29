using System;
using FileFormat.SoftImage;
using FileFormat.Core;

namespace FileFormat.SoftImage.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void SoftImageFile_DefaultPixelData_IsEmptyArray() {
    var file = new SoftImageFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void SoftImageFile_DefaultWidth_IsZero() {
    var file = new SoftImageFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void SoftImageFile_DefaultHeight_IsZero() {
    var file = new SoftImageFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void SoftImageFile_DefaultComment_IsEmpty() {
    var file = new SoftImageFile();
    Assert.That(file.Comment, Is.EqualTo(string.Empty));
  }

  [Test]
  [Category("Unit")]
  public void SoftImageFile_DefaultHasAlpha_IsFalse() {
    var file = new SoftImageFile();
    Assert.That(file.HasAlpha, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void SoftImageFile_DefaultVersion_IsZero() {
    var file = new SoftImageFile();
    Assert.That(file.Version, Is.EqualTo(0f));
  }

  [Test]
  [Category("Unit")]
  public void SoftImageFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new SoftImageFile {
      Width = 1,
      Height = 1,
      PixelData = pixels,
      Comment = "test",
      HasAlpha = false,
      Version = 3.71f,
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.SameAs(pixels));
    Assert.That(file.Comment, Is.EqualTo("test"));
    Assert.That(file.HasAlpha, Is.False);
    Assert.That(file.Version, Is.EqualTo(3.71f).Within(0.01f));
  }

  [Test]
  [Category("Unit")]
  public void SoftImageFile_PrimaryExtension_IsPic() {
    var ext = _GetPrimaryExtension<SoftImageFile>();
    Assert.That(ext, Is.EqualTo(".pic"));
  }

  [Test]
  [Category("Unit")]
  public void SoftImageFile_FileExtensions_ContainsPic() {
    var exts = _GetFileExtensions<SoftImageFile>();
    Assert.That(exts, Has.Length.EqualTo(2));
    Assert.That(exts, Does.Contain(".pic"));
    Assert.That(exts, Does.Contain(".si"));
  }

  [Test]
  [Category("Unit")]
  public void SoftImageFile_Magic_IsExpected() {
    Assert.That(SoftImageFile.Magic, Is.EqualTo(0x5380F634));
  }

  [Test]
  [Category("Unit")]
  public void SoftImageFile_HeaderSize_Is100() {
    Assert.That(SoftImageFile.HeaderSize, Is.EqualTo(100));
  }

  [Test]
  [Category("Unit")]
  public void SoftImageFile_CommentSize_Is80() {
    Assert.That(SoftImageFile.CommentSize, Is.EqualTo(80));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SoftImageFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SoftImageFile.FromRawImage(null!));
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
    Assert.Throws<ArgumentException>(() => SoftImageFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb_ReturnsRgb24Format() {
    var file = new SoftImageFile {
      Width = 1,
      Height = 1,
      PixelData = [255, 128, 64],
    };

    var raw = SoftImageFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(1));
    Assert.That(raw.Height, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgba_ReturnsRgba32Format() {
    var file = new SoftImageFile {
      Width = 1,
      Height = 1,
      HasAlpha = true,
      PixelData = [255, 128, 64, 200],
    };

    var raw = SoftImageFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgba32));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 10, 20, 30 };
    var file = new SoftImageFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    var raw = SoftImageFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgb24_ClonesPixelData() {
    var pixels = new byte[] { 10, 20, 30 };
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = pixels
    };

    var file = SoftImageFile.FromRawImage(raw);

    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgba32_SetsHasAlpha() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[4]
    };

    var file = SoftImageFile.FromRawImage(raw);

    Assert.That(file.HasAlpha, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgb24_HasAlphaIsFalse() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[3]
    };

    var file = SoftImageFile.FromRawImage(raw);

    Assert.That(file.HasAlpha, Is.False);
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T>
    => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T>
    => T.FileExtensions;
}
