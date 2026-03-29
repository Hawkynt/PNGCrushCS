using System;
using FileFormat.Bmp;

namespace FileFormat.Bmp.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24() {
    var pixelData = new byte[4 * 3 * 3]; // 4x3, BGR triplets
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new BmpFile {
      Width = 4,
      Height = 3,
      BitsPerPixel = 24,
      PixelData = pixelData,
      ColorMode = BmpColorMode.Rgb24,
      RowOrder = BmpRowOrder.TopDown
    };

    var bytes = BmpWriter.ToBytes(original);
    var restored = BmpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(24));
    Assert.That(restored.ColorMode, Is.EqualTo(BmpColorMode.Rgb24));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Palette8() {
    var palette = new byte[4 * 3]; // 4 colors
    palette[0] = 255; palette[1] = 0; palette[2] = 0;     // red
    palette[3] = 0;   palette[4] = 255; palette[5] = 0;   // green
    palette[6] = 0;   palette[7] = 0;   palette[8] = 255; // blue
    palette[9] = 128; palette[10] = 128; palette[11] = 128; // gray

    var pixelData = new byte[] { 0, 1, 2, 3, 3, 2, 1, 0 }; // 4x2

    var original = new BmpFile {
      Width = 4,
      Height = 2,
      BitsPerPixel = 8,
      PixelData = pixelData,
      Palette = palette,
      PaletteColorCount = 4,
      ColorMode = BmpColorMode.Palette8,
      RowOrder = BmpRowOrder.TopDown
    };

    var bytes = BmpWriter.ToBytes(original);
    var restored = BmpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(8));
    Assert.That(restored.Palette, Is.Not.Null);
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Palette4() {
    var palette = new byte[4 * 3];
    palette[0] = 0;   palette[1] = 0;   palette[2] = 0;     // black
    palette[3] = 255; palette[4] = 0;   palette[5] = 0;     // red
    palette[6] = 0;   palette[7] = 255; palette[8] = 0;     // green
    palette[9] = 0;   palette[10] = 0;  palette[11] = 255;  // blue

    // 4-bit packed: 2 pixels per byte, 4 pixels wide x 2 rows = 4 bytes
    var pixelData = new byte[] { 0x01, 0x23, 0x30, 0x12 };

    var original = new BmpFile {
      Width = 4,
      Height = 2,
      BitsPerPixel = 4,
      PixelData = pixelData,
      Palette = palette,
      PaletteColorCount = 4,
      ColorMode = BmpColorMode.Palette4,
      RowOrder = BmpRowOrder.TopDown
    };

    var bytes = BmpWriter.ToBytes(original);
    var restored = BmpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Palette1() {
    var palette = new byte[2 * 3];
    palette[0] = 0;   palette[1] = 0;   palette[2] = 0;   // black
    palette[3] = 255; palette[4] = 255; palette[5] = 255; // white

    // 1-bit packed: 8 pixels per byte, 8 pixels wide x 1 row = 1 byte
    var pixelData = new byte[] { 0b10101010 };

    var original = new BmpFile {
      Width = 8,
      Height = 1,
      BitsPerPixel = 1,
      PixelData = pixelData,
      Palette = palette,
      PaletteColorCount = 2,
      ColorMode = BmpColorMode.Palette1,
      RowOrder = BmpRowOrder.TopDown
    };

    var bytes = BmpWriter.ToBytes(original);
    var restored = BmpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale8() {
    // Grayscale palette: R=G=B for each entry
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)i;
      palette[i * 3 + 2] = (byte)i;
    }

    var pixelData = new byte[] { 0, 64, 128, 255 }; // 2x2

    var original = new BmpFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 8,
      PixelData = pixelData,
      Palette = palette,
      PaletteColorCount = 256,
      ColorMode = BmpColorMode.Grayscale8,
      RowOrder = BmpRowOrder.TopDown
    };

    var bytes = BmpWriter.ToBytes(original);
    var restored = BmpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.ColorMode, Is.EqualTo(BmpColorMode.Grayscale8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rle8() {
    var palette = new byte[4 * 3];
    palette[0] = 255; palette[1] = 0;   palette[2] = 0;
    palette[3] = 0;   palette[4] = 255; palette[5] = 0;
    palette[6] = 0;   palette[7] = 0;   palette[8] = 255;
    palette[9] = 128; palette[10] = 128; palette[11] = 128;

    // 4 pixels wide, 2 rows: indices with runs for RLE
    var pixelData = new byte[] { 0, 0, 1, 1, 2, 2, 3, 3 };

    var original = new BmpFile {
      Width = 4,
      Height = 2,
      BitsPerPixel = 8,
      PixelData = pixelData,
      Palette = palette,
      PaletteColorCount = 4,
      ColorMode = BmpColorMode.Palette8,
      Compression = BmpCompression.Rle8,
      RowOrder = BmpRowOrder.TopDown
    };

    var bytes = BmpWriter.ToBytes(original);
    var restored = BmpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb16_565() {
    // 2 bytes per pixel, 2x2 image = 8 bytes
    // R=31 (5 bits), G=63 (6 bits), B=31 (5 bits) => 0xFFFF
    var pixelData = new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0xE0, 0x07, 0x1F, 0xF8 };

    var original = new BmpFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 16,
      PixelData = pixelData,
      ColorMode = BmpColorMode.Rgb16_565,
      RowOrder = BmpRowOrder.TopDown
    };

    var bytes = BmpWriter.ToBytes(original);
    var restored = BmpReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(16));
    Assert.That(restored.ColorMode, Is.EqualTo(BmpColorMode.Rgb16_565));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
