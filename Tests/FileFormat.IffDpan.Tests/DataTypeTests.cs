using System;
using FileFormat.Core;
using FileFormat.IffDpan;

namespace FileFormat.IffDpan.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void IffDpanFile_DefaultWidth_Is320() {
    var file = new IffDpanFile();
    Assert.That(file.Width, Is.EqualTo(IffDpanFile.DefaultWidth));
  }

  [Test]
  [Category("Unit")]
  public void IffDpanFile_DefaultHeight_Is200() {
    var file = new IffDpanFile();
    Assert.That(file.Height, Is.EqualTo(IffDpanFile.DefaultHeight));
  }

  [Test]
  [Category("Unit")]
  public void IffDpanFile_DefaultRawData_IsEmptyArray() {
    var file = new IffDpanFile();
    Assert.That(file.RawData, Is.Not.Null);
    Assert.That(file.RawData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void IffDpanFile_InitProperties_RoundTrip() {
    var rawData = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new IffDpanFile {
      Width = 640,
      Height = 480,
      RawData = rawData,
    };

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(640));
      Assert.That(file.Height, Is.EqualTo(480));
      Assert.That(file.RawData, Is.SameAs(rawData));
    });
  }

  [Test]
  [Category("Unit")]
  public void MinFileSize_Is12() {
    Assert.That(IffDpanFile.MinFileSize, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void DefaultWidth_Is320() {
    Assert.That(IffDpanFile.DefaultWidth, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void DefaultHeight_Is200() {
    Assert.That(IffDpanFile.DefaultHeight, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsDpan() {
    var ext = _GetPrimaryExtension<IffDpanFile>();
    Assert.That(ext, Is.EqualTo(".dpan"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsExpected() {
    var extensions = _GetFileExtensions<IffDpanFile>();
    Assert.Multiple(() => {
      Assert.That(extensions, Has.Length.EqualTo(1));
      Assert.That(extensions, Does.Contain(".dpan"));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffDpanFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsRgb24() {
    var file = new IffDpanFile {
      Width = 2,
      Height = 2,
      RawData = new byte[20],
    };

    var raw = IffDpanFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(raw.Width, Is.EqualTo(2));
      Assert.That(raw.Height, Is.EqualTo(2));
      Assert.That(raw.PixelData, Has.Length.EqualTo(2 * 2 * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_PixelDataIsZeroFilled() {
    var file = new IffDpanFile {
      Width = 1,
      Height = 1,
      RawData = new byte[12],
    };

    var raw = IffDpanFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.All.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffDpanFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[3],
    };

    Assert.Throws<NotSupportedException>(() => IffDpanFile.FromRawImage(raw));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
