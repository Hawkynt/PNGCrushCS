using System;
using FileFormat.SegaGenTile;
using FileFormat.Core;

namespace FileFormat.SegaGenTile.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  public void FixedWidth_Is128()
    => Assert.That(SegaGenTileFile.FixedWidth, Is.EqualTo(128));

  [Test]
  public void BytesPerTile_Is32()
    => Assert.That(SegaGenTileFile.BytesPerTile, Is.EqualTo(32));

  [Test]
  public void TileSize_Is8()
    => Assert.That(SegaGenTileFile.TileSize, Is.EqualTo(8));

  [Test]
  public void TilesPerRow_Is16()
    => Assert.That(SegaGenTileFile.TilesPerRow, Is.EqualTo(16));

  [Test]
  public void ToRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => SegaGenTileFile.ToRawImage(null!));

  [Test]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => SegaGenTileFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage { Width = 128, Height = 8, Format = PixelFormat.Rgb24, PixelData = new byte[128 * 8 * 3] };
    Assert.Throws<ArgumentException>(() => SegaGenTileFile.FromRawImage(raw));
  }

  [Test]
  public void FromRawImage_WrongWidth_Throws() {
    var raw = new RawImage { Width = 64, Height = 8, Format = PixelFormat.Indexed8, PixelData = new byte[64 * 8], Palette = new byte[48], PaletteCount = 16 };
    Assert.Throws<ArgumentException>(() => SegaGenTileFile.FromRawImage(raw));
  }

  [Test]
  public void FromRawImage_TooManyPaletteEntries_Throws() {
    var raw = new RawImage { Width = 128, Height = 8, Format = PixelFormat.Indexed8, PixelData = new byte[128 * 8], Palette = new byte[256 * 3], PaletteCount = 256 };
    Assert.Throws<ArgumentException>(() => SegaGenTileFile.FromRawImage(raw));
  }

  [Test]
  public void DefaultPalette_Is16EntryGrayscale() {
    var file = new SegaGenTileFile { Height = 8, PixelData = new byte[128 * 8] };
    Assert.Multiple(() => {
      Assert.That(file.Palette.Length, Is.EqualTo(48));
      Assert.That(file.Palette[0], Is.EqualTo(0));
      Assert.That(file.Palette[1], Is.EqualTo(0));
      Assert.That(file.Palette[2], Is.EqualTo(0));
      Assert.That(file.Palette[45], Is.EqualTo(255));
      Assert.That(file.Palette[46], Is.EqualTo(255));
      Assert.That(file.Palette[47], Is.EqualTo(255));
    });
  }

  [Test]
  public void DefaultWidth_Is128() {
    var file = new SegaGenTileFile { Height = 8, PixelData = new byte[128 * 8] };
    Assert.That(file.Width, Is.EqualTo(128));
  }

  [Test]
  public void ToRawImage_ClonesPixelData() {
    var data = new byte[32 * 16];
    data[0] = 0x12;
    var file = SegaGenTileReader.FromBytes(data);
    var raw = SegaGenTileFile.ToRawImage(file);
    raw.PixelData[0] = 0xFF;
    Assert.That(file.PixelData[0], Is.Not.EqualTo(0xFF));
  }

  [Test]
  public void FromRawImage_ClonesPalette() {
    var palette = new byte[48];
    palette[0] = 42;
    var raw = new RawImage { Width = 128, Height = 8, Format = PixelFormat.Indexed8, PixelData = new byte[128 * 8], Palette = palette, PaletteCount = 16 };
    var file = SegaGenTileFile.FromRawImage(raw);
    palette[0] = 99;
    Assert.That(file.Palette[0], Is.EqualTo(42));
  }
}
