using System;
using FileFormat.FaceSaver;
using FileFormat.Core;

namespace FileFormat.FaceSaver.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void FaceSaverFile_Defaults() {
    var file = new FaceSaverFile();
    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(0));
      Assert.That(file.Height, Is.EqualTo(0));
      Assert.That(file.BitsPerPixel, Is.EqualTo(8));
      Assert.That(file.ImageWidth, Is.EqualTo(0));
      Assert.That(file.ImageHeight, Is.EqualTo(0));
      Assert.That(file.FirstName, Is.EqualTo(string.Empty));
      Assert.That(file.LastName, Is.EqualTo(string.Empty));
      Assert.That(file.Email, Is.EqualTo(string.Empty));
      Assert.That(file.PixelData, Is.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void FaceSaverFile_InitProperties() {
    var file = new FaceSaverFile {
      Width = 48, Height = 48,
      FirstName = "Test",
      PixelData = new byte[48 * 48],
    };
    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(48));
      Assert.That(file.Height, Is.EqualTo(48));
      Assert.That(file.FirstName, Is.EqualTo("Test"));
      Assert.That(file.PixelData, Has.Length.EqualTo(2304));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FaceSaverFile.ToRawImage(null!));

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FaceSaverFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void ToRawImage_ProducesGray8() {
    var file = new FaceSaverFile {
      Width = 2, Height = 2, PixelData = [0x10, 0x20, 0x30, 0x40]
    };
    var raw = FaceSaverFile.ToRawImage(file);
    Assert.Multiple(() => {
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
      Assert.That(raw.Width, Is.EqualTo(2));
      Assert.That(raw.Height, Is.EqualTo(2));
      Assert.That(raw.PixelData, Has.Length.EqualTo(4));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Gray8_Preserves() {
    var raw = new RawImage {
      Width = 3, Height = 2,
      Format = PixelFormat.Gray8,
      PixelData = [0xAA, 0xBB, 0xCC, 0x11, 0x22, 0x33],
    };
    var file = FaceSaverFile.FromRawImage(raw);
    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(3));
      Assert.That(file.Height, Is.EqualTo(2));
      Assert.That(file.BitsPerPixel, Is.EqualTo(8));
      Assert.That(file.PixelData, Has.Length.EqualTo(6));
    });
  }

  [Test]
  [Category("Unit")]
  public void Extensions_Include_Face_And_Fac() {
    var extensions = _GetFileExtensions<FaceSaverFile>();
    Assert.Multiple(() => {
      Assert.That(extensions, Does.Contain(".face"));
      Assert.That(extensions, Does.Contain(".fac"));
    });
  }

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsFace()
    => Assert.That(_GetPrimaryExtension<FaceSaverFile>(), Is.EqualTo(".face"));

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
}
