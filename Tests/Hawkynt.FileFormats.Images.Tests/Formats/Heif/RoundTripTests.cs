using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Heif;

namespace FileFormat.Heif.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SmallRgbImage() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new HeifFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
      RawImageData = pixelData,
    };

    var bytes = HeifWriter.ToBytes(original);
    var restored = HeifReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.RawImageData, Is.EqualTo(original.RawImageData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var width = 8;
    var height = 6;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new HeifFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
      RawImageData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".heic");
    try {
      File.WriteAllBytes(tempPath, HeifWriter.ToBytes(original));
      var restored = HeifReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.RawImageData, Is.EqualTo(original.RawImageData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var width = 4;
    var height = 4;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var rawImage = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixelData,
    };

    var heifFile = HeifFile.FromRawImage(rawImage);
    var bytes = HeifWriter.ToBytes(heifFile);
    var restored = HeifReader.FromBytes(bytes);
    var restoredRaw = HeifFile.ToRawImage(restored);

    Assert.That(restoredRaw.Width, Is.EqualTo(width));
    Assert.That(restoredRaw.Height, Is.EqualTo(height));
    Assert.That(restoredRaw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(restoredRaw.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var width = 2;
    var height = 2;
    var pixelData = new byte[width * height * 3];

    var original = new HeifFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
      RawImageData = pixelData,
    };

    var bytes = HeifWriter.ToBytes(original);
    var restored = HeifReader.FromBytes(bytes);

    Assert.That(restored.RawImageData, Is.EqualTo(pixelData));
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

    var original = new HeifFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
      RawImageData = pixelData,
    };

    var bytes = HeifWriter.ToBytes(original);
    var restored = HeifReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.RawImageData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BrandPreserved() {
    var original = new HeifFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[12],
      RawImageData = new byte[12],
      Brand = "heix",
    };

    var bytes = HeifWriter.ToBytes(original);
    var restored = HeifReader.FromBytes(bytes);

    Assert.That(restored.Brand, Is.EqualTo("heix"));
  }
}
