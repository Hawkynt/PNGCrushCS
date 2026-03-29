using System;
using FileFormat.Qoi;

namespace FileFormat.Qoi.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_2x2() {
    var pixelData = new byte[2 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new QoiFile {
      Width = 2,
      Height = 2,
      Channels = QoiChannels.Rgb,
      ColorSpace = QoiColorSpace.Srgb,
      PixelData = pixelData
    };

    var bytes = QoiWriter.ToBytes(original);
    var restored = QoiReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Channels, Is.EqualTo(QoiChannels.Rgb));
    Assert.That(restored.ColorSpace, Is.EqualTo(QoiColorSpace.Srgb));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba_2x2() {
    var pixelData = new byte[2 * 2 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 31 % 256);

    var original = new QoiFile {
      Width = 2,
      Height = 2,
      Channels = QoiChannels.Rgba,
      ColorSpace = QoiColorSpace.Linear,
      PixelData = pixelData
    };

    var bytes = QoiWriter.ToBytes(original);
    var restored = QoiReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Channels, Is.EqualTo(QoiChannels.Rgba));
    Assert.That(restored.ColorSpace, Is.EqualTo(QoiColorSpace.Linear));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_LargerImage() {
    var width = 16;
    var height = 16;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new QoiFile {
      Width = width,
      Height = height,
      Channels = QoiChannels.Rgb,
      ColorSpace = QoiColorSpace.Srgb,
      PixelData = pixelData
    };

    var bytes = QoiWriter.ToBytes(original);
    var restored = QoiReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba_AllBlack() {
    var pixelData = new byte[4 * 4 * 4];
    // All black with full alpha
    for (var i = 0; i < 4 * 4; ++i)
      pixelData[i * 4 + 3] = 255;

    var original = new QoiFile {
      Width = 4,
      Height = 4,
      Channels = QoiChannels.Rgba,
      ColorSpace = QoiColorSpace.Srgb,
      PixelData = pixelData
    };

    var bytes = QoiWriter.ToBytes(original);
    var restored = QoiReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_AllWhite() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = 255;

    var original = new QoiFile {
      Width = 4,
      Height = 4,
      Channels = QoiChannels.Rgb,
      ColorSpace = QoiColorSpace.Srgb,
      PixelData = pixelData
    };

    var bytes = QoiWriter.ToBytes(original);
    var restored = QoiReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba_GradientAlpha() {
    var width = 8;
    var height = 1;
    var pixelData = new byte[width * height * 4];
    for (var i = 0; i < width; ++i) {
      pixelData[i * 4] = (byte)(i * 32);       // R
      pixelData[i * 4 + 1] = (byte)(255 - i * 32); // G
      pixelData[i * 4 + 2] = 128;               // B
      pixelData[i * 4 + 3] = (byte)(i * 32);    // A
    }

    var original = new QoiFile {
      Width = width,
      Height = height,
      Channels = QoiChannels.Rgba,
      ColorSpace = QoiColorSpace.Srgb,
      PixelData = pixelData
    };

    var bytes = QoiWriter.ToBytes(original);
    var restored = QoiReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesColorSpace() {
    var original = new QoiFile {
      Width = 1,
      Height = 1,
      Channels = QoiChannels.Rgb,
      ColorSpace = QoiColorSpace.Linear,
      PixelData = new byte[] { 100, 200, 50 }
    };

    var bytes = QoiWriter.ToBytes(original);
    var restored = QoiReader.FromBytes(bytes);

    Assert.That(restored.ColorSpace, Is.EqualTo(QoiColorSpace.Linear));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_1x1() {
    var original = new QoiFile {
      Width = 1,
      Height = 1,
      Channels = QoiChannels.Rgb,
      ColorSpace = QoiColorSpace.Srgb,
      PixelData = new byte[] { 42, 84, 126 }
    };

    var bytes = QoiWriter.ToBytes(original);
    var restored = QoiReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
