using System;
using FileFormat.Core;
using FileFormat.NesChr;

namespace FileFormat.NesChr.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void NesChrFile_DefaultWidth_Is128() {
    var file = new NesChrFile();
    Assert.That(file.Width, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void NesChrFile_DefaultPixelData_IsEmptyArray() {
    var file = new NesChrFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void NesChrFile_DefaultPalette_Is12Bytes() {
    var file = new NesChrFile();
    Assert.That(file.Palette, Is.Not.Null);
    Assert.That(file.Palette.Length, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void NesChrFile_DefaultPalette_4GrayscaleColors() {
    var file = new NesChrFile();
    Assert.That(file.Palette[0], Is.EqualTo(0));
    Assert.That(file.Palette[1], Is.EqualTo(0));
    Assert.That(file.Palette[2], Is.EqualTo(0));
    Assert.That(file.Palette[3], Is.EqualTo(85));
    Assert.That(file.Palette[4], Is.EqualTo(85));
    Assert.That(file.Palette[5], Is.EqualTo(85));
    Assert.That(file.Palette[6], Is.EqualTo(170));
    Assert.That(file.Palette[7], Is.EqualTo(170));
    Assert.That(file.Palette[8], Is.EqualTo(170));
    Assert.That(file.Palette[9], Is.EqualTo(255));
    Assert.That(file.Palette[10], Is.EqualTo(255));
    Assert.That(file.Palette[11], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void NesChrFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0, 1, 2, 3 };
    var palette = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120 };
    var file = new NesChrFile {
      Width = 128,
      Height = 8,
      PixelData = pixels,
      Palette = palette
    };

    Assert.That(file.Width, Is.EqualTo(128));
    Assert.That(file.Height, Is.EqualTo(8));
    Assert.That(file.PixelData, Is.SameAs(pixels));
    Assert.That(file.Palette, Is.SameAs(palette));
  }

  [Test]
  [Category("Unit")]
  public void NesChrFile_PrimaryExtension_IsChr() {
    Assert.That(_GetPrimaryExtension<NesChrFile>(), Is.EqualTo(".chr"));
  }

  [Test]
  [Category("Unit")]
  public void NesChrFile_FileExtensions_ContainsChr() {
    var exts = _GetFileExtensions<NesChrFile>();
    Assert.That(exts, Does.Contain(".chr"));
  }

  [Test]
  [Category("Unit")]
  public void NesChrFile_FileExtensions_HasSingleEntry() {
    var exts = _GetFileExtensions<NesChrFile>();
    Assert.That(exts.Length, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NesChrFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsIndexed8() {
    var file = new NesChrFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8]
    };

    var raw = NesChrFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_PaletteCount_Is4() {
    var file = new NesChrFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8]
    };

    var raw = NesChrFile.ToRawImage(file);

    Assert.That(raw.PaletteCount, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[128 * 8];
    pixels[0] = 2;
    var file = new NesChrFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var raw = NesChrFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(file.PixelData));
    Assert.That(raw.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => NesChrFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 128,
      Height = 8,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[128 * 8 * 3]
    };

    Assert.Throws<ArgumentException>(() => NesChrFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongWidth_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 64,
      Height = 8,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[64 * 8],
      PaletteCount = 4
    };

    Assert.Throws<ArgumentException>(() => NesChrFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_TooManyPaletteEntries_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 128,
      Height = 8,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[128 * 8],
      PaletteCount = 256
    };

    Assert.Throws<ArgumentException>(() => NesChrFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixels = new byte[128 * 8];
    pixels[0] = 1;
    var raw = new RawImage {
      Width = 128,
      Height = 8,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      PaletteCount = 4,
      Palette = new byte[12]
    };

    var file = NesChrFile.FromRawImage(raw);

    Assert.That(file.PixelData, Is.Not.SameAs(raw.PixelData));
    Assert.That(file.PixelData, Is.EqualTo(raw.PixelData));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
