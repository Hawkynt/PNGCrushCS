using System;
using FileFormat.Nitf;
using FileFormat.Core;

namespace FileFormat.Nitf.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void NitfImageMode_HasExpectedValues() {
    Assert.That((int)NitfImageMode.Grayscale, Is.EqualTo(0));
    Assert.That((int)NitfImageMode.Rgb, Is.EqualTo(1));

    var values = Enum.GetValues<NitfImageMode>();
    Assert.That(values, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void NitfFile_DefaultWidth_IsZero() {
    var file = new NitfFile();

    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void NitfFile_DefaultHeight_IsZero() {
    var file = new NitfFile();

    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void NitfFile_DefaultMode_IsRgb() {
    var file = new NitfFile();

    Assert.That(file.Mode, Is.EqualTo(NitfImageMode.Rgb));
  }

  [Test]
  [Category("Unit")]
  public void NitfFile_DefaultPixelData_IsEmpty() {
    var file = new NitfFile();

    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void NitfFile_DefaultTitle_IsEmpty() {
    var file = new NitfFile();

    Assert.That(file.Title, Is.EqualTo(string.Empty));
  }

  [Test]
  [Category("Unit")]
  public void NitfFile_DefaultClassification_IsU() {
    var file = new NitfFile();

    Assert.That(file.Classification, Is.EqualTo('U'));
  }

  [Test]
  [Category("Unit")]
  public void NitfFile_PrimaryExtension_IsNtf() {
    var ext = GetPrimaryExtension();

    Assert.That(ext, Is.EqualTo(".ntf"));
  }

  [Test]
  [Category("Unit")]
  public void NitfFile_FileExtensions_IncludesBothExtensions() {
    var exts = GetFileExtensions();

    Assert.That(exts, Does.Contain(".ntf"));
    Assert.That(exts, Does.Contain(".nitf"));
    Assert.That(exts, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NitfFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NitfFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Bgra32,
      PixelData = new byte[16],
    };

    Assert.Throws<ArgumentException>(() => NitfFile.FromRawImage(raw));
  }

  private static string GetPrimaryExtension() => GetPrimary<NitfFile>();

  private static string GetPrimary<T>() where T : IImageFileFormat<T>
    => T.PrimaryExtension;

  private static string[] GetFileExtensions() => GetExts<NitfFile>();

  private static string[] GetExts<T>() where T : IImageFileFormat<T>
    => T.FileExtensions;
}
