using System;
using FileFormat.JpegXr;
using FileFormat.Core;

namespace FileFormat.JpegXr.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void JpegXrFile_DefaultPixelData_IsEmptyArray() {
    var file = new JpegXrFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void JpegXrFile_DefaultWidth_IsZero() {
    var file = new JpegXrFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void JpegXrFile_DefaultHeight_IsZero() {
    var file = new JpegXrFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void JpegXrFile_DefaultComponentCount_IsZero() {
    var file = new JpegXrFile();
    Assert.That(file.ComponentCount, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void JpegXrFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new JpegXrFile {
      Width = 1,
      Height = 1,
      ComponentCount = 3,
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.ComponentCount, Is.EqualTo(3));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsJxr() {
    Assert.That(_GetPrimaryExtension<JpegXrFile>(), Is.EqualTo(".jxr"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsJxrWdpHdp() {
    var extensions = _GetFileExtensions<JpegXrFile>();
    Assert.That(extensions, Does.Contain(".jxr"));
    Assert.That(extensions, Does.Contain(".wdp"));
    Assert.That(extensions, Does.Contain(".hdp"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_CountIs3() {
    var extensions = _GetFileExtensions<JpegXrFile>();
    Assert.That(extensions.Length, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JpegXrFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => JpegXrFile.FromRawImage(null!));
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

    Assert.Throws<ArgumentException>(() => JpegXrFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_UnsupportedComponentCount_ThrowsNotSupportedException() {
    var file = new JpegXrFile {
      Width = 1,
      Height = 1,
      ComponentCount = 4,
      PixelData = new byte[4]
    };

    Assert.Throws<NotSupportedException>(() => JpegXrFile.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Gray8_Format() {
    var file = new JpegXrFile {
      Width = 1,
      Height = 1,
      ComponentCount = 1,
      PixelData = [0x80]
    };

    var raw = JpegXrFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb24_Format() {
    var file = new JpegXrFile {
      Width = 1,
      Height = 1,
      ComponentCount = 3,
      PixelData = [0xAA, 0xBB, 0xCC]
    };

    var raw = JpegXrFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0x42 };
    var file = new JpegXrFile {
      Width = 1,
      Height = 1,
      ComponentCount = 1,
      PixelData = pixels
    };

    var raw = JpegXrFile.ToRawImage(file);
    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0x42, 0x43, 0x44 };
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = pixels
    };

    var file = JpegXrFile.FromRawImage(raw);
    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
