using System;
using FileFormat.SunRaster;

namespace FileFormat.SunRaster.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_Uncompressed() {
    var bytesPerRow = 4 * 3;
    var paddedBytesPerRow = (bytesPerRow + 1) & ~1; // 12, already even
    var pixelData = new byte[paddedBytesPerRow * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new SunRasterFile {
      Width = 4,
      Height = 3,
      Depth = 24,
      PixelData = pixelData,
      ColorMode = SunRasterColorMode.Rgb24,
      Compression = SunRasterCompression.None
    };

    var bytes = SunRasterWriter.ToBytes(original);
    var restored = SunRasterReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Depth, Is.EqualTo(24));
    Assert.That(restored.ColorMode, Is.EqualTo(SunRasterColorMode.Rgb24));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Palette8() {
    var palette = new byte[4 * 3];
    palette[0] = 255; palette[1] = 0; palette[2] = 0;     // red
    palette[3] = 0;   palette[4] = 255; palette[5] = 0;   // green
    palette[6] = 0;   palette[7] = 0;   palette[8] = 255; // blue
    palette[9] = 128; palette[10] = 128; palette[11] = 128; // gray

    // 4 pixels wide (4 bytes per row, already 2-byte aligned), 2 rows
    var pixelData = new byte[] { 0, 1, 2, 3, 3, 2, 1, 0 };

    var original = new SunRasterFile {
      Width = 4,
      Height = 2,
      Depth = 8,
      PixelData = pixelData,
      Palette = palette,
      PaletteColorCount = 4,
      ColorMode = SunRasterColorMode.Palette8,
      Compression = SunRasterCompression.None
    };

    var bytes = SunRasterWriter.ToBytes(original);
    var restored = SunRasterReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.Depth, Is.EqualTo(8));
    Assert.That(restored.Palette, Is.Not.Null);
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Monochrome() {
    // 1-bit: 8 pixels per byte, 16 pixels wide = 2 bytes per row (already 2-byte aligned)
    var pixelData = new byte[] { 0b10101010, 0b01010101, 0b11001100, 0b00110011 };

    var original = new SunRasterFile {
      Width = 16,
      Height = 2,
      Depth = 1,
      PixelData = pixelData,
      ColorMode = SunRasterColorMode.Monochrome,
      Compression = SunRasterCompression.None
    };

    var bytes = SunRasterWriter.ToBytes(original);
    var restored = SunRasterReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(16));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.Depth, Is.EqualTo(1));
    Assert.That(restored.ColorMode, Is.EqualTo(SunRasterColorMode.Monochrome));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_Rle() {
    var bytesPerRow = 4 * 3;
    var paddedBytesPerRow = (bytesPerRow + 1) & ~1;
    var pixelData = new byte[paddedBytesPerRow * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var original = new SunRasterFile {
      Width = 4,
      Height = 2,
      Depth = 24,
      PixelData = pixelData,
      ColorMode = SunRasterColorMode.Rgb24,
      Compression = SunRasterCompression.Rle
    };

    var bytes = SunRasterWriter.ToBytes(original);
    var restored = SunRasterReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Depth, Is.EqualTo(24));
    Assert.That(restored.Compression, Is.EqualTo(SunRasterCompression.Rle));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb32() {
    var pixelData = new byte[2 * 2 * 4]; // 2x2, 32bpp
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new SunRasterFile {
      Width = 2,
      Height = 2,
      Depth = 32,
      PixelData = pixelData,
      ColorMode = SunRasterColorMode.Rgb32,
      Compression = SunRasterCompression.None
    };

    var bytes = SunRasterWriter.ToBytes(original);
    var restored = SunRasterReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.Depth, Is.EqualTo(32));
    Assert.That(restored.ColorMode, Is.EqualTo(SunRasterColorMode.Rgb32));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_OddWidthRgb24_PaddingHandled() {
    // 3 pixels wide: 3*3=9 bytes per row, padded to 10 (2-byte boundary)
    var bytesPerRow = 3 * 3;
    var paddedBytesPerRow = (bytesPerRow + 1) & ~1; // 10
    var pixelData = new byte[paddedBytesPerRow * 2]; // 2 rows
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new SunRasterFile {
      Width = 3,
      Height = 2,
      Depth = 24,
      PixelData = pixelData,
      ColorMode = SunRasterColorMode.Rgb24,
      Compression = SunRasterCompression.None
    };

    var bytes = SunRasterWriter.ToBytes(original);
    var restored = SunRasterReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(3));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Palette8_Rle() {
    var palette = new byte[3 * 3];
    palette[0] = 255; palette[1] = 0; palette[2] = 0;
    palette[3] = 0; palette[4] = 255; palette[5] = 0;
    palette[6] = 0; palette[7] = 0; palette[8] = 255;

    // Repeating data good for RLE
    var pixelData = new byte[] { 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2 };

    var original = new SunRasterFile {
      Width = 4,
      Height = 3,
      Depth = 8,
      PixelData = pixelData,
      Palette = palette,
      PaletteColorCount = 3,
      ColorMode = SunRasterColorMode.Palette8,
      Compression = SunRasterCompression.Rle
    };

    var bytes = SunRasterWriter.ToBytes(original);
    var restored = SunRasterReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }
}
