using System;
using FileFormat.IffPbm;
using FileFormat.Core;

namespace FileFormat.IffPbm.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void IffPbmCompression_HasExpectedValues() {
    Assert.That((int)IffPbmCompression.None, Is.EqualTo(0));
    Assert.That((int)IffPbmCompression.ByteRun1, Is.EqualTo(1));

    var values = Enum.GetValues<IffPbmCompression>();
    Assert.That(values, Has.Length.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void IffPbmFile_Defaults() {
    var file = new IffPbmFile();
    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(0));
      Assert.That(file.Height, Is.EqualTo(0));
      Assert.That(file.Compression, Is.EqualTo(IffPbmCompression.None));
      Assert.That(file.TransparentColor, Is.EqualTo(0));
      Assert.That(file.XAspect, Is.EqualTo(0));
      Assert.That(file.YAspect, Is.EqualTo(0));
      Assert.That(file.PageWidth, Is.EqualTo(0));
      Assert.That(file.PageHeight, Is.EqualTo(0));
      Assert.That(file.PixelData, Is.Not.Null);
      Assert.That(file.PixelData, Is.Empty);
      Assert.That(file.Palette, Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void IffPbmFile_InitProperties() {
    var file = new IffPbmFile {
      Width = 100,
      Height = 50,
      Compression = IffPbmCompression.ByteRun1,
      TransparentColor = 5,
      XAspect = 10,
      YAspect = 11,
      PageWidth = 320,
      PageHeight = 200,
    };

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(100));
      Assert.That(file.Height, Is.EqualTo(50));
      Assert.That(file.Compression, Is.EqualTo(IffPbmCompression.ByteRun1));
      Assert.That(file.TransparentColor, Is.EqualTo(5));
      Assert.That(file.XAspect, Is.EqualTo(10));
      Assert.That(file.YAspect, Is.EqualTo(11));
      Assert.That(file.PageWidth, Is.EqualTo(320));
      Assert.That(file.PageHeight, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void IffPbmFile_PrimaryExtension_IsLbm() {
    Assert.That(GetPrimaryExtension(), Is.EqualTo(".lbm"));
  }

  [Test]
  [Category("Unit")]
  public void IffPbmFile_FileExtensions_ContainsLbmAndPbm() {
    var extensions = GetFileExtensions();
    Assert.Multiple(() => {
      Assert.That(extensions, Has.Length.EqualTo(2));
      Assert.That(extensions, Does.Contain(".lbm"));
      Assert.That(extensions, Does.Contain(".pbm"));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffPbmFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffPbmFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[2 * 2 * 3],
    };
    Assert.Throws<ArgumentException>(() => IffPbmFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsIndexed8() {
    var file = new IffPbmFile {
      Width = 2,
      Height = 2,
      PixelData = [0, 1, 2, 3],
      Palette = new byte[4 * 3],
    };

    var raw = IffPbmFile.ToRawImage(file);
    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixelData = new byte[] { 0, 1, 2, 3 };
    var file = new IffPbmFile {
      Width = 2,
      Height = 2,
      PixelData = pixelData,
      Palette = new byte[4 * 3],
    };

    var raw = IffPbmFile.ToRawImage(file);
    Assert.That(raw.PixelData, Is.Not.SameAs(pixelData));
    Assert.That(raw.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixelData = new byte[] { 0, 1, 2, 3 };
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Indexed8,
      PixelData = pixelData,
      Palette = new byte[4 * 3],
      PaletteCount = 4,
    };

    var file = IffPbmFile.FromRawImage(raw);
    Assert.That(file.PixelData, Is.Not.SameAs(pixelData));
    Assert.That(file.PixelData, Is.EqualTo(pixelData));
  }

  // Helpers to access static interface members
  private static string GetPrimaryExtension() => Access.PrimaryExtension;
  private static string[] GetFileExtensions() => Access.FileExtensions;

  private static class Access {
    public static string PrimaryExtension => GetIt();
    public static string[] FileExtensions => GetExts();

    private static string GetIt() {
      // Access via interface static abstract member
      return CallPrimary<IffPbmFile>();
    }

    private static string[] GetExts() {
      return CallExtensions<IffPbmFile>();
    }

    private static string CallPrimary<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
    private static string[] CallExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
  }
}
