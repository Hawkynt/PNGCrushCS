using System;
using FileFormat.Pcx;

namespace FileFormat.Pcx.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_SeparatePlanes() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7);

    var original = new PcxFile {
      Width = 4,
      Height = 4,
      BitsPerPixel = 8,
      PixelData = pixelData,
      ColorMode = PcxColorMode.Rgb24,
      PlaneConfig = PcxPlaneConfig.SeparatePlanes
    };

    var bytes = PcxWriter.ToBytes(original);
    var restored = PcxReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ColorMode, Is.EqualTo(PcxColorMode.Rgb24));
    Assert.That(restored.PlaneConfig, Is.EqualTo(PcxPlaneConfig.SeparatePlanes));
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

    var original = new PcxFile {
      Width = 4,
      Height = 4,
      BitsPerPixel = 8,
      PixelData = pixelData,
      Palette = palette,
      PaletteColorCount = 4,
      ColorMode = PcxColorMode.Indexed8,
      PlaneConfig = PcxPlaneConfig.SinglePlane
    };

    var bytes = PcxWriter.ToBytes(original);
    var restored = PcxReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.ColorMode, Is.EqualTo(PcxColorMode.Indexed8));
    Assert.That(restored.PaletteColorCount, Is.EqualTo(256));
    Assert.That(restored.Palette, Is.Not.Null);
    Assert.That(restored.Palette![0], Is.EqualTo(255), "Red channel of first palette entry");
    Assert.That(restored.Palette![1], Is.EqualTo(0));
    Assert.That(restored.Palette![2], Is.EqualTo(0));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Indexed4() {
    var palette = new byte[4 * 3];
    palette[0] = 0; palette[1] = 0; palette[2] = 0;         // Black
    palette[3] = 255; palette[4] = 0; palette[5] = 0;       // Red
    palette[6] = 0; palette[7] = 255; palette[8] = 0;       // Green
    palette[9] = 0; palette[10] = 0; palette[11] = 255;     // Blue

    // 4 pixels wide, packed as nibbles: 2 bytes per row
    // Row 0: pixels 0,1,2,3 -> 0x01, 0x23
    // Row 1: pixels 3,2,1,0 -> 0x32, 0x10
    var pixelData = new byte[] { 0x01, 0x23, 0x32, 0x10 };

    var original = new PcxFile {
      Width = 4,
      Height = 2,
      BitsPerPixel = 4,
      PixelData = pixelData,
      Palette = palette,
      PaletteColorCount = 4,
      ColorMode = PcxColorMode.Indexed4,
      PlaneConfig = PcxPlaneConfig.SinglePlane
    };

    var bytes = PcxWriter.ToBytes(original);
    var restored = PcxReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.ColorMode, Is.EqualTo(PcxColorMode.Indexed4));
    Assert.That(restored.PaletteColorCount, Is.EqualTo(16));
    Assert.That(restored.Palette, Is.Not.Null);
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Monochrome() {
    var palette = new byte[2 * 3];
    palette[0] = 0; palette[1] = 0; palette[2] = 0;         // Black
    palette[3] = 255; palette[4] = 255; palette[5] = 255;   // White

    // 8 pixels wide, packed as bits: 1 byte per row
    // Row 0: 0b10101010 = 0xAA
    // Row 1: 0b01010101 = 0x55
    var pixelData = new byte[] { 0xAA, 0x55 };

    var original = new PcxFile {
      Width = 8,
      Height = 2,
      BitsPerPixel = 1,
      PixelData = pixelData,
      Palette = palette,
      PaletteColorCount = 2,
      ColorMode = PcxColorMode.Monochrome,
      PlaneConfig = PcxPlaneConfig.SinglePlane
    };

    var bytes = PcxWriter.ToBytes(original);
    var restored = PcxReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.ColorMode, Is.EqualTo(PcxColorMode.Monochrome));
    Assert.That(restored.PaletteColorCount, Is.EqualTo(2));
    Assert.That(restored.Palette, Is.Not.Null);
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage_Rgb24() {
    var pixelData = new byte[16 * 16 * 3];
    for (var i = 0; i < 16 * 16; ++i) {
      pixelData[i * 3] = (byte)(i % 256);
      pixelData[i * 3 + 1] = (byte)((i * 3) % 256);
      pixelData[i * 3 + 2] = (byte)((i * 7) % 256);
    }

    var original = new PcxFile {
      Width = 16,
      Height = 16,
      BitsPerPixel = 8,
      PixelData = pixelData,
      ColorMode = PcxColorMode.Rgb24,
      PlaneConfig = PcxPlaneConfig.SeparatePlanes
    };

    var bytes = PcxWriter.ToBytes(original);
    var restored = PcxReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(16));
    Assert.That(restored.Height, Is.EqualTo(16));
    Assert.That(restored.ColorMode, Is.EqualTo(PcxColorMode.Rgb24));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
