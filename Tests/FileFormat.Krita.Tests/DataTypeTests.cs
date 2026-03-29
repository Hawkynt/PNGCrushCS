using System;
using FileFormat.Core;
using FileFormat.Krita;

namespace FileFormat.Krita.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void KritaFile_DefaultPixelData_IsEmptyArray() {
    var file = new KritaFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void KritaFile_DefaultWidth_IsZero() {
    var file = new KritaFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void KritaFile_DefaultHeight_IsZero() {
    var file = new KritaFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void KritaFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80, 0x40 };
    var file = new KritaFile {
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
  public void KritaFile_PrimaryExtension_IsKra() {
    var extension = GetPrimaryExtension();
    Assert.That(extension, Is.EqualTo(".kra"));
  }

  [Test]
  [Category("Unit")]
  public void KritaFile_FileExtensions_ContainsKra() {
    var extensions = GetFileExtensions();
    Assert.That(extensions, Does.Contain(".kra"));
  }

  [Test]
  [Category("Unit")]
  public void KritaFile_FileExtensions_HasSingleEntry() {
    var extensions = GetFileExtensions();
    Assert.That(extensions, Has.Length.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => KritaFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsRgba32Format() {
    var file = new KritaFile {
      Width = 1,
      Height = 1,
      PixelData = [0xFF, 0x00, 0x80, 0x40]
    };

    var raw = KritaFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgba32));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80, 0x40 };
    var file = new KritaFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    var raw = KritaFile.ToRawImage(file);
    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => KritaFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [0xFF, 0x00, 0x80]
    };

    Assert.Throws<ArgumentException>(() => KritaFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80, 0x40 };
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = pixels
    };

    var file = KritaFile.FromRawImage(raw);
    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  private static string GetPrimaryExtension() => CallStaticInterface<string>(nameof(IImageFileFormat<KritaFile>.PrimaryExtension));
  private static string[] GetFileExtensions() => CallStaticInterface<string[]>(nameof(IImageFileFormat<KritaFile>.FileExtensions));

  private static T CallStaticInterface<T>(string propertyName) {
    var prop = typeof(KritaFile).GetInterfaceMap(typeof(IImageFileFormat<KritaFile>))
      .TargetMethods;
    foreach (var method in prop)
      if (method.Name.Contains(propertyName))
        return (T)method.Invoke(null, null)!;

    // Fallback via interface property
    var interfaceType = typeof(IImageFileFormat<KritaFile>);
    var iProp = interfaceType.GetProperty(propertyName);
    return (T)iProp!.GetValue(null)!;
  }
}
