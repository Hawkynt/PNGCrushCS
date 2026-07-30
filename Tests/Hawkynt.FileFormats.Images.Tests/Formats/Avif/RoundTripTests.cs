using System;
using System.IO;
using FileFormat.Avif;

namespace FileFormat.Avif.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_RgbPixels() {
    var width = 2;
    var height = 2;
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 17 % 256);

    var original = new AvifFile {
      Width = width,
      Height = height,
      PixelData = pixels,
      RawImageData = pixels,
    };

    var bytes = AvifWriter.ToBytes(original);
    var restored = AvifReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var width = 4;
    var height = 3;
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 13 % 256);

    var original = new AvifFile {
      Width = width,
      Height = height,
      PixelData = pixels,
      RawImageData = pixels,
    };

    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".avif");
    try {
      var bytes = AvifWriter.ToBytes(original);
      File.WriteAllBytes(path, bytes);

      var restored = AvifReader.FromFile(new FileInfo(path));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var width = 3;
    var height = 2;
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7 % 256);

    var original = new AvifFile {
      Width = width,
      Height = height,
      PixelData = pixels,
      RawImageData = pixels,
    };

    var rawImage = AvifFile.ToRawImage(original);
    var restored = AvifFile.FromRawImage(rawImage);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var width = 4;
    var height = 4;
    var pixels = new byte[width * height * 3];

    var original = new AvifFile {
      Width = width,
      Height = height,
      PixelData = pixels,
      RawImageData = pixels,
    };

    var bytes = AvifWriter.ToBytes(original);
    var restored = AvifReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient() {
    var width = 8;
    var height = 8;
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var idx = (y * width + x) * 3;
        pixels[idx] = (byte)(x * 32);
        pixels[idx + 1] = (byte)(y * 32);
        pixels[idx + 2] = (byte)((x + y) * 16);
      }

    var original = new AvifFile {
      Width = width,
      Height = height,
      PixelData = pixels,
      RawImageData = pixels,
    };

    var bytes = AvifWriter.ToBytes(original);
    var restored = AvifReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BrandPreserved() {
    var original = new AvifFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3],
      RawImageData = new byte[3],
      Brand = "avif",
    };

    var bytes = AvifWriter.ToBytes(original);
    var restored = AvifReader.FromBytes(bytes);

    Assert.That(restored.Brand, Is.EqualTo("avif"));
  }
}
