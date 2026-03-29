using System;
using FileFormat.Sgi;

namespace FileFormat.Sgi.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_GrayscaleUncompressed() {
    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new SgiFile {
      Width = 4,
      Height = 3,
      Channels = 1,
      BytesPerChannel = 1,
      Compression = SgiCompression.None,
      ColorMode = SgiColorMode.Normal,
      ImageName = "test",
      PixelData = pixelData
    };

    var bytes = SgiWriter.ToBytes(original);
    var restored = SgiReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Channels, Is.EqualTo(original.Channels));
    Assert.That(restored.BytesPerChannel, Is.EqualTo(original.BytesPerChannel));
    Assert.That(restored.Compression, Is.EqualTo(SgiCompression.None));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RgbUncompressed() {
    var pixelData = new byte[4 * 3 * 3]; // 4x3, 3 channels
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new SgiFile {
      Width = 4,
      Height = 3,
      Channels = 3,
      BytesPerChannel = 1,
      Compression = SgiCompression.None,
      ColorMode = SgiColorMode.Normal,
      ImageName = "rgb test",
      PixelData = pixelData
    };

    var bytes = SgiWriter.ToBytes(original);
    var restored = SgiReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Channels, Is.EqualTo(3));
    Assert.That(restored.Compression, Is.EqualTo(SgiCompression.None));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_GrayscaleRle() {
    var pixelData = new byte[8 * 4]; // 8x4, 1 channel
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 5 == 0 ? 42 : i);

    var original = new SgiFile {
      Width = 8,
      Height = 4,
      Channels = 1,
      BytesPerChannel = 1,
      Compression = SgiCompression.Rle,
      ColorMode = SgiColorMode.Normal,
      ImageName = "rle gray",
      PixelData = pixelData
    };

    var bytes = SgiWriter.ToBytes(original);
    var restored = SgiReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Channels, Is.EqualTo(1));
    Assert.That(restored.Compression, Is.EqualTo(SgiCompression.Rle));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RgbRle() {
    var pixelData = new byte[8 * 4 * 3]; // 8x4, 3 channels
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var original = new SgiFile {
      Width = 8,
      Height = 4,
      Channels = 3,
      BytesPerChannel = 1,
      Compression = SgiCompression.Rle,
      ColorMode = SgiColorMode.Normal,
      ImageName = "rle rgb",
      PixelData = pixelData
    };

    var bytes = SgiWriter.ToBytes(original);
    var restored = SgiReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Channels, Is.EqualTo(3));
    Assert.That(restored.Compression, Is.EqualTo(SgiCompression.Rle));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RgbaRle() {
    var pixelData = new byte[4 * 2 * 4]; // 4x2, 4 channels
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new SgiFile {
      Width = 4,
      Height = 2,
      Channels = 4,
      BytesPerChannel = 1,
      Compression = SgiCompression.Rle,
      ColorMode = SgiColorMode.Normal,
      PixelData = pixelData
    };

    var bytes = SgiWriter.ToBytes(original);
    var restored = SgiReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Channels, Is.EqualTo(4));
    Assert.That(restored.Compression, Is.EqualTo(SgiCompression.Rle));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ImageNamePreserved() {
    var original = new SgiFile {
      Width = 2,
      Height = 2,
      Channels = 1,
      BytesPerChannel = 1,
      Compression = SgiCompression.None,
      ColorMode = SgiColorMode.Normal,
      ImageName = "Hello SGI",
      PixelData = new byte[4]
    };

    var bytes = SgiWriter.ToBytes(original);
    var restored = SgiReader.FromBytes(bytes);

    Assert.That(restored.ImageName, Is.EqualTo("Hello SGI"));
  }
}
