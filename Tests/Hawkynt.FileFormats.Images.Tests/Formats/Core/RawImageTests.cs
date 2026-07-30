using System;
using FileFormat.Core;

namespace FileFormat.Core.Tests;

[TestFixture]
public sealed class RawImageTests {

  [Test]
  public void Construction_RequiredProperties_SetCorrectly() {
    var image = new RawImage {
      Width = 100,
      Height = 200,
      Format = PixelFormat.Bgra32,
      PixelData = new byte[100 * 200 * 4],
    };

    Assert.That(image.Width, Is.EqualTo(100));
    Assert.That(image.Height, Is.EqualTo(200));
    Assert.That(image.Format, Is.EqualTo(PixelFormat.Bgra32));
    Assert.That(image.PixelData, Has.Length.EqualTo(80_000));
  }

  [Test]
  public void OptionalProperties_DefaultNull() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = new byte[4],
    };

    Assert.That(image.Palette, Is.Null);
    Assert.That(image.AlphaTable, Is.Null);
    Assert.That(image.PaletteCount, Is.EqualTo(0));
  }

  [Test]
  public void IndexedImage_WithPalette() {
    var image = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Indexed8,
      PixelData = [0, 1, 0, 1],
      Palette = [255, 0, 0, 0, 255, 0],
      PaletteCount = 2,
    };

    Assert.That(image.Palette, Has.Length.EqualTo(6));
    Assert.That(image.PaletteCount, Is.EqualTo(2));
  }

  [Test]
  public void IndexedImage_WithAlphaTable() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = [0],
      Palette = [255, 0, 0],
      PaletteCount = 1,
      AlphaTable = [128],
    };

    Assert.That(image.AlphaTable, Has.Length.EqualTo(1));
    Assert.That(image.AlphaTable![0], Is.EqualTo(128));
  }

  [TestCase(PixelFormat.Bgra32, 4)]
  [TestCase(PixelFormat.Rgba32, 4)]
  [TestCase(PixelFormat.Argb32, 4)]
  [TestCase(PixelFormat.Rgb24, 3)]
  [TestCase(PixelFormat.Bgr24, 3)]
  [TestCase(PixelFormat.Gray8, 1)]
  [TestCase(PixelFormat.Gray16, 2)]
  [TestCase(PixelFormat.GrayAlpha16, 2)]
  [TestCase(PixelFormat.Indexed8, 1)]
  [TestCase(PixelFormat.Rgba64, 8)]
  [TestCase(PixelFormat.Rgb48, 6)]
  [TestCase(PixelFormat.Rgb565, 2)]
  public void BytesPerPixel_ReturnsCorrectValue(PixelFormat format, int expected) {
    Assert.That(RawImage.BytesPerPixel(format), Is.EqualTo(expected));
  }

  [TestCase(PixelFormat.Indexed4, 0)]
  [TestCase(PixelFormat.Indexed1, 0)]
  public void BytesPerPixel_SubByteFormats_ReturnZero(PixelFormat format, int expected) {
    Assert.That(RawImage.BytesPerPixel(format), Is.EqualTo(expected));
  }

  [TestCase(PixelFormat.Bgra32, 32)]
  [TestCase(PixelFormat.Rgb24, 24)]
  [TestCase(PixelFormat.Gray8, 8)]
  [TestCase(PixelFormat.Indexed4, 4)]
  [TestCase(PixelFormat.Indexed1, 1)]
  [TestCase(PixelFormat.Rgba64, 64)]
  [TestCase(PixelFormat.Rgb48, 48)]
  [TestCase(PixelFormat.Rgb565, 16)]
  public void BitsPerPixel_ReturnsCorrectValue(PixelFormat format, int expected) {
    Assert.That(RawImage.BitsPerPixel(format), Is.EqualTo(expected));
  }

  [Test]
  public void BytesPerPixel_InvalidFormat_Throws() {
    Assert.Throws<ArgumentOutOfRangeException>(() => RawImage.BytesPerPixel((PixelFormat)99));
  }

  [Test]
  public void BitsPerPixel_InvalidFormat_Throws() {
    Assert.Throws<ArgumentOutOfRangeException>(() => RawImage.BitsPerPixel((PixelFormat)99));
  }

  [Test]
  public void IsIndexed_Indexed8_ReturnsTrue() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = [0],
      Palette = [255, 0, 0],
      PaletteCount = 1,
    };

    Assert.That(image.IsIndexed, Is.True);
  }

  [Test]
  public void IsIndexed_Indexed4_ReturnsTrue() {
    var image = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Indexed4,
      PixelData = [0x10],
      Palette = [255, 0, 0, 0, 255, 0],
      PaletteCount = 2,
    };

    Assert.That(image.IsIndexed, Is.True);
  }

  [Test]
  public void IsIndexed_Indexed1_ReturnsTrue() {
    var image = new RawImage {
      Width = 8,
      Height = 1,
      Format = PixelFormat.Indexed1,
      PixelData = [0b10101010],
      Palette = [255, 0, 0, 0, 255, 0],
      PaletteCount = 2,
    };

    Assert.That(image.IsIndexed, Is.True);
  }

  [Test]
  public void IsIndexed_Bgra32_ReturnsFalse() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = [0, 0, 0, 255],
    };

    Assert.That(image.IsIndexed, Is.False);
  }

  [Test]
  public void HasAlpha_Bgra32WithTransparentPixel_ReturnsTrue() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = [0, 0, 0, 128],
    };

    Assert.That(image.HasAlpha, Is.True);
  }

  [Test]
  public void HasAlpha_Bgra32AllOpaque_ReturnsFalse() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = [0, 0, 0, 255],
    };

    Assert.That(image.HasAlpha, Is.True);
  }

  [Test]
  public void HasAlpha_IndexedWithAlphaTable_ReturnsTrue() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = [0],
      Palette = [255, 0, 0],
      PaletteCount = 1,
      AlphaTable = [128],
    };

    Assert.That(image.HasAlpha, Is.True);
  }

  [Test]
  public void HasAlpha_IndexedWithoutAlphaTable_ReturnsFalse() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = [0],
      Palette = [255, 0, 0],
      PaletteCount = 1,
    };

    Assert.That(image.HasAlpha, Is.False);
  }

  [Test]
  public void ToBgra32_AlreadyBgra32_ReturnsSameData() {
    byte[] pixels = [0x10, 0x20, 0x30, 0xFF];
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = pixels,
    };

    var result = image.ToBgra32();

    Assert.That(result, Is.SameAs(pixels));
  }

  [Test]
  public void ToBgra32_Rgb24_ConvertsCorrectly() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [0xAA, 0xBB, 0xCC],
    };

    var result = image.ToBgra32();

    Assert.That(result, Has.Length.EqualTo(4));
    Assert.That(result[0], Is.EqualTo(0xCC));
    Assert.That(result[1], Is.EqualTo(0xBB));
    Assert.That(result[2], Is.EqualTo(0xAA));
    Assert.That(result[3], Is.EqualTo(255));
  }

  [Test]
  public void ToRgba32_Bgra32_SwapsRedBlue() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = [0xCC, 0xBB, 0xAA, 0x80],
    };

    var result = image.ToRgba32();

    Assert.That(result, Has.Length.EqualTo(4));
    Assert.That(result[0], Is.EqualTo(0xAA));
    Assert.That(result[1], Is.EqualTo(0xBB));
    Assert.That(result[2], Is.EqualTo(0xCC));
    Assert.That(result[3], Is.EqualTo(0x80));
  }

  [Test]
  public void ToRgb24_Bgra32_DropsAlpha() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = [0xCC, 0xBB, 0xAA, 0x80],
    };

    var result = image.ToRgb24();

    Assert.That(result, Has.Length.EqualTo(3));
    Assert.That(result[0], Is.EqualTo(0xAA));
    Assert.That(result[1], Is.EqualTo(0xBB));
    Assert.That(result[2], Is.EqualTo(0xCC));
  }
}
