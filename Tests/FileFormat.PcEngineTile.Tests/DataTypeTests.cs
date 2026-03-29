using System;
using FileFormat.Core;
using FileFormat.PcEngineTile;

namespace FileFormat.PcEngineTile.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PcEngineTileFile_DefaultWidth_Is128() {
    var file = new PcEngineTileFile();
    Assert.That(file.Width, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void PcEngineTileFile_DefaultPixelData_IsEmptyArray() {
    var file = new PcEngineTileFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void PcEngineTileFile_DefaultPalette_Is48Bytes() {
    var file = new PcEngineTileFile();
    Assert.That(file.Palette, Is.Not.Null);
    Assert.That(file.Palette.Length, Is.EqualTo(48));
  }

  [Test]
  [Category("Unit")]
  public void PcEngineTileFile_DefaultPalette_16GrayscaleColors() {
    var file = new PcEngineTileFile();
    // First entry: black (0,0,0)
    Assert.That(file.Palette[0], Is.EqualTo(0));
    Assert.That(file.Palette[1], Is.EqualTo(0));
    Assert.That(file.Palette[2], Is.EqualTo(0));
    // Last entry: white (255,255,255)
    Assert.That(file.Palette[45], Is.EqualTo(255));
    Assert.That(file.Palette[46], Is.EqualTo(255));
    Assert.That(file.Palette[47], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void PcEngineTileFile_DefaultPalette_IsGrayscale() {
    var file = new PcEngineTileFile();
    for (var i = 0; i < 16; ++i) {
      var r = file.Palette[i * 3];
      var g = file.Palette[i * 3 + 1];
      var b = file.Palette[i * 3 + 2];
      Assert.That(r, Is.EqualTo(g), $"Palette entry {i}: R != G");
      Assert.That(g, Is.EqualTo(b), $"Palette entry {i}: G != B");
    }
  }

  [Test]
  [Category("Unit")]
  public void PcEngineTileFile_DefaultPalette_IsMonotonicallyIncreasing() {
    var file = new PcEngineTileFile();
    for (var i = 1; i < 16; ++i)
      Assert.That(file.Palette[i * 3], Is.GreaterThan(file.Palette[(i - 1) * 3]), $"Palette entry {i} should be brighter than {i - 1}");
  }

  [Test]
  [Category("Unit")]
  public void PcEngineTileFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0, 1, 2, 3 };
    var palette = new byte[48];
    for (var i = 0; i < 48; ++i)
      palette[i] = (byte)(i + 10);

    var file = new PcEngineTileFile {
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
  public void PcEngineTileFile_PrimaryExtension_IsPce() {
    Assert.That(_GetPrimaryExtension<PcEngineTileFile>(), Is.EqualTo(".pce"));
  }

  [Test]
  [Category("Unit")]
  public void PcEngineTileFile_FileExtensions_ContainsPce() {
    var exts = _GetFileExtensions<PcEngineTileFile>();
    Assert.That(exts, Does.Contain(".pce"));
  }

  [Test]
  [Category("Unit")]
  public void PcEngineTileFile_FileExtensions_HasSingleEntry() {
    var exts = _GetFileExtensions<PcEngineTileFile>();
    Assert.That(exts.Length, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcEngineTileFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsIndexed8() {
    var file = new PcEngineTileFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8]
    };

    var raw = PcEngineTileFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_PaletteCount_Is16() {
    var file = new PcEngineTileFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8]
    };

    var raw = PcEngineTileFile.ToRawImage(file);

    Assert.That(raw.PaletteCount, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[128 * 8];
    pixels[0] = 7;
    var file = new PcEngineTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var raw = PcEngineTileFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(file.PixelData));
    Assert.That(raw.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPalette() {
    var file = new PcEngineTileFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8]
    };

    var raw = PcEngineTileFile.ToRawImage(file);

    Assert.That(raw.Palette, Is.Not.SameAs(file.Palette));
    Assert.That(raw.Palette, Is.EqualTo(file.Palette));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PcEngineTileFile.FromRawImage(null!));
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

    Assert.Throws<ArgumentException>(() => PcEngineTileFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongWidth_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 64,
      Height = 8,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[64 * 8],
      PaletteCount = 16
    };

    Assert.Throws<ArgumentException>(() => PcEngineTileFile.FromRawImage(raw));
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

    Assert.Throws<ArgumentException>(() => PcEngineTileFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixels = new byte[128 * 8];
    pixels[0] = 5;
    var raw = new RawImage {
      Width = 128,
      Height = 8,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      PaletteCount = 16,
      Palette = new byte[48]
    };

    var file = PcEngineTileFile.FromRawImage(raw);

    Assert.That(file.PixelData, Is.Not.SameAs(raw.PixelData));
    Assert.That(file.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_NullPalette_UsesDefault() {
    var raw = new RawImage {
      Width = 128,
      Height = 8,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[128 * 8],
      PaletteCount = 16,
      Palette = null
    };

    var file = PcEngineTileFile.FromRawImage(raw);

    Assert.That(file.Palette, Is.Not.Null);
    Assert.That(file.Palette.Length, Is.EqualTo(48));
  }

  [Test]
  [Category("Unit")]
  public void BytesPerTile_Is32() {
    Assert.That(PcEngineTileFile.BytesPerTile, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void TileSize_Is8() {
    Assert.That(PcEngineTileFile.TileSize, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void TilesPerRow_Is16() {
    Assert.That(PcEngineTileFile.TilesPerRow, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void FixedWidth_Is128() {
    Assert.That(PcEngineTileFile.FixedWidth, Is.EqualTo(128));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
