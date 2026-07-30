using System;
using FileFormat.Tiff;

namespace FileFormat.Tiff.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7);

    var original = new TiffFile {
      Width = 4,
      Height = 4,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      PixelData = pixelData,
      ColorMode = TiffColorMode.Rgb
    };

    var bytes = TiffWriter.ToBytes(original);
    var restored = TiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.SamplesPerPixel, Is.EqualTo(3));
    Assert.That(restored.BitsPerSample, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale() {
    var pixelData = new byte[4 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 16);

    var original = new TiffFile {
      Width = 4,
      Height = 4,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      PixelData = pixelData,
      ColorMode = TiffColorMode.Grayscale
    };

    var bytes = TiffWriter.ToBytes(original);
    var restored = TiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.SamplesPerPixel, Is.EqualTo(1));
    Assert.That(restored.BitsPerSample, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Palette() {
    var colorMap = new byte[256 * 3];
    colorMap[0] = 255; colorMap[1] = 0; colorMap[2] = 0;
    colorMap[3] = 0; colorMap[4] = 255; colorMap[5] = 0;
    colorMap[6] = 0; colorMap[7] = 0; colorMap[8] = 255;
    colorMap[9] = 255; colorMap[10] = 255; colorMap[11] = 0;

    var pixelData = new byte[4 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 4);

    var original = new TiffFile {
      Width = 4,
      Height = 4,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      PixelData = pixelData,
      ColorMap = colorMap,
      ColorMode = TiffColorMode.Palette
    };

    var bytes = TiffWriter.ToBytes(original);
    var restored = TiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.SamplesPerPixel, Is.EqualTo(1));
    Assert.That(restored.BitsPerSample, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.ColorMap, Is.Not.Null);
    Assert.That(restored.ColorMap![0], Is.EqualTo(255));
    Assert.That(restored.ColorMap[1], Is.EqualTo(0));
    Assert.That(restored.ColorMap[2], Is.EqualTo(0));
    Assert.That(restored.ColorMap[3], Is.EqualTo(0));
    Assert.That(restored.ColorMap[4], Is.EqualTo(255));
    Assert.That(restored.ColorMap[5], Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PackBits() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13);

    var original = new TiffFile {
      Width = 4,
      Height = 4,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      PixelData = pixelData,
      ColorMode = TiffColorMode.Rgb
    };

    var bytes = TiffWriter.ToBytes(original, TiffCompression.PackBits);
    var restored = TiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Lzw() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 5);

    var original = new TiffFile {
      Width = 4,
      Height = 4,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      PixelData = pixelData,
      ColorMode = TiffColorMode.Rgb
    };

    var bytes = TiffWriter.ToBytes(original, TiffCompression.Lzw);
    var restored = TiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Tiled() {
    var pixelData = new byte[16 * 16 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 9);

    var original = new TiffFile {
      Width = 16,
      Height = 16,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      PixelData = pixelData,
      ColorMode = TiffColorMode.Rgb
    };

    var bytes = TiffWriter.ToBytes(original, tileWidth: 16, tileHeight: 16);
    var restored = TiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(16));
    Assert.That(restored.Height, Is.EqualTo(16));
    Assert.That(restored.SamplesPerPixel, Is.EqualTo(3));
    Assert.That(restored.BitsPerSample, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
