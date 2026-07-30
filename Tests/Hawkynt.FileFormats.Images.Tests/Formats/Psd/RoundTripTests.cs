using System;
using FileFormat.Psd;

namespace FileFormat.Psd.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb8() {
    var pixelData = new byte[4 * 3 * 3]; // 4x3, 3 channels, 8-bit planar
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new PsdFile {
      Width = 4,
      Height = 3,
      Channels = 3,
      Depth = 8,
      ColorMode = PsdColorMode.RGB,
      PixelData = pixelData
    };

    var bytes = PsdWriter.ToBytes(original);
    var restored = PsdReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Channels, Is.EqualTo(original.Channels));
    Assert.That(restored.Depth, Is.EqualTo(8));
    Assert.That(restored.ColorMode, Is.EqualTo(PsdColorMode.RGB));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale8() {
    var pixelData = new byte[4 * 2]; // 4x2, 1 channel, 8-bit
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 31 % 256);

    var original = new PsdFile {
      Width = 4,
      Height = 2,
      Channels = 1,
      Depth = 8,
      ColorMode = PsdColorMode.Grayscale,
      PixelData = pixelData
    };

    var bytes = PsdWriter.ToBytes(original);
    var restored = PsdReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.Channels, Is.EqualTo(1));
    Assert.That(restored.Depth, Is.EqualTo(8));
    Assert.That(restored.ColorMode, Is.EqualTo(PsdColorMode.Grayscale));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_IndexedWithPalette() {
    // PSD indexed palette is 768 bytes: 256 R values, then 256 G values, then 256 B values
    var palette = new byte[768];
    for (var i = 0; i < 256; ++i) {
      palette[i] = (byte)i;         // R channel
      palette[256 + i] = (byte)(255 - i); // G channel
      palette[512 + i] = (byte)(i * 2 % 256); // B channel
    }

    var pixelData = new byte[4 * 2]; // 4x2, 1 channel indices
    pixelData[0] = 0;
    pixelData[1] = 1;
    pixelData[2] = 127;
    pixelData[3] = 255;
    pixelData[4] = 64;
    pixelData[5] = 128;
    pixelData[6] = 192;
    pixelData[7] = 32;

    var original = new PsdFile {
      Width = 4,
      Height = 2,
      Channels = 1,
      Depth = 8,
      ColorMode = PsdColorMode.Indexed,
      PixelData = pixelData,
      Palette = palette
    };

    var bytes = PsdWriter.ToBytes(original);
    var restored = PsdReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.Channels, Is.EqualTo(1));
    Assert.That(restored.ColorMode, Is.EqualTo(PsdColorMode.Indexed));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Palette, Is.Not.Null);
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesImageResources() {
    var imageResources = new byte[] { 0x38, 0x42, 0x49, 0x4D, 0x03, 0xE8, 0x00 };

    var original = new PsdFile {
      Width = 1,
      Height = 1,
      Channels = 3,
      Depth = 8,
      ColorMode = PsdColorMode.RGB,
      PixelData = new byte[3],
      ImageResources = imageResources
    };

    var bytes = PsdWriter.ToBytes(original);
    var restored = PsdReader.FromBytes(bytes);

    Assert.That(restored.ImageResources, Is.Not.Null);
    Assert.That(restored.ImageResources, Is.EqualTo(imageResources));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesLayerMaskInfo() {
    var layerMaskInfo = new byte[] { 0x01, 0x02, 0x03, 0x04 };

    var original = new PsdFile {
      Width = 1,
      Height = 1,
      Channels = 3,
      Depth = 8,
      ColorMode = PsdColorMode.RGB,
      PixelData = new byte[3],
      LayerMaskInfo = layerMaskInfo
    };

    var bytes = PsdWriter.ToBytes(original);
    var restored = PsdReader.FromBytes(bytes);

    Assert.That(restored.LayerMaskInfo, Is.Not.Null);
    Assert.That(restored.LayerMaskInfo, Is.EqualTo(layerMaskInfo));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba8_FourChannels() {
    var pixelData = new byte[2 * 2 * 4]; // 2x2, 4 channels (RGBA), 8-bit planar
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new PsdFile {
      Width = 2,
      Height = 2,
      Channels = 4,
      Depth = 8,
      ColorMode = PsdColorMode.RGB,
      PixelData = pixelData
    };

    var bytes = PsdWriter.ToBytes(original);
    var restored = PsdReader.FromBytes(bytes);

    Assert.That(restored.Channels, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
