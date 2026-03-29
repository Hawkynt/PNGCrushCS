using System;
using FileFormat.Core;
using FileFormat.MasterSystemTile;

namespace FileFormat.MasterSystemTile.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void MasterSystemTileFile_DefaultWidth_Is128() {
    var file = new MasterSystemTileFile();
    Assert.That(file.Width, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void MasterSystemTileFile_DefaultPixelData_IsEmptyArray() {
    var file = new MasterSystemTileFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void MasterSystemTileFile_DefaultPalette_Is48Bytes() {
    var file = new MasterSystemTileFile();
    Assert.That(file.Palette, Is.Not.Null);
    Assert.That(file.Palette.Length, Is.EqualTo(48));
  }

  [Test]
  [Category("Unit")]
  public void MasterSystemTileFile_DefaultPalette_16GrayscaleColors() {
    var file = new MasterSystemTileFile();
    Assert.That(file.Palette[0], Is.EqualTo(0));
    Assert.That(file.Palette[1], Is.EqualTo(0));
    Assert.That(file.Palette[2], Is.EqualTo(0));
    Assert.That(file.Palette[45], Is.EqualTo(255));
    Assert.That(file.Palette[46], Is.EqualTo(255));
    Assert.That(file.Palette[47], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void MasterSystemTileFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
    var palette = new byte[48];
    var file = new MasterSystemTileFile {
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
  public void MasterSystemTileFile_PrimaryExtension_IsSms() {
    Assert.That(_GetPrimaryExtension<MasterSystemTileFile>(), Is.EqualTo(".sms"));
  }

  [Test]
  [Category("Unit")]
  public void MasterSystemTileFile_FileExtensions_ContainsSms() {
    var exts = _GetFileExtensions<MasterSystemTileFile>();
    Assert.That(exts, Does.Contain(".sms"));
  }

  [Test]
  [Category("Unit")]
  public void MasterSystemTileFile_FileExtensions_HasTwoEntries() {
    var exts = _GetFileExtensions<MasterSystemTileFile>();
    Assert.That(exts.Length, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MasterSystemTileFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsIndexed8() {
    var file = new MasterSystemTileFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8]
    };

    var raw = MasterSystemTileFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_PaletteCount_Is16() {
    var file = new MasterSystemTileFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8]
    };

    var raw = MasterSystemTileFile.ToRawImage(file);

    Assert.That(raw.PaletteCount, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[128 * 8];
    pixels[0] = 7;
    var file = new MasterSystemTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var raw = MasterSystemTileFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(file.PixelData));
    Assert.That(raw.PixelData, Is.EqualTo(file.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => MasterSystemTileFile.FromRawImage(null!));
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

    Assert.Throws<ArgumentException>(() => MasterSystemTileFile.FromRawImage(raw));
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

    Assert.Throws<ArgumentException>(() => MasterSystemTileFile.FromRawImage(raw));
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

    Assert.Throws<ArgumentException>(() => MasterSystemTileFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPixelData() {
    var pixels = new byte[128 * 8];
    pixels[0] = 3;
    var raw = new RawImage {
      Width = 128,
      Height = 8,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      PaletteCount = 16,
      Palette = new byte[48]
    };

    var file = MasterSystemTileFile.FromRawImage(raw);

    Assert.That(file.PixelData, Is.Not.SameAs(raw.PixelData));
    Assert.That(file.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void MasterSystemTileFile_FileExtensions_ContainsGg() {
    var exts = _GetFileExtensions<MasterSystemTileFile>();
    Assert.That(exts, Does.Contain(".gg"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPalette() {
    var file = new MasterSystemTileFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8]
    };

    var raw = MasterSystemTileFile.ToRawImage(file);

    Assert.That(raw.Palette, Is.Not.SameAs(file.Palette));
    Assert.That(raw.Palette, Is.EqualTo(file.Palette));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_NullPalette_UsesDefaultPalette() {
    var raw = new RawImage {
      Width = 128,
      Height = 8,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[128 * 8],
      PaletteCount = 16,
      Palette = null
    };

    var file = MasterSystemTileFile.FromRawImage(raw);

    Assert.That(file.Palette.Length, Is.EqualTo(48));
    Assert.That(file.Palette[0], Is.EqualTo(0));
    Assert.That(file.Palette[47], Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void MasterSystemTileFile_BytesPerTile_Is32() {
    Assert.That(MasterSystemTileFile.BytesPerTile, Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void MasterSystemTileFile_TileSize_Is8() {
    Assert.That(MasterSystemTileFile.TileSize, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void MasterSystemTileFile_TilesPerRow_Is16() {
    Assert.That(MasterSystemTileFile.TilesPerRow, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void MasterSystemTileFile_PlanesPerPixel_Is4() {
    Assert.That(MasterSystemTileFile.PlanesPerPixel, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void MasterSystemTileFile_MaxPaletteEntries_Is16() {
    Assert.That(MasterSystemTileFile.MaxPaletteEntries, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void MasterSystemTileFile_DefaultPalette_NotSharedBetweenInstances() {
    var file1 = new MasterSystemTileFile();
    var file2 = new MasterSystemTileFile();

    Assert.That(file1.Palette, Is.Not.SameAs(file2.Palette));
    Assert.That(file1.Palette, Is.EqualTo(file2.Palette));
  }

  [Test]
  [Category("Unit")]
  public void MasterSystemTileFile_DefaultPalette_GrayscaleGradient() {
    var file = new MasterSystemTileFile();
    for (var i = 1; i < 16; ++i) {
      var prev = file.Palette[(i - 1) * 3];
      var curr = file.Palette[i * 3];
      Assert.That(curr, Is.GreaterThan(prev));
    }
  }

  [Test]
  [Category("Unit")]
  public void MasterSystemTileFile_DefaultPalette_EachEntryIsGray() {
    var file = new MasterSystemTileFile();
    for (var i = 0; i < 16; ++i) {
      var r = file.Palette[i * 3];
      var g = file.Palette[i * 3 + 1];
      var b = file.Palette[i * 3 + 2];
      Assert.That(g, Is.EqualTo(r));
      Assert.That(b, Is.EqualTo(r));
    }
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
