using System;
using System.IO;
using FileFormat.Core;
using FileFormat.MayaIff;

namespace FileFormat.MayaIff.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new MayaIffFile {
      Width = width,
      Height = height,
      HasAlpha = true,
      PixelData = pixelData
    };

    var bytes = MayaIffWriter.ToBytes(original);
    var restored = MayaIffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.HasAlpha, Is.EqualTo(original.HasAlpha));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb() {
    var width = 3;
    var height = 2;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new MayaIffFile {
      Width = width,
      Height = height,
      HasAlpha = false,
      PixelData = pixelData
    };

    var bytes = MayaIffWriter.ToBytes(original);
    var restored = MayaIffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.HasAlpha, Is.EqualTo(original.HasAlpha));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = new MayaIffFile {
      Width = 2,
      Height = 1,
      HasAlpha = true,
      PixelData = [0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE]
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".iff");
    try {
      var bytes = MayaIffWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = MayaIffReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.HasAlpha, Is.EqualTo(original.HasAlpha));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var width = 2;
    var height = 2;
    var pixelData = new byte[width * height * 4];

    var original = new MayaIffFile {
      Width = width,
      Height = height,
      HasAlpha = true,
      PixelData = pixelData
    };

    var bytes = MayaIffWriter.ToBytes(original);
    var restored = MayaIffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgba() {
    var rawImage = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[] {
        0xFF, 0x00, 0x00, 0xFF,
        0x00, 0xFF, 0x00, 0x80,
        0x00, 0x00, 0xFF, 0xC0,
        0xAA, 0xBB, 0xCC, 0xDD
      }
    };

    var file = MayaIffFile.FromRawImage(rawImage);
    var raw2 = MayaIffFile.ToRawImage(file);

    Assert.That(raw2.Width, Is.EqualTo(rawImage.Width));
    Assert.That(raw2.Height, Is.EqualTo(rawImage.Height));
    Assert.That(raw2.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(raw2.PixelData, Is.EqualTo(rawImage.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb() {
    var rawImage = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [0xFF, 0x00, 0x80, 0x11, 0x22, 0x33]
    };

    var file = MayaIffFile.FromRawImage(rawImage);
    var raw2 = MayaIffFile.ToRawImage(file);

    Assert.That(raw2.Width, Is.EqualTo(rawImage.Width));
    Assert.That(raw2.Height, Is.EqualTo(rawImage.Height));
    Assert.That(raw2.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(raw2.PixelData, Is.EqualTo(rawImage.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var width = 64;
    var height = 48;
    var pixelData = new byte[width * height * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new MayaIffFile {
      Width = width,
      Height = height,
      HasAlpha = true,
      PixelData = pixelData
    };

    var bytes = MayaIffWriter.ToBytes(original);
    var restored = MayaIffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixelRgb() {
    var original = new MayaIffFile {
      Width = 1,
      Height = 1,
      HasAlpha = false,
      PixelData = [0xAA, 0xBB, 0xCC]
    };

    var bytes = MayaIffWriter.ToBytes(original);
    var restored = MayaIffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.HasAlpha, Is.False);
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
