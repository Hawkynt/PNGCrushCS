using System;
using FileFormat.UtahRle;

namespace FileFormat.UtahRle.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale() {
    var pixelData = new byte[4 * 3]; // 4x3, 1 channel
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new UtahRleFile {
      Width = 4,
      Height = 3,
      NumChannels = 1,
      PixelData = pixelData
    };

    var bytes = UtahRleWriter.ToBytes(original);
    var restored = UtahRleReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.NumChannels, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb() {
    var pixelData = new byte[4 * 3 * 3]; // 4x3, 3 channels
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new UtahRleFile {
      Width = 4,
      Height = 3,
      NumChannels = 3,
      PixelData = pixelData
    };

    var bytes = UtahRleWriter.ToBytes(original);
    var restored = UtahRleReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.NumChannels, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba() {
    var pixelData = new byte[2 * 2 * 4]; // 2x2, 4 channels
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new UtahRleFile {
      Width = 2,
      Height = 2,
      NumChannels = 4,
      PixelData = pixelData
    };

    var bytes = UtahRleWriter.ToBytes(original);
    var restored = UtahRleReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.NumChannels, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Position_Preserved() {
    var original = new UtahRleFile {
      XPos = 10,
      YPos = 20,
      Width = 2,
      Height = 2,
      NumChannels = 1,
      PixelData = [1, 2, 3, 4]
    };

    var bytes = UtahRleWriter.ToBytes(original);
    var restored = UtahRleReader.FromBytes(bytes);

    Assert.That(restored.XPos, Is.EqualTo(10));
    Assert.That(restored.YPos, Is.EqualTo(20));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_UniformData_CompressesWithRuns() {
    var pixelData = new byte[100]; // 100x1, 1 channel, all same value
    Array.Fill(pixelData, (byte)42);

    var original = new UtahRleFile {
      Width = 100,
      Height = 1,
      NumChannels = 1,
      PixelData = pixelData
    };

    var bytes = UtahRleWriter.ToBytes(original);
    var restored = UtahRleReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    // Encoded data should be smaller than raw pixel data
    Assert.That(bytes.Length, Is.LessThan(UtahRleHeader.StructSize + pixelData.Length));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithBackground() {
    var background = new byte[] { 128, 64, 32 };
    var pixelData = new byte[2 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new UtahRleFile {
      Width = 2,
      Height = 2,
      NumChannels = 3,
      PixelData = pixelData,
      BackgroundColor = background
    };

    var bytes = UtahRleWriter.ToBytes(original);
    var restored = UtahRleReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.NumChannels, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.BackgroundColor, Is.EqualTo(background));
  }
}
