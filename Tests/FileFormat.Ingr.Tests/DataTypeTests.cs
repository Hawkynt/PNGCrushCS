using System;
using FileFormat.Ingr;

namespace FileFormat.Ingr.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void IngrDataType_HasExpectedValues() {
    Assert.That((ushort)IngrDataType.ByteData, Is.EqualTo(2));
    Assert.That((ushort)IngrDataType.Rgb24, Is.EqualTo(24));

    var values = Enum.GetValues<IngrDataType>();
    Assert.That(values, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void IngrFile_DefaultWidth_IsZero() {
    var file = new IngrFile();

    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void IngrFile_DefaultHeight_IsZero() {
    var file = new IngrFile();

    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void IngrFile_DefaultDataType_IsRgb24() {
    var file = new IngrFile();

    Assert.That(file.DataType, Is.EqualTo(IngrDataType.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void IngrFile_DefaultPixelData_IsEmpty() {
    var file = new IngrFile();

    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void IngrFile_PrimaryExtension_IsCit() {
    var ext = GetPrimaryExtension();

    Assert.That(ext, Is.EqualTo(".cit"));
  }

  [Test]
  [Category("Unit")]
  public void IngrFile_FileExtensions_IncludesBothExtensions() {
    var exts = GetFileExtensions();

    Assert.That(exts, Does.Contain(".cit"));
    Assert.That(exts, Does.Contain(".itg"));
    Assert.That(exts, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IngrFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IngrFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_ThrowsArgumentException() {
    var image = new FileFormat.Core.RawImage {
      Width = 1,
      Height = 1,
      Format = FileFormat.Core.PixelFormat.Bgra32,
      PixelData = new byte[4]
    };

    Assert.Throws<ArgumentException>(() => IngrFile.FromRawImage(image));
  }

  private static string GetPrimaryExtension() => GetPrimary<IngrFile>();

  private static string GetPrimary<T>() where T : FileFormat.Core.IImageFileFormat<T>
    => T.PrimaryExtension;

  private static string[] GetFileExtensions() => GetExts<IngrFile>();

  private static string[] GetExts<T>() where T : FileFormat.Core.IImageFileFormat<T>
    => T.FileExtensions;
}
