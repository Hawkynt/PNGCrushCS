using System;
using System.IO;
using FileFormat.CokeAtari;
using FileFormat.Core;

namespace FileFormat.CokeAtari.Tests;

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

    var original = new CokeAtariFile {
      Width = 4,
      Height = 3,
      PixelData = pixelData,
    };

    var bytes = CokeAtariWriter.ToBytes(original);
    var restored = CokeAtariReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new CokeAtariFile {
      Width = 8,
      Height = 4,
      PixelData = new byte[8 * 4 * 2],
    };

    var bytes = CokeAtariWriter.ToBytes(original);
    var restored = CokeAtariReader.FromBytes(bytes);

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

    var original = new CokeAtariFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var bytes = CokeAtariWriter.ToBytes(original);
    var restored = CokeAtariReader.FromBytes(bytes);

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

    var original = new CokeAtariFile {
      Width = 16,
      Height = 8,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".tg1");
    try {
      var bytes = CokeAtariWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = CokeAtariReader.FromFile(new FileInfo(tempPath));

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

    var file = CokeAtariFile.FromRawImage(raw);
    var rawBack = CokeAtariFile.ToRawImage(file);

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

    var file = CokeAtariFile.FromRawImage(raw);
    var rawBack = CokeAtariFile.ToRawImage(file);

    Assert.That(rawBack.PixelData[0], Is.EqualTo(0));
    Assert.That(rawBack.PixelData[1], Is.EqualTo(255));
    Assert.That(rawBack.PixelData[2], Is.EqualTo(0));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_PureBlue() {
    var rgb24 = new byte[4 * 3 * 3];
    rgb24[0] = 0;
    rgb24[1] = 0;
    rgb24[2] = 255;

    var raw = new RawImage {
      Width = 4,
      Height = 3,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };

    var file = CokeAtariFile.FromRawImage(raw);
    var rawBack = CokeAtariFile.ToRawImage(file);

    Assert.That(rawBack.PixelData[0], Is.EqualTo(0));
    Assert.That(rawBack.PixelData[1], Is.EqualTo(0));
    Assert.That(rawBack.PixelData[2], Is.EqualTo(255));
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

    var file = CokeAtariFile.FromRawImage(raw);
    var rawBack = CokeAtariFile.ToRawImage(file);

    Assert.That(rawBack.PixelData[0], Is.EqualTo(206));
    Assert.That(rawBack.PixelData[1], Is.EqualTo(101));
    Assert.That(rawBack.PixelData[2], Is.EqualTo(49));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllMaxValues() {
    var pixelData = new byte[8 * 4 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = 0xFF;

    var original = new CokeAtariFile {
      Width = 8,
      Height = 4,
      PixelData = pixelData,
    };

    var bytes = CokeAtariWriter.ToBytes(original);
    var restored = CokeAtariReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
