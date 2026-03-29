using System;
using FileFormat.Pdn;

namespace FileFormat.Pdn.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PdnFile_DefaultWidth_IsZero() {
    var file = new PdnFile();

    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PdnFile_DefaultHeight_IsZero() {
    var file = new PdnFile();

    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void PdnFile_DefaultVersion_Is3() {
    var file = new PdnFile();

    Assert.That(file.Version, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void PdnFile_DefaultPixelData_IsEmptyArray() {
    var file = new PdnFile();

    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void PdnFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80, 0x40 };
    var file = new PdnFile {
      Width = 1,
      Height = 1,
      Version = 4,
      PixelData = pixels,
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.Version, Is.EqualTo(4));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void PdnFile_PrimaryExtension_IsPdn() {
    var ext = GetPrimaryExtension();

    Assert.That(ext, Is.EqualTo(".pdn"));
  }

  [Test]
  [Category("Unit")]
  public void PdnFile_FileExtensions_ContainsPdn() {
    var exts = GetFileExtensions();

    Assert.That(exts, Does.Contain(".pdn"));
    Assert.That(exts, Has.Length.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PdnFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PdnFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_ThrowsArgumentException() {
    var raw = new FileFormat.Core.RawImage {
      Width = 1,
      Height = 1,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = new byte[3],
    };

    Assert.Throws<ArgumentException>(() => PdnFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_FormatIsBgra32() {
    var file = new PdnFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[4],
    };

    var raw = PdnFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Bgra32));
  }

  private static string GetPrimaryExtension() => GetPrimary<PdnFile>();

  private static string GetPrimary<T>() where T : FileFormat.Core.IImageFileFormat<T>
    => T.PrimaryExtension;

  private static string[] GetFileExtensions() => GetExts<PdnFile>();

  private static string[] GetExts<T>() where T : FileFormat.Core.IImageFileFormat<T>
    => T.FileExtensions;
}
