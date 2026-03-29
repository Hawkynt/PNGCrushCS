using System;
using FileFormat.SeattleFilmWorks;
using FileFormat.Core;

namespace FileFormat.SeattleFilmWorks.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void SeattleFilmWorksFile_DefaultPixelData_IsEmptyArray() {
    var file = new SeattleFilmWorksFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void SeattleFilmWorksFile_DefaultJpegData_IsEmptyArray() {
    var file = new SeattleFilmWorksFile();
    Assert.That(file.JpegData, Is.Not.Null);
    Assert.That(file.JpegData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void SeattleFilmWorksFile_DefaultWidth_IsZero() {
    var file = new SeattleFilmWorksFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void SeattleFilmWorksFile_DefaultHeight_IsZero() {
    var file = new SeattleFilmWorksFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void SeattleFilmWorksFile_InitProperties_RoundTrip() {
    var jpeg = new byte[] { 0xFF, 0xD8 };
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new SeattleFilmWorksFile {
      Width = 640,
      Height = 480,
      JpegData = jpeg,
      PixelData = pixels,
    };

    Assert.That(file.Width, Is.EqualTo(640));
    Assert.That(file.Height, Is.EqualTo(480));
    Assert.That(file.JpegData, Is.SameAs(jpeg));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsSfw() {
    var ext = _GetPrimaryExtension<SeattleFilmWorksFile>();
    Assert.That(ext, Is.EqualTo(".sfw"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsSfwAndPwp() {
    var exts = _GetFileExtensions<SeattleFilmWorksFile>();
    Assert.That(exts, Has.Length.EqualTo(2));
    Assert.That(exts, Does.Contain(".sfw"));
    Assert.That(exts, Does.Contain(".pwp"));
  }

  [Test]
  [Category("Unit")]
  public void SfwMagic_IsSfw94A() {
    Assert.That(SeattleFilmWorksFile.SfwMagic, Is.EqualTo(new byte[] { 0x53, 0x46, 0x57, 0x39, 0x34, 0x41 }));
  }

  [Test]
  [Category("Unit")]
  public void PwpMagic_IsSfw95A() {
    Assert.That(SeattleFilmWorksFile.PwpMagic, Is.EqualTo(new byte[] { 0x53, 0x46, 0x57, 0x39, 0x35, 0x41 }));
  }

  [Test]
  [Category("Unit")]
  public void MagicLength_IsSix() {
    Assert.That(SeattleFilmWorksFile.MAGIC_LENGTH, Is.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void MinFileSize_IsEight() {
    Assert.That(SeattleFilmWorksFile.MIN_FILE_SIZE, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SeattleFilmWorksFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => SeattleFilmWorksFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[4],
    };
    Assert.Throws<ArgumentException>(() => SeattleFilmWorksFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsRgb24Format() {
    var file = new SeattleFilmWorksFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3],
    };

    var raw = SeattleFilmWorksFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(2));
    Assert.That(raw.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80, 0x40, 0x20, 0x10, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
    var file = new SeattleFilmWorksFile {
      Width = 2,
      Height = 2,
      PixelData = pixels,
    };

    var raw = SeattleFilmWorksFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixels = new byte[4 * 4 * 3];
    pixels[0] = 0xAB;
    var image = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };

    var file = SeattleFilmWorksFile.FromRawImage(image);

    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SetsEmptyJpegData() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[3],
    };

    var file = SeattleFilmWorksFile.FromRawImage(image);

    Assert.That(file.JpegData, Is.Empty);
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T>
    => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T>
    => T.FileExtensions;
}
