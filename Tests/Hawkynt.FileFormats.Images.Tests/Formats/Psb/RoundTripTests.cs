using System;
using System.IO;
using FileFormat.Psb;

namespace FileFormat.Psb.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb8() {
    var pixelData = new byte[4 * 3 * 3]; // 4x3, 3 channels, 8-bit planar
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new PsbFile {
      Width = 4,
      Height = 3,
      Channels = 3,
      Depth = 8,
      ColorMode = PsbColorMode.RGB,
      PixelData = pixelData
    };

    var bytes = PsbWriter.ToBytes(original);
    var restored = PsbReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Channels, Is.EqualTo(original.Channels));
    Assert.That(restored.Depth, Is.EqualTo(8));
    Assert.That(restored.ColorMode, Is.EqualTo(PsbColorMode.RGB));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba8_FourChannels() {
    var pixelData = new byte[2 * 2 * 4]; // 2x2, 4 channels (RGBA), 8-bit planar
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new PsbFile {
      Width = 2,
      Height = 2,
      Channels = 4,
      Depth = 8,
      ColorMode = PsbColorMode.RGB,
      PixelData = pixelData
    };

    var bytes = PsbWriter.ToBytes(original);
    var restored = PsbReader.FromBytes(bytes);

    Assert.That(restored.Channels, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale8() {
    var pixelData = new byte[4 * 2]; // 4x2, 1 channel, 8-bit
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 31 % 256);

    var original = new PsbFile {
      Width = 4,
      Height = 2,
      Channels = 1,
      Depth = 8,
      ColorMode = PsbColorMode.Grayscale,
      PixelData = pixelData
    };

    var bytes = PsbWriter.ToBytes(original);
    var restored = PsbReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.Channels, Is.EqualTo(1));
    Assert.That(restored.Depth, Is.EqualTo(8));
    Assert.That(restored.ColorMode, Is.EqualTo(PsbColorMode.Grayscale));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_IndexedWithPalette() {
    var palette = new byte[768];
    for (var i = 0; i < 256; ++i) {
      palette[i] = (byte)i;
      palette[256 + i] = (byte)(255 - i);
      palette[512 + i] = (byte)(i * 2 % 256);
    }

    var pixelData = new byte[4 * 2];
    pixelData[0] = 0;
    pixelData[1] = 1;
    pixelData[2] = 127;
    pixelData[3] = 255;
    pixelData[4] = 64;
    pixelData[5] = 128;
    pixelData[6] = 192;
    pixelData[7] = 32;

    var original = new PsbFile {
      Width = 4,
      Height = 2,
      Channels = 1,
      Depth = 8,
      ColorMode = PsbColorMode.Indexed,
      PixelData = pixelData,
      Palette = palette
    };

    var bytes = PsbWriter.ToBytes(original);
    var restored = PsbReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.Channels, Is.EqualTo(1));
    Assert.That(restored.ColorMode, Is.EqualTo(PsbColorMode.Indexed));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Palette, Is.Not.Null);
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesImageResources() {
    var imageResources = new byte[] { 0x38, 0x42, 0x49, 0x4D, 0x03, 0xE8, 0x00 };

    var original = new PsbFile {
      Width = 1,
      Height = 1,
      Channels = 3,
      Depth = 8,
      ColorMode = PsbColorMode.RGB,
      PixelData = new byte[3],
      ImageResources = imageResources
    };

    var bytes = PsbWriter.ToBytes(original);
    var restored = PsbReader.FromBytes(bytes);

    Assert.That(restored.ImageResources, Is.Not.Null);
    Assert.That(restored.ImageResources, Is.EqualTo(imageResources));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesLayerMaskInfo() {
    var layerMaskInfo = new byte[] { 0x01, 0x02, 0x03, 0x04 };

    var original = new PsbFile {
      Width = 1,
      Height = 1,
      Channels = 3,
      Depth = 8,
      ColorMode = PsbColorMode.RGB,
      PixelData = new byte[3],
      LayerMaskInfo = layerMaskInfo
    };

    var bytes = PsbWriter.ToBytes(original);
    var restored = PsbReader.FromBytes(bytes);

    Assert.That(restored.LayerMaskInfo, Is.Not.Null);
    Assert.That(restored.LayerMaskInfo, Is.EqualTo(layerMaskInfo));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFileInfo() {
    var pixelData = new byte[3 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new PsbFile {
      Width = 3,
      Height = 2,
      Channels = 3,
      Depth = 8,
      ColorMode = PsbColorMode.RGB,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".psb");
    try {
      var bytes = PsbWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = PsbReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(3));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.Channels, Is.EqualTo(3));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
