using System;
using FileFormat.Core;
using FileFormat.GameBoyTile;

namespace FileFormat.GameBoyTile.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void DefaultWidth_Is128() {
    var file = new GameBoyTileFile();
    Assert.That(file.Width, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPixelData_IsEmptyArray() {
    var file = new GameBoyTileFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void DefaultPalette_Is12Bytes() {
    var file = new GameBoyTileFile();
    Assert.That(file.Palette, Is.Not.Null);
    Assert.That(file.Palette.Length, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPalette_Color0_IsLightest() {
    var file = new GameBoyTileFile();
    Assert.That(file.Palette[0], Is.EqualTo(224));
    Assert.That(file.Palette[1], Is.EqualTo(248));
    Assert.That(file.Palette[2], Is.EqualTo(208));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPalette_Color1_IsLight() {
    var file = new GameBoyTileFile();
    Assert.That(file.Palette[3], Is.EqualTo(136));
    Assert.That(file.Palette[4], Is.EqualTo(192));
    Assert.That(file.Palette[5], Is.EqualTo(112));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPalette_Color2_IsDark() {
    var file = new GameBoyTileFile();
    Assert.That(file.Palette[6], Is.EqualTo(52));
    Assert.That(file.Palette[7], Is.EqualTo(104));
    Assert.That(file.Palette[8], Is.EqualTo(86));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPalette_Color3_IsDarkest() {
    var file = new GameBoyTileFile();
    Assert.That(file.Palette[9], Is.EqualTo(8));
    Assert.That(file.Palette[10], Is.EqualTo(24));
    Assert.That(file.Palette[11], Is.EqualTo(32));
  }

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_Is2bpp() {
    Assert.That(_GetPrimaryExtension<GameBoyTileFile>(), Is.EqualTo(".2bpp"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsBothFormats() {
    var exts = _GetFileExtensions<GameBoyTileFile>();
    Assert.That(exts, Is.Not.Null);
    Assert.That(exts.Length, Is.EqualTo(2));
    Assert.That(exts, Does.Contain(".2bpp"));
    Assert.That(exts, Does.Contain(".cgb"));
  }

  [Test]
  [Category("Unit")]
  public void InitProperties_RoundTrip() {
    var pixels = new byte[] { 0, 1, 2, 3, 0, 1, 2, 3 };
    var palette = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
    var file = new GameBoyTileFile {
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
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GameBoyTileFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsIndexed8() {
    var file = new GameBoyTileFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8]
    };

    var raw = GameBoyTileFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(raw.PaletteCount, Is.EqualTo(4));
    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.Palette!.Length, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixelData = new byte[128 * 8];
    pixelData[0] = 2;
    var file = new GameBoyTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixelData
    };

    var raw = GameBoyTileFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(pixelData));
    Assert.That(raw.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GameBoyTileFile.FromRawImage(null!));
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

    Assert.Throws<ArgumentException>(() => GameBoyTileFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongWidth_ThrowsArgumentException() {
    var raw = new RawImage {
      Width = 64,
      Height = 8,
      Format = PixelFormat.Indexed8,
      PixelData = new byte[64 * 8]
    };

    Assert.Throws<ArgumentException>(() => GameBoyTileFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPalette_IndependentPerInstance() {
    var file1 = new GameBoyTileFile();
    var file2 = new GameBoyTileFile();
    Assert.That(file1.Palette, Is.Not.SameAs(file2.Palette));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T> => T.FileExtensions;
}
