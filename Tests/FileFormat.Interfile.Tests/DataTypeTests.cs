using System;
using FileFormat.Core;
using FileFormat.Interfile;

namespace FileFormat.Interfile.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void InterfileFile_DefaultValues() {
    var file = new InterfileFile {
      Width = 10,
      Height = 20
    };

    Assert.That(file.Width, Is.EqualTo(10));
    Assert.That(file.Height, Is.EqualTo(20));
    Assert.That(file.BytesPerPixel, Is.EqualTo(1));
    Assert.That(file.NumberFormat, Is.EqualTo("unsigned integer"));
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void InterfileFile_InitProperties() {
    var pixels = new byte[] { 1, 2, 3 };
    var file = new InterfileFile {
      Width = 3,
      Height = 1,
      BytesPerPixel = 1,
      NumberFormat = "signed integer",
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(3));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.BytesPerPixel, Is.EqualTo(1));
    Assert.That(file.NumberFormat, Is.EqualTo("signed integer"));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void InterfileFile_PrimaryExtension() {
    var ext = _GetStaticProperty<string>("PrimaryExtension");

    Assert.That(ext, Is.EqualTo(".hv"));
  }

  [Test]
  [Category("Unit")]
  public void InterfileFile_FileExtensions() {
    var exts = _GetStaticProperty<string[]>("FileExtensions");

    Assert.That(exts, Has.Length.EqualTo(1));
    Assert.That(exts, Does.Contain(".hv"));
  }

  [Test]
  [Category("Unit")]
  public void InterfileFile_ToRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => InterfileFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void InterfileFile_FromRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => InterfileFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void InterfileFile_FromRawImage_UnsupportedFormat_Throws() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[16]
    };

    Assert.Throws<ArgumentException>(() => InterfileFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void InterfileFile_ToRawImage_Gray8() {
    var pixels = new byte[] { 10, 20, 30, 40 };
    var file = new InterfileFile {
      Width = 2,
      Height = 2,
      BytesPerPixel = 1,
      PixelData = pixels
    };

    var raw = InterfileFile.ToRawImage(file);

    Assert.That(raw.Width, Is.EqualTo(2));
    Assert.That(raw.Height, Is.EqualTo(2));
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void InterfileFile_ToRawImage_Rgb24() {
    var pixels = new byte[2 * 2 * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)i;

    var file = new InterfileFile {
      Width = 2,
      Height = 2,
      BytesPerPixel = 3,
      PixelData = pixels
    };

    var raw = InterfileFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void InterfileFile_ToRawImage_UnsupportedBpp_Throws() {
    var file = new InterfileFile {
      Width = 2,
      Height = 2,
      BytesPerPixel = 4,
      PixelData = new byte[16]
    };

    Assert.Throws<InvalidOperationException>(() => InterfileFile.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void InterfileFile_ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 10, 20, 30, 40 };
    var file = new InterfileFile {
      Width = 2,
      Height = 2,
      BytesPerPixel = 1,
      PixelData = pixels
    };

    var raw = InterfileFile.ToRawImage(file);
    raw.PixelData[0] = 99;

    Assert.That(file.PixelData[0], Is.EqualTo(10));
  }

  [Test]
  [Category("Unit")]
  public void InterfileFile_FromRawImage_ClonesPixelData() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Gray8,
      PixelData = [10, 20, 30, 40]
    };

    var file = InterfileFile.FromRawImage(raw);
    raw.PixelData[0] = 99;

    Assert.That(file.PixelData[0], Is.EqualTo(10));
  }

  [Test]
  [Category("Unit")]
  public void InterfileFile_FromRawImage_Gray8_SetsBytesPerPixel() {
    var raw = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Gray8,
      PixelData = [10, 20]
    };

    var file = InterfileFile.FromRawImage(raw);

    Assert.That(file.BytesPerPixel, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void InterfileFile_FromRawImage_Rgb24_SetsBytesPerPixel() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [10, 20, 30]
    };

    var file = InterfileFile.FromRawImage(raw);

    Assert.That(file.BytesPerPixel, Is.EqualTo(3));
  }

  private static T _GetStaticProperty<T>(string name) {
    var map = typeof(InterfileFile).GetInterfaceMap(typeof(IImageFileFormat<InterfileFile>));
    foreach (var method in map.TargetMethods)
      if (method.Name.Contains(name))
        return (T)method.Invoke(null, null)!;
    throw new InvalidOperationException($"Property {name} not found.");
  }
}
