using System;
using FileFormat.Rla;

namespace FileFormat.Rla.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb8() {
    var width = 4;
    var height = 3;
    var channels = 3;
    var pixelData = new byte[width * height * channels];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new RlaFile {
      Width = width,
      Height = height,
      NumChannels = channels,
      NumMatte = 0,
      NumBits = 8,
      PixelData = pixelData
    };

    var bytes = RlaWriter.ToBytes(original);
    var restored = RlaReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.NumChannels, Is.EqualTo(original.NumChannels));
    Assert.That(restored.NumMatte, Is.EqualTo(original.NumMatte));
    Assert.That(restored.NumBits, Is.EqualTo(original.NumBits));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba8() {
    var width = 4;
    var height = 2;
    var channels = 3;
    var matte = 1;
    var totalChannels = channels + matte;
    var pixelData = new byte[width * height * totalChannels];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new RlaFile {
      Width = width,
      Height = height,
      NumChannels = channels,
      NumMatte = matte,
      NumBits = 8,
      PixelData = pixelData
    };

    var bytes = RlaWriter.ToBytes(original);
    var restored = RlaReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.NumChannels, Is.EqualTo(original.NumChannels));
    Assert.That(restored.NumMatte, Is.EqualTo(original.NumMatte));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_16bit() {
    var width = 4;
    var height = 2;
    var channels = 3;
    var bytesPerChannel = 2;
    var pixelData = new byte[width * height * channels * bytesPerChannel];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var original = new RlaFile {
      Width = width,
      Height = height,
      NumChannels = channels,
      NumMatte = 0,
      NumBits = 16,
      PixelData = pixelData
    };

    var bytes = RlaWriter.ToBytes(original);
    var restored = RlaReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.NumBits, Is.EqualTo(16));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DescriptionPreserved() {
    var original = new RlaFile {
      Width = 2,
      Height = 2,
      NumChannels = 3,
      NumMatte = 0,
      NumBits = 8,
      Description = "test desc",
      ProgramName = "test prog",
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = RlaWriter.ToBytes(original);
    var restored = RlaReader.FromBytes(bytes);

    Assert.That(restored.Description, Is.EqualTo("test desc"));
    Assert.That(restored.ProgramName, Is.EqualTo("test prog"));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleChannel() {
    var width = 8;
    var height = 4;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 5 == 0 ? 42 : i);

    var original = new RlaFile {
      Width = width,
      Height = height,
      NumChannels = 1,
      NumMatte = 0,
      NumBits = 8,
      PixelData = pixelData
    };

    var bytes = RlaWriter.ToBytes(original);
    var restored = RlaReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.NumChannels, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
