using System;
using System.Linq;
using FileFormat.Png;

namespace FileFormat.Png.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb8() {
    var pixelData = new byte[4][];
    for (var y = 0; y < 4; ++y) {
      pixelData[y] = new byte[12];
      for (var x = 0; x < 12; ++x)
        pixelData[y][x] = (byte)((y * 12 + x) * 7);
    }

    var original = new PngFile {
      Width = 4,
      Height = 4,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      PixelData = pixelData
    };

    var bytes = PngWriter.ToBytes(original);
    var restored = PngReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.BitDepth, Is.EqualTo(8));
    Assert.That(restored.ColorType, Is.EqualTo(PngColorType.RGB));
    Assert.That(restored.PixelData, Is.Not.Null);
    Assert.That(restored.PixelData!.Length, Is.EqualTo(4));
    for (var y = 0; y < 4; ++y)
      Assert.That(restored.PixelData[y], Is.EqualTo(pixelData[y]), $"Scanline {y} mismatch");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba8() {
    var pixelData = new byte[4][];
    for (var y = 0; y < 4; ++y) {
      pixelData[y] = new byte[16];
      for (var x = 0; x < 16; ++x)
        pixelData[y][x] = (byte)((y * 16 + x) * 3);
    }

    var original = new PngFile {
      Width = 4,
      Height = 4,
      BitDepth = 8,
      ColorType = PngColorType.RGBA,
      PixelData = pixelData
    };

    var bytes = PngWriter.ToBytes(original);
    var restored = PngReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.BitDepth, Is.EqualTo(8));
    Assert.That(restored.ColorType, Is.EqualTo(PngColorType.RGBA));
    Assert.That(restored.PixelData, Is.Not.Null);
    for (var y = 0; y < 4; ++y)
      Assert.That(restored.PixelData![y], Is.EqualTo(pixelData[y]), $"Scanline {y} mismatch");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale8() {
    var pixelData = new byte[4][];
    for (var y = 0; y < 4; ++y) {
      pixelData[y] = new byte[4];
      for (var x = 0; x < 4; ++x)
        pixelData[y][x] = (byte)(y * 64 + x * 16);
    }

    var original = new PngFile {
      Width = 4,
      Height = 4,
      BitDepth = 8,
      ColorType = PngColorType.Grayscale,
      PixelData = pixelData
    };

    var bytes = PngWriter.ToBytes(original);
    var restored = PngReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.BitDepth, Is.EqualTo(8));
    Assert.That(restored.ColorType, Is.EqualTo(PngColorType.Grayscale));
    Assert.That(restored.PixelData, Is.Not.Null);
    for (var y = 0; y < 4; ++y)
      Assert.That(restored.PixelData![y], Is.EqualTo(pixelData[y]), $"Scanline {y} mismatch");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_GrayscaleAlpha8() {
    var pixelData = new byte[4][];
    for (var y = 0; y < 4; ++y) {
      pixelData[y] = new byte[8];
      for (var x = 0; x < 4; ++x) {
        pixelData[y][x * 2] = (byte)(y * 50 + x * 10);
        pixelData[y][x * 2 + 1] = (byte)(200 - y * 40);
      }
    }

    var original = new PngFile {
      Width = 4,
      Height = 4,
      BitDepth = 8,
      ColorType = PngColorType.GrayscaleAlpha,
      PixelData = pixelData
    };

    var bytes = PngWriter.ToBytes(original);
    var restored = PngReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.BitDepth, Is.EqualTo(8));
    Assert.That(restored.ColorType, Is.EqualTo(PngColorType.GrayscaleAlpha));
    Assert.That(restored.PixelData, Is.Not.Null);
    for (var y = 0; y < 4; ++y)
      Assert.That(restored.PixelData![y], Is.EqualTo(pixelData[y]), $"Scanline {y} mismatch");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Palette() {
    var palette = new byte[12];
    palette[0] = 255; palette[1] = 0; palette[2] = 0;
    palette[3] = 0; palette[4] = 255; palette[5] = 0;
    palette[6] = 0; palette[7] = 0; palette[8] = 255;
    palette[9] = 255; palette[10] = 255; palette[11] = 0;

    var pixelData = new byte[4][];
    for (var y = 0; y < 4; ++y) {
      pixelData[y] = new byte[4];
      for (var x = 0; x < 4; ++x)
        pixelData[y][x] = (byte)((y + x) % 4);
    }

    var original = new PngFile {
      Width = 4,
      Height = 4,
      BitDepth = 8,
      ColorType = PngColorType.Palette,
      Palette = palette,
      PaletteCount = 4,
      PixelData = pixelData
    };

    var bytes = PngWriter.ToBytes(original);
    var restored = PngReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.BitDepth, Is.EqualTo(8));
    Assert.That(restored.ColorType, Is.EqualTo(PngColorType.Palette));
    Assert.That(restored.PaletteCount, Is.EqualTo(4));
    Assert.That(restored.Palette, Is.Not.Null);
    Assert.That(restored.Palette!, Is.EqualTo(palette));
    Assert.That(restored.PixelData, Is.Not.Null);
    for (var y = 0; y < 4; ++y)
      Assert.That(restored.PixelData![y], Is.EqualTo(pixelData[y]), $"Scanline {y} mismatch");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TrnsPreserved() {
    var pixelData = new byte[2][];
    for (var y = 0; y < 2; ++y) {
      pixelData[y] = new byte[6];
      for (var x = 0; x < 6; ++x)
        pixelData[y][x] = (byte)(y * 30 + x * 10);
    }

    var tRNS = new byte[] { 0, 100, 0, 150, 0, 200 };

    var original = new PngFile {
      Width = 2,
      Height = 2,
      BitDepth = 8,
      ColorType = PngColorType.RGB,
      Transparency = tRNS,
      PixelData = pixelData
    };

    var bytes = PngWriter.ToBytes(original);
    var restored = PngReader.FromBytes(bytes);

    Assert.That(restored.Transparency, Is.Not.Null);
    Assert.That(restored.Transparency!, Is.EqualTo(tRNS));
  }
}
