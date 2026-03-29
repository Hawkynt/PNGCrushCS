using System;
using FileFormat.Xcursor;
using FileFormat.Core;

namespace FileFormat.Xcursor.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void XcursorFile_DefaultPixelData_IsEmptyArray() {
    var file = new XcursorFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void XcursorFile_DefaultWidth_IsZero() {
    var file = new XcursorFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void XcursorFile_DefaultHeight_IsZero() {
    var file = new XcursorFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void XcursorFile_DefaultXHot_IsZero() {
    var file = new XcursorFile();
    Assert.That(file.XHot, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void XcursorFile_DefaultYHot_IsZero() {
    var file = new XcursorFile();
    Assert.That(file.YHot, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void XcursorFile_DefaultNominalSize_IsZero() {
    var file = new XcursorFile();
    Assert.That(file.NominalSize, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void XcursorFile_DefaultDelay_IsZero() {
    var file = new XcursorFile();
    Assert.That(file.Delay, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void XcursorFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80, 0xCC };
    var file = new XcursorFile {
      Width = 1,
      Height = 1,
      XHot = 3,
      YHot = 5,
      NominalSize = 48,
      Delay = 200,
      PixelData = pixels
    };

    Assert.That(file.Width, Is.EqualTo(1));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.XHot, Is.EqualTo(3));
    Assert.That(file.YHot, Is.EqualTo(5));
    Assert.That(file.NominalSize, Is.EqualTo(48));
    Assert.That(file.Delay, Is.EqualTo(200));
    Assert.That(file.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void XcursorFile_PrimaryExtension_IsXcur() {
    var ext = _GetPrimaryExtension<XcursorFile>();
    Assert.That(ext, Is.EqualTo(".xcur"));
  }

  [Test]
  [Category("Unit")]
  public void XcursorFile_FileExtensions_ContainsBothExtensions() {
    var exts = _GetFileExtensions<XcursorFile>();
    Assert.That(exts, Has.Length.EqualTo(2));
    Assert.That(exts, Does.Contain(".xcur"));
    Assert.That(exts, Does.Contain(".cursor"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XcursorFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => XcursorFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[3]
    };
    Assert.Throws<ArgumentException>(() => XcursorFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsRgba32Format() {
    var file = new XcursorFile {
      Width = 1,
      Height = 1,
      PixelData = [0x80, 0x40, 0xC0, 0xFF]
    };

    var raw = XcursorFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(raw.Width, Is.EqualTo(1));
    Assert.That(raw.Height, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_UnpremultipliesAlpha() {
    var file = new XcursorFile {
      Width = 1,
      Height = 1,
      PixelData = [0x40, 0x20, 0x60, 0x80]
    };

    var raw = XcursorFile.ToRawImage(file);

    var a = raw.PixelData[3];
    Assert.That(a, Is.EqualTo(0x80));

    var r = raw.PixelData[0];
    Assert.That(r, Is.GreaterThan(0x60));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_OpaquePixelsUnchanged() {
    var file = new XcursorFile {
      Width = 1,
      Height = 1,
      PixelData = [0x30, 0x60, 0x90, 0xFF]
    };

    var raw = XcursorFile.ToRawImage(file);

    Assert.That(raw.PixelData[0], Is.EqualTo(0x90));
    Assert.That(raw.PixelData[1], Is.EqualTo(0x60));
    Assert.That(raw.PixelData[2], Is.EqualTo(0x30));
    Assert.That(raw.PixelData[3], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_PremultipliesAlpha() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = [0xFF, 0x80, 0x40, 0x80]
    };

    var file = XcursorFile.FromRawImage(raw);

    var a = file.PixelData[3];
    Assert.That(a, Is.EqualTo(0x80));

    var r = file.PixelData[2];
    Assert.That(r, Is.LessThan(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SetsNominalSizeToMaxDimension() {
    var raw = new RawImage {
      Width = 32,
      Height = 48,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[32 * 48 * 4]
    };

    var file = XcursorFile.FromRawImage(raw);

    Assert.That(file.NominalSize, Is.EqualTo(48));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_TransparentPixelPreservesZero() {
    var file = new XcursorFile {
      Width = 1,
      Height = 1,
      PixelData = [0x00, 0x00, 0x00, 0x00]
    };

    var raw = XcursorFile.ToRawImage(file);

    Assert.That(raw.PixelData[0], Is.EqualTo(0));
    Assert.That(raw.PixelData[1], Is.EqualTo(0));
    Assert.That(raw.PixelData[2], Is.EqualTo(0));
    Assert.That(raw.PixelData[3], Is.EqualTo(0));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T>
    => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T>
    => T.FileExtensions;
}
