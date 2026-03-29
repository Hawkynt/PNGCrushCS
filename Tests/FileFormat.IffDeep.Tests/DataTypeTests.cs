using System;
using FileFormat.Core;
using FileFormat.IffDeep;

namespace FileFormat.IffDeep.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void IffDeepCompression_None_IsZero() {
    Assert.That((ushort)IffDeepCompression.None, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void IffDeepCompression_Rle_IsOne() {
    Assert.That((ushort)IffDeepCompression.Rle, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void IffDeepCompression_HasTwoValues() {
    var values = Enum.GetValues<IffDeepCompression>();
    Assert.That(values, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void IffDeepFile_DefaultPixelData_IsEmptyArray() {
    var file = new IffDeepFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void IffDeepFile_DefaultWidth_IsZero() {
    var file = new IffDeepFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void IffDeepFile_DefaultHeight_IsZero() {
    var file = new IffDeepFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void IffDeepFile_DefaultHasAlpha_IsFalse() {
    var file = new IffDeepFile();
    Assert.That(file.HasAlpha, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void IffDeepFile_DefaultCompression_IsNone() {
    var file = new IffDeepFile();
    Assert.That(file.Compression, Is.EqualTo(IffDeepCompression.None));
  }

  [Test]
  [Category("Unit")]
  public void IffDeepFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80, 0x40 };
    var file = new IffDeepFile {
      Width = 1,
      Height = 1,
      HasAlpha = true,
      Compression = IffDeepCompression.Rle,
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.HasAlpha, Is.True);
    Assert.That(file.Compression, Is.EqualTo(IffDeepCompression.Rle));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void IffDeepFile_PrimaryExtension_IsDeep() {
    var ext = _GetPrimaryExtension<IffDeepFile>();
    Assert.That(ext, Is.EqualTo(".deep"));
  }

  [Test]
  [Category("Unit")]
  public void IffDeepFile_FileExtensions_ContainsBoth() {
    var exts = _GetFileExtensions<IffDeepFile>();
    Assert.That(exts, Does.Contain(".deep"));
    Assert.That(exts, Does.Contain(".iff"));
  }

  [Test]
  [Category("Unit")]
  public void IffDeepFile_FileExtensions_HasTwoEntries() {
    var exts = _GetFileExtensions<IffDeepFile>();
    Assert.That(exts, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => IffDeepFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => IffDeepFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = new byte[4]
    };

    Assert.Throws<ArgumentException>(() => IffDeepFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgb_ReturnsRgb24() {
    var file = new IffDeepFile {
      Width = 2,
      Height = 1,
      HasAlpha = false,
      PixelData = [0xFF, 0x00, 0x80, 0x11, 0x22, 0x33]
    };

    var raw = IffDeepFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw.Width, Is.EqualTo(2));
    Assert.That(raw.Height, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Rgba_ReturnsRgba32() {
    var file = new IffDeepFile {
      Width = 1,
      Height = 1,
      HasAlpha = true,
      PixelData = [0xFF, 0x00, 0x80, 0xCC]
    };

    var raw = IffDeepFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgba32));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var file = new IffDeepFile {
      Width = 1,
      Height = 1,
      HasAlpha = false,
      PixelData = pixels
    };

    var raw = IffDeepFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = pixels
    };

    var file = IffDeepFile.FromRawImage(raw);

    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgb24_SetsHasAlphaFalse() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[3]
    };

    var file = IffDeepFile.FromRawImage(raw);

    Assert.That(file.HasAlpha, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Rgba32_SetsHasAlphaTrue() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[4]
    };

    var file = IffDeepFile.FromRawImage(raw);

    Assert.That(file.HasAlpha, Is.True);
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
