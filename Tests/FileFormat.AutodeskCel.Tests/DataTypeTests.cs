using System;
using FileFormat.AutodeskCel;
using FileFormat.Core;

namespace FileFormat.AutodeskCel.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void AutodeskCelFile_DefaultPixelData_IsEmptyArray() {
    var file = new AutodeskCelFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void AutodeskCelFile_DefaultWidth_IsZero() {
    var file = new AutodeskCelFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AutodeskCelFile_DefaultHeight_IsZero() {
    var file = new AutodeskCelFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AutodeskCelFile_DefaultXOffset_IsZero() {
    var file = new AutodeskCelFile();
    Assert.That(file.XOffset, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AutodeskCelFile_DefaultYOffset_IsZero() {
    var file = new AutodeskCelFile();
    Assert.That(file.YOffset, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AutodeskCelFile_DefaultBitsPerPixel_Is8() {
    var file = new AutodeskCelFile();
    Assert.That(file.BitsPerPixel, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void AutodeskCelFile_DefaultCompression_IsZero() {
    var file = new AutodeskCelFile();
    Assert.That(file.Compression, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AutodeskCelFile_DefaultPalette_IsGrayscale() {
    var file = new AutodeskCelFile();
    Assert.That(file.Palette, Is.Not.Null);
    Assert.That(file.Palette.Length, Is.EqualTo(AutodeskCelFile.PaletteSize));

    // Check entry 0 = (0, 0, 0)
    Assert.That(file.Palette[0], Is.EqualTo(0));
    Assert.That(file.Palette[1], Is.EqualTo(0));
    Assert.That(file.Palette[2], Is.EqualTo(0));

    // Check entry 255 = (255, 255, 255)
    Assert.That(file.Palette[765], Is.EqualTo(255));
    Assert.That(file.Palette[766], Is.EqualTo(255));
    Assert.That(file.Palette[767], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void AutodeskCelFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0xFF, 0x00, 0x80 };
    var palette = new byte[AutodeskCelFile.PaletteSize];
    var file = new AutodeskCelFile {
      Width = 3,
      Height = 1,
      XOffset = 10,
      YOffset = 20,
      BitsPerPixel = 8,
      Compression = 0,
      PixelData = pixels,
      Palette = palette,
    };

    Assert.That(file.Width, Is.EqualTo(3));
    Assert.That(file.Height, Is.EqualTo(1));
    Assert.That(file.XOffset, Is.EqualTo(10));
    Assert.That(file.YOffset, Is.EqualTo(20));
    Assert.That(file.BitsPerPixel, Is.EqualTo(8));
    Assert.That(file.Compression, Is.EqualTo(0));
    Assert.That(file.PixelData, Is.SameAs(pixels));
    Assert.That(file.Palette, Is.SameAs(palette));
  }

  [Test]
  [Category("Unit")]
  public void AutodeskCelFile_PrimaryExtension_IsCel() {
    var ext = _GetPrimaryExtension<AutodeskCelFile>();
    Assert.That(ext, Is.EqualTo(".cel"));
  }

  [Test]
  [Category("Unit")]
  public void AutodeskCelFile_FileExtensions_ContainsCel() {
    var exts = _GetFileExtensions<AutodeskCelFile>();
    Assert.That(exts, Has.Length.EqualTo(1));
    Assert.That(exts[0], Is.EqualTo(".cel"));
  }

  [Test]
  [Category("Unit")]
  public void AutodeskCelFile_Magic_Is0x9119() {
    Assert.That(AutodeskCelFile.Magic, Is.EqualTo(0x9119));
  }

  [Test]
  [Category("Unit")]
  public void AutodeskCelFile_HeaderSize_Is16() {
    Assert.That(AutodeskCelFile.HeaderSize, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void AutodeskCelFile_PaletteSize_Is768() {
    Assert.That(AutodeskCelFile.PaletteSize, Is.EqualTo(768));
  }

  [Test]
  [Category("Unit")]
  public void AutodeskCelFile_PaletteEntryCount_Is256() {
    Assert.That(AutodeskCelFile.PaletteEntryCount, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AutodeskCelFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AutodeskCelFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UnsupportedFormat_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[3],
    };
    Assert.Throws<ArgumentException>(() => AutodeskCelFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_TooManyPaletteEntries_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[1],
      Palette = new byte[257 * 3],
      PaletteCount = 257,
    };
    Assert.Throws<ArgumentException>(() => AutodeskCelFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsIndexed8Format() {
    var file = new AutodeskCelFile {
      Width = 1,
      Height = 1,
      PixelData = [42],
    };

    var raw = AutodeskCelFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(raw.Width, Is.EqualTo(1));
    Assert.That(raw.Height, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 10, 20, 30 };
    var file = new AutodeskCelFile {
      Width = 3,
      Height = 1,
      PixelData = pixels,
    };

    var raw = AutodeskCelFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPalette() {
    var palette = new byte[AutodeskCelFile.PaletteSize];
    palette[0] = 42;
    var file = new AutodeskCelFile {
      Width = 1,
      Height = 1,
      PixelData = [0],
      Palette = palette,
    };

    var raw = AutodeskCelFile.ToRawImage(file);

    Assert.That(raw.Palette, Is.Not.SameAs(palette));
    Assert.That(raw.Palette, Is.EqualTo(palette));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixels = new byte[] { 10, 20, 30 };
    var raw = new RawImage {
      Width = 3,
      Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = new byte[AutodeskCelFile.PaletteSize],
      PaletteCount = AutodeskCelFile.PaletteEntryCount,
    };

    var file = AutodeskCelFile.FromRawImage(raw);

    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_NoPalette_UsesGrayscale() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = [0],
    };

    var file = AutodeskCelFile.FromRawImage(raw);

    Assert.That(file.Palette.Length, Is.EqualTo(AutodeskCelFile.PaletteSize));
    // Entry 0 = (0, 0, 0) default grayscale
    Assert.That(file.Palette[0], Is.EqualTo(0));
    Assert.That(file.Palette[1], Is.EqualTo(0));
    Assert.That(file.Palette[2], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_PaletteCount_IsCorrect() {
    var file = new AutodeskCelFile {
      Width = 1,
      Height = 1,
      PixelData = [0],
    };

    var raw = AutodeskCelFile.ToRawImage(file);

    Assert.That(raw.PaletteCount, Is.EqualTo(AutodeskCelFile.PaletteEntryCount));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T>
    => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T>
    => T.FileExtensions;
}
