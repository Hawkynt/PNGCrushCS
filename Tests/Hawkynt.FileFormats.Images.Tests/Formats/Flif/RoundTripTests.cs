using System;
using System.IO;
using FileFormat.Flif;

namespace FileFormat.Flif.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale_2x2() {
    var pixelData = new byte[] { 0, 64, 128, 255 };
    var original = new FlifFile {
      Width = 2,
      Height = 2,
      ChannelCount = FlifChannelCount.Gray,
      BitsPerChannel = 8,
      PixelData = pixelData
    };

    var bytes = FlifWriter.ToBytes(original);
    var restored = FlifReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ChannelCount, Is.EqualTo(FlifChannelCount.Gray));
    Assert.That(restored.BitsPerChannel, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_2x2() {
    var pixelData = new byte[2 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new FlifFile {
      Width = 2,
      Height = 2,
      ChannelCount = FlifChannelCount.Rgb,
      BitsPerChannel = 8,
      PixelData = pixelData
    };

    var bytes = FlifWriter.ToBytes(original);
    var restored = FlifReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ChannelCount, Is.EqualTo(FlifChannelCount.Rgb));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba_2x2() {
    var pixelData = new byte[2 * 2 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 31 % 256);

    var original = new FlifFile {
      Width = 2,
      Height = 2,
      ChannelCount = FlifChannelCount.Rgba,
      BitsPerChannel = 8,
      PixelData = pixelData
    };

    var bytes = FlifWriter.ToBytes(original);
    var restored = FlifReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ChannelCount, Is.EqualTo(FlifChannelCount.Rgba));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var pixelData = new byte[4 * 4 * 3];

    var original = new FlifFile {
      Width = 4,
      Height = 4,
      ChannelCount = FlifChannelCount.Rgb,
      BitsPerChannel = 8,
      PixelData = pixelData
    };

    var bytes = FlifWriter.ToBytes(original);
    var restored = FlifReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient() {
    var width = 16;
    var height = 16;
    var pixelData = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var idx = (y * width + x) * 3;
        pixelData[idx] = (byte)(x * 16);
        pixelData[idx + 1] = (byte)(y * 16);
        pixelData[idx + 2] = (byte)((x + y) * 8);
      }

    var original = new FlifFile {
      Width = width,
      Height = height,
      ChannelCount = FlifChannelCount.Rgb,
      BitsPerChannel = 8,
      PixelData = pixelData
    };

    var bytes = FlifWriter.ToBytes(original);
    var restored = FlifReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new FlifFile {
      Width = 4,
      Height = 4,
      ChannelCount = FlifChannelCount.Rgb,
      BitsPerChannel = 8,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".flif");
    try {
      var bytes = FlifWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = FlifReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.ChannelCount, Is.EqualTo(original.ChannelCount));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Gray() {
    var image = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Gray8,
      PixelData = new byte[] { 10, 20, 30, 40 }
    };

    var flifFile = FlifFile.FromRawImage(image);
    var rawBack = FlifFile.ToRawImage(flifFile);

    Assert.That(rawBack.Width, Is.EqualTo(image.Width));
    Assert.That(rawBack.Height, Is.EqualTo(image.Height));
    Assert.That(rawBack.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Gray8));
    Assert.That(rawBack.PixelData, Is.EqualTo(image.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb() {
    var image = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 2,
      Format = FileFormat.Core.PixelFormat.Rgb24,
      PixelData = new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255, 128, 128, 128 }
    };

    var flifFile = FlifFile.FromRawImage(image);
    var rawBack = FlifFile.ToRawImage(flifFile);

    Assert.That(rawBack.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgb24));
    Assert.That(rawBack.PixelData, Is.EqualTo(image.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgba() {
    var image = new FileFormat.Core.RawImage {
      Width = 2,
      Height = 1,
      Format = FileFormat.Core.PixelFormat.Rgba32,
      PixelData = new byte[] { 255, 0, 0, 255, 0, 255, 0, 128 }
    };

    var flifFile = FlifFile.FromRawImage(image);
    var rawBack = FlifFile.ToRawImage(flifFile);

    Assert.That(rawBack.Format, Is.EqualTo(FileFormat.Core.PixelFormat.Rgba32));
    Assert.That(rawBack.PixelData, Is.EqualTo(image.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeDimensions() {
    var width = 200;
    var height = 150;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new FlifFile {
      Width = width,
      Height = height,
      ChannelCount = FlifChannelCount.Rgb,
      BitsPerChannel = 8,
      PixelData = pixelData
    };

    var bytes = FlifWriter.ToBytes(original);
    var restored = FlifReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_1x1() {
    var original = new FlifFile {
      Width = 1,
      Height = 1,
      ChannelCount = FlifChannelCount.Rgb,
      BitsPerChannel = 8,
      PixelData = new byte[] { 42, 84, 126 }
    };

    var bytes = FlifWriter.ToBytes(original);
    var restored = FlifReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Interlaced_Preserved() {
    var original = new FlifFile {
      Width = 4,
      Height = 4,
      ChannelCount = FlifChannelCount.Rgb,
      BitsPerChannel = 8,
      IsInterlaced = true,
      PixelData = new byte[4 * 4 * 3]
    };

    var bytes = FlifWriter.ToBytes(original);
    var restored = FlifReader.FromBytes(bytes);

    Assert.That(restored.IsInterlaced, Is.True);
  }
}
