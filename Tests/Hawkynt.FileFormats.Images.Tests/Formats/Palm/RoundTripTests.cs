using System;
using System.IO;
using FileFormat.Palm;

namespace FileFormat.Palm.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_1bpp() {
    var original = new PalmFile {
      Width = 16,
      Height = 2,
      BitsPerPixel = 1,
      Compression = PalmCompression.None,
      PixelData = new byte[] { 0xFF, 0x00, 0xAA, 0x55 }
    };

    var bytes = PalmWriter.ToBytes(original);
    var restored = PalmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_4bpp() {
    // 4x2, 4bpp: bytesPerRow = ceil(4*4/8) = 2, padded to 2
    var original = new PalmFile {
      Width = 4,
      Height = 2,
      BitsPerPixel = 4,
      Compression = PalmCompression.None,
      PixelData = new byte[] { 0x12, 0x34, 0x56, 0x78 }
    };

    var bytes = PalmWriter.ToBytes(original);
    var restored = PalmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_8bpp() {
    // 4x2, 8bpp: bytesPerRow = 4
    var pixelData = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 };
    var original = new PalmFile {
      Width = 4,
      Height = 2,
      BitsPerPixel = 8,
      Compression = PalmCompression.None,
      PixelData = pixelData
    };

    var bytes = PalmWriter.ToBytes(original);
    var restored = PalmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_16bpp() {
    // 4x2, 16bpp: bytesPerRow = 4*2 = 8
    var pixelData = new byte[16];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 15 % 256);

    var original = new PalmFile {
      Width = 4,
      Height = 2,
      BitsPerPixel = 16,
      Compression = PalmCompression.None,
      PixelData = pixelData
    };

    var bytes = PalmWriter.ToBytes(original);
    var restored = PalmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(16));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithPalette() {
    var palette = new byte[] {
      255, 0, 0,     // red
      0, 255, 0,     // green
      0, 0, 255,     // blue
      128, 128, 128  // gray
    };

    var pixelData = new byte[] { 0, 1, 2, 3, 3, 2, 1, 0 };

    var original = new PalmFile {
      Width = 4,
      Height = 2,
      BitsPerPixel = 8,
      Compression = PalmCompression.None,
      PixelData = pixelData,
      Palette = palette
    };

    var bytes = PalmWriter.ToBytes(original);
    var restored = PalmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.Palette, Is.Not.Null);
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithRle() {
    // 8x4, 8bpp: bytesPerRow = 8
    var pixelData = new byte[8 * 4];
    Array.Fill(pixelData, (byte)42);

    var original = new PalmFile {
      Width = 8,
      Height = 4,
      BitsPerPixel = 8,
      Compression = PalmCompression.Rle,
      PixelData = pixelData
    };

    var bytes = PalmWriter.ToBytes(original);
    var restored = PalmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.Compression, Is.EqualTo(PalmCompression.Rle));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithTransparency() {
    var original = new PalmFile {
      Width = 4,
      Height = 2,
      BitsPerPixel = 8,
      Compression = PalmCompression.None,
      TransparentIndex = 3,
      PixelData = new byte[] { 0, 1, 2, 3, 3, 2, 1, 0 }
    };

    var bytes = PalmWriter.ToBytes(original);
    var restored = PalmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.TransparentIndex, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
