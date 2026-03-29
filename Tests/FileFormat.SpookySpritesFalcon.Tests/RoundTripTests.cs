using System;
using System.IO;
using FileFormat.SpookySpritesFalcon;
using FileFormat.Core;

namespace FileFormat.SpookySpritesFalcon.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SmallImage() {
    var pixelData = new byte[4 * 3 * 2];
    pixelData[0] = 0xF8;
    pixelData[1] = 0x00;
    pixelData[2] = 0x07;
    pixelData[3] = 0xE0;
    pixelData[pixelData.Length - 2] = 0x00;
    pixelData[pixelData.Length - 1] = 0x1F;

    var original = new SpookySpritesFalconFile {
      Width = 4,
      Height = 3,
      PixelData = pixelData,
    };

    var bytes = SpookySpritesFalconWriter.ToBytes(original);
    var restored = SpookySpritesFalconReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new SpookySpritesFalconFile {
      Width = 8,
      Height = 4,
      PixelData = new byte[8 * 4 * 2],
    };

    var bytes = SpookySpritesFalconWriter.ToBytes(original);
    var restored = SpookySpritesFalconReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    var width = 320;
    var height = 200;
    var pixelData = new byte[width * height * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new SpookySpritesFalconFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var bytes = SpookySpritesFalconWriter.ToBytes(original);
    var restored = SpookySpritesFalconReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[16 * 8 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new SpookySpritesFalconFile {
      Width = 16,
      Height = 8,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tre");
    try {
      var bytes = SpookySpritesFalconWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = SpookySpritesFalconReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_PureRed() {
    var rgb24 = new byte[4 * 3 * 3];
    rgb24[0] = 255;
    rgb24[1] = 0;
    rgb24[2] = 0;

    var raw = new RawImage {
      Width = 4,
      Height = 3,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = SpookySpritesFalconFile.FromRawImage(raw);
    var rawBack = SpookySpritesFalconFile.ToRawImage(file);

    Assert.That(rawBack.PixelData[0], Is.EqualTo(255));
    Assert.That(rawBack.PixelData[1], Is.EqualTo(0));
    Assert.That(rawBack.PixelData[2], Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_PureGreen() {
    var rgb24 = new byte[4 * 3 * 3];
    rgb24[0] = 0;
    rgb24[1] = 255;
    rgb24[2] = 0;

    var raw = new RawImage {
      Width = 4,
      Height = 3,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = SpookySpritesFalconFile.FromRawImage(raw);
    var rawBack = SpookySpritesFalconFile.ToRawImage(file);

    Assert.That(rawBack.PixelData[0], Is.EqualTo(0));
    Assert.That(rawBack.PixelData[1], Is.EqualTo(255));
    Assert.That(rawBack.PixelData[2], Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb565Precision() {
    var rgb24 = new byte[2 * 1 * 3];
    rgb24[0] = 200;
    rgb24[1] = 100;
    rgb24[2] = 50;

    var raw = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = SpookySpritesFalconFile.FromRawImage(raw);
    var rawBack = SpookySpritesFalconFile.ToRawImage(file);

    Assert.That(rawBack.PixelData[0], Is.EqualTo(206));
    Assert.That(rawBack.PixelData[1], Is.EqualTo(101));
    Assert.That(rawBack.PixelData[2], Is.EqualTo(49));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Compressed_AllSamePixels() {
    var pixelData = new byte[100 * 2];
    for (var i = 0; i < pixelData.Length; i += 2) {
      pixelData[i] = 0xF8;
      pixelData[i + 1] = 0x00;
    }

    var original = new SpookySpritesFalconFile {
      Width = 100,
      Height = 1,
      PixelData = pixelData,
    };

    var bytes = SpookySpritesFalconWriter.ToBytes(original);
    var restored = SpookySpritesFalconReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(bytes.Length, Is.LessThan(SpookySpritesFalconHeader.StructSize + pixelData.Length));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllMaxValues() {
    var pixelData = new byte[8 * 4 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = 0xFF;

    var original = new SpookySpritesFalconFile {
      Width = 8,
      Height = 4,
      PixelData = pixelData,
    };

    var bytes = SpookySpritesFalconWriter.ToBytes(original);
    var restored = SpookySpritesFalconReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
