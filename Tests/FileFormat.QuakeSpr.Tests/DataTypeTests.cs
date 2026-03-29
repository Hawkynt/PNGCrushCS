using System;
using FileFormat.Core;
using FileFormat.QuakeSpr;

namespace FileFormat.QuakeSpr.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void DefaultPalette_Is768Bytes() {
    Assert.That(QuakeSprFile.DefaultPalette.Length, Is.EqualTo(768));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPalette_IsGrayscaleRamp() {
    var pal = QuakeSprFile.DefaultPalette;
    for (var i = 0; i < 256; ++i) {
      Assert.That(pal[i * 3], Is.EqualTo((byte)i));
      Assert.That(pal[i * 3 + 1], Is.EqualTo((byte)i));
      Assert.That(pal[i * 3 + 2], Is.EqualTo((byte)i));
    }
  }

  [Test]
  [Category("Unit")]
  public void QuakeSprFile_DefaultPixelData_IsEmptyArray() {
    var file = new QuakeSprFile();
    Assert.That(file.PixelData, Is.Not.Null);
    Assert.That(file.PixelData, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void QuakeSprFile_DefaultPalette_IsDefaultPalette() {
    var file = new QuakeSprFile();
    Assert.That(file.Palette, Is.SameAs(QuakeSprFile.DefaultPalette));
  }

  [Test]
  [Category("Unit")]
  public void QuakeSprFile_DefaultNumFrames_Is1() {
    var file = new QuakeSprFile();
    Assert.That(file.NumFrames, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void QuakeSprFile_DefaultWidth_Is0() {
    var file = new QuakeSprFile();
    Assert.That(file.Width, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void QuakeSprFile_DefaultHeight_Is0() {
    var file = new QuakeSprFile();
    Assert.That(file.Height, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void QuakeSprFile_DefaultSpriteType_Is0() {
    var file = new QuakeSprFile();
    Assert.That(file.SpriteType, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void QuakeSprFile_DefaultSyncType_Is0() {
    var file = new QuakeSprFile();
    Assert.That(file.SyncType, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void QuakeSprFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 0x01, 0x02, 0x03, 0x04 };
    var palette = new byte[768];
    var file = new QuakeSprFile {
      Width = 2,
      Height = 2,
      SpriteType = 4,
      NumFrames = 3,
      BoundingRadius = 10.0f,
      BeamLength = 2.5f,
      SyncType = 1,
      PixelData = pixels,
      Palette = palette
    };

    Assert.That(file.Width, Is.EqualTo(2));
    Assert.That(file.Height, Is.EqualTo(2));
    Assert.That(file.SpriteType, Is.EqualTo(4));
    Assert.That(file.NumFrames, Is.EqualTo(3));
    Assert.That(file.BoundingRadius, Is.EqualTo(10.0f));
    Assert.That(file.BeamLength, Is.EqualTo(2.5f));
    Assert.That(file.SyncType, Is.EqualTo(1));
    Assert.That(file.PixelData, Is.SameAs(pixels));
    Assert.That(file.Palette, Is.SameAs(palette));
  }

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsSpr() {
    var ext = _GetPrimaryExtension<QuakeSprFile>();
    Assert.That(ext, Is.EqualTo(".spr"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsSpr() {
    var exts = _GetFileExtensions<QuakeSprFile>();
    Assert.That(exts, Contains.Item(".spr"));
    Assert.That(exts, Has.Length.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => QuakeSprFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => QuakeSprFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WrongFormat_Throws() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[12]
    };
    Assert.Throws<ArgumentException>(() => QuakeSprFile.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ReturnsIndexed8() {
    var file = new QuakeSprFile {
      Width = 2,
      Height = 2,
      PixelData = [0, 1, 2, 3]
    };

    var raw = QuakeSprFile.ToRawImage(file);

    Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(raw.Width, Is.EqualTo(2));
    Assert.That(raw.Height, Is.EqualTo(2));
    Assert.That(raw.PixelData, Is.EqualTo(file.PixelData));
    Assert.That(raw.Palette, Is.Not.Null);
    Assert.That(raw.PaletteCount, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ClonesPixelData() {
    var pixels = new byte[] { 0, 1, 2, 3 };
    var file = new QuakeSprFile {
      Width = 2,
      Height = 2,
      PixelData = pixels
    };

    var raw = QuakeSprFile.ToRawImage(file);

    Assert.That(raw.PixelData, Is.Not.SameAs(pixels));
    Assert.That(raw.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ClonesPaletteAndPixels() {
    var pixels = new byte[] { 0, 1, 2, 3 };
    var palette = new byte[768];
    palette[0] = 0xFF;
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = 256
    };

    var file = QuakeSprFile.FromRawImage(raw);

    Assert.That(file.PixelData, Is.Not.SameAs(pixels));
    Assert.That(file.PixelData, Is.EqualTo(pixels));
    Assert.That(file.Palette, Is.Not.SameAs(palette));
    Assert.That(file.Palette[0], Is.EqualTo(0xFF));
  }

  private static string _GetPrimaryExtension<T>() where T : IImageFileFormat<T>
    => T.PrimaryExtension;

  private static string[] _GetFileExtensions<T>() where T : IImageFileFormat<T>
    => T.FileExtensions;
}
