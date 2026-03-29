using System;
using FileFormat.Tga;

namespace FileFormat.Tga.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7);

    var original = new TgaFile {
      Width = 4,
      Height = 4,
      BitsPerPixel = 24,
      PixelData = pixelData,
      ColorMode = TgaColorMode.Rgb24,
      Compression = TgaCompression.None,
      Origin = TgaOrigin.TopLeft
    };

    var bytes = TgaWriter.ToBytes(original);
    var restored = TgaReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(24));
    Assert.That(restored.ColorMode, Is.EqualTo(TgaColorMode.Rgb24));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba32() {
    var pixelData = new byte[3 * 3 * 4];
    for (var i = 0; i < 9; ++i) {
      pixelData[i * 4] = (byte)(i * 20);
      pixelData[i * 4 + 1] = (byte)(i * 25);
      pixelData[i * 4 + 2] = (byte)(i * 15);
      pixelData[i * 4 + 3] = (byte)(128 + i);
    }

    var original = new TgaFile {
      Width = 3,
      Height = 3,
      BitsPerPixel = 32,
      PixelData = pixelData,
      ColorMode = TgaColorMode.Rgba32,
      Compression = TgaCompression.None,
      Origin = TgaOrigin.TopLeft
    };

    var bytes = TgaWriter.ToBytes(original);
    var restored = TgaReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(3));
    Assert.That(restored.Height, Is.EqualTo(3));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(32));
    Assert.That(restored.ColorMode, Is.EqualTo(TgaColorMode.Rgba32));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale8() {
    var pixelData = new byte[8 * 8];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 4);

    var original = new TgaFile {
      Width = 8,
      Height = 8,
      BitsPerPixel = 8,
      PixelData = pixelData,
      ColorMode = TgaColorMode.Grayscale8,
      Compression = TgaCompression.None,
      Origin = TgaOrigin.TopLeft
    };

    var bytes = TgaWriter.ToBytes(original);
    var restored = TgaReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(8));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(8));
    Assert.That(restored.ColorMode, Is.EqualTo(TgaColorMode.Grayscale8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Indexed8() {
    var palette = new byte[4 * 3];
    palette[0] = 255; palette[1] = 0; palette[2] = 0;       // Red
    palette[3] = 0; palette[4] = 255; palette[5] = 0;       // Green
    palette[6] = 0; palette[7] = 0; palette[8] = 255;       // Blue
    palette[9] = 255; palette[10] = 255; palette[11] = 0;   // Yellow

    var pixelData = new byte[] { 0, 1, 2, 3, 3, 2, 1, 0, 0, 1, 2, 3, 3, 2, 1, 0 };

    var original = new TgaFile {
      Width = 4,
      Height = 4,
      BitsPerPixel = 8,
      PixelData = pixelData,
      Palette = palette,
      PaletteColorCount = 4,
      ColorMode = TgaColorMode.Indexed8,
      Compression = TgaCompression.None,
      Origin = TgaOrigin.TopLeft
    };

    var bytes = TgaWriter.ToBytes(original);
    var restored = TgaReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.ColorMode, Is.EqualTo(TgaColorMode.Indexed8));
    Assert.That(restored.PaletteColorCount, Is.EqualTo(4));
    Assert.That(restored.Palette, Is.Not.Null);
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rle_Rgb24() {
    var pixelData = new byte[8 * 4 * 3];
    for (var i = 0; i < 8 * 4; ++i) {
      pixelData[i * 3] = (byte)(i % 4 == 0 ? 0xAA : i * 5);
      pixelData[i * 3 + 1] = (byte)(i % 4 == 0 ? 0xBB : i * 3);
      pixelData[i * 3 + 2] = (byte)(i % 4 == 0 ? 0xCC : i * 7);
    }

    var original = new TgaFile {
      Width = 8,
      Height = 4,
      BitsPerPixel = 24,
      PixelData = pixelData,
      ColorMode = TgaColorMode.Rgb24,
      Compression = TgaCompression.Rle,
      Origin = TgaOrigin.TopLeft
    };

    var bytes = TgaWriter.ToBytes(original);
    var restored = TgaReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.ColorMode, Is.EqualTo(TgaColorMode.Rgb24));
    Assert.That(restored.Compression, Is.EqualTo(TgaCompression.Rle));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BottomLeft_Origin() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3);

    var original = new TgaFile {
      Width = 4,
      Height = 4,
      BitsPerPixel = 24,
      PixelData = pixelData,
      ColorMode = TgaColorMode.Rgb24,
      Compression = TgaCompression.None,
      Origin = TgaOrigin.BottomLeft
    };

    var bytes = TgaWriter.ToBytes(original);
    var restored = TgaReader.FromBytes(bytes);

    Assert.That(restored.Origin, Is.EqualTo(TgaOrigin.BottomLeft));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
