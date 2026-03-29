using System;
using FileFormat.SnesTile;
using FileFormat.Core;

namespace FileFormat.SnesTile.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  public void FixedWidth_Is128()
    => Assert.That(SnesTileFile.FixedWidth, Is.EqualTo(128));

  [Test]
  public void BytesPerTile_Is32()
    => Assert.That(SnesTileFile.BytesPerTile, Is.EqualTo(32));

  [Test]
  public void TileSize_Is8()
    => Assert.That(SnesTileFile.TileSize, Is.EqualTo(8));

  [Test]
  public void TilesPerRow_Is16()
    => Assert.That(SnesTileFile.TilesPerRow, Is.EqualTo(16));

  [Test]
  public void DefaultPalette_Has16Entries() {
    var file = new SnesTileFile { Height = 8, PixelData = new byte[128 * 8] };
    Assert.That(file.Palette.Length, Is.EqualTo(48));
  }

  [Test]
  public void PrimaryExtension_IsSfc() {
    var ext = GetPrimaryExtension();
    Assert.That(ext, Is.EqualTo(".sfc"));
  }

  [Test]
  public void FileExtensions_ContainsSfc() {
    var exts = GetExtensions();
    Assert.That(exts, Does.Contain(".sfc"));
  }

  [Test]
  public void FileExtensions_ContainsSnes() {
    var exts = GetExtensions();
    Assert.That(exts, Does.Contain(".snes"));
  }

  private static string GetPrimaryExtension() => ".sfc";
  private static string[] GetExtensions() => [".sfc", ".snes"];

  [Test]
  public void ToRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => SnesTileFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => SnesTileFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage { Width = 128, Height = 8, Format = PixelFormat.Rgb24, PixelData = new byte[128 * 8 * 3] };
    Assert.Throws<ArgumentException>(() => SnesTileFile.FromRawImage(raw));
  }

  [Test]
  public void FromRawImage_WrongWidth_Throws() {
    var raw = new RawImage { Width = 64, Height = 8, Format = PixelFormat.Indexed8, PixelData = new byte[64 * 8], Palette = new byte[48], PaletteCount = 16 };
    Assert.Throws<ArgumentException>(() => SnesTileFile.FromRawImage(raw));
  }

  [Test]
  public void FromRawImage_TooManyPaletteEntries_Throws() {
    var raw = new RawImage { Width = 128, Height = 8, Format = PixelFormat.Indexed8, PixelData = new byte[128 * 8], Palette = new byte[256 * 3], PaletteCount = 256 };
    Assert.Throws<ArgumentException>(() => SnesTileFile.FromRawImage(raw));
  }
}
