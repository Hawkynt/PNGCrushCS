using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Jpeg2000;

namespace FileFormat.Jpeg2000.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_2x2() {
    var pixelData = new byte[2 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new Jpeg2000File {
      Width = 2,
      Height = 2,
      ComponentCount = 3,
      BitsPerComponent = 8,
      PixelData = pixelData,
    };

    var bytes = Jpeg2000Writer.ToBytes(original);
    var restored = Jpeg2000Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ComponentCount, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale_2x2() {
    var pixelData = new byte[2 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 31 % 256);

    var original = new Jpeg2000File {
      Width = 2,
      Height = 2,
      ComponentCount = 1,
      BitsPerComponent = 8,
      PixelData = pixelData,
    };

    var bytes = Jpeg2000Writer.ToBytes(original);
    var restored = Jpeg2000Reader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ComponentCount, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_Rgb() {
    var pixelData = new byte[4 * 4 * 3];

    var original = new Jpeg2000File {
      Width = 4,
      Height = 4,
      ComponentCount = 3,
      BitsPerComponent = 8,
      PixelData = pixelData,
    };

    var bytes = Jpeg2000Writer.ToBytes(original);
    var restored = Jpeg2000Reader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros_Grayscale() {
    var pixelData = new byte[4 * 4];

    var original = new Jpeg2000File {
      Width = 4,
      Height = 4,
      ComponentCount = 1,
      BitsPerComponent = 8,
      PixelData = pixelData,
    };

    var bytes = Jpeg2000Writer.ToBytes(original);
    var restored = Jpeg2000Reader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient_Rgb() {
    var width = 8;
    var height = 8;
    var pixelData = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var idx = (y * width + x) * 3;
        pixelData[idx] = (byte)(x * 32);
        pixelData[idx + 1] = (byte)(y * 32);
        pixelData[idx + 2] = (byte)((x + y) * 16);
      }

    var original = new Jpeg2000File {
      Width = width,
      Height = height,
      ComponentCount = 3,
      BitsPerComponent = 8,
      PixelData = pixelData,
    };

    var bytes = Jpeg2000Writer.ToBytes(original);
    var restored = Jpeg2000Reader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient_Grayscale() {
    var width = 8;
    var height = 8;
    var pixelData = new byte[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        pixelData[y * width + x] = (byte)((x + y) * 16);

    var original = new Jpeg2000File {
      Width = width,
      Height = height,
      ComponentCount = 1,
      BitsPerComponent = 8,
      PixelData = pixelData,
    };

    var bytes = Jpeg2000Writer.ToBytes(original);
    var restored = Jpeg2000Reader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new Jpeg2000File {
      Width = 4,
      Height = 4,
      ComponentCount = 3,
      BitsPerComponent = 8,
      PixelData = pixelData,
    };

    var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jp2");
    try {
      var bytes = Jpeg2000Writer.ToBytes(original);
      File.WriteAllBytes(tempFile, bytes);

      var restored = Jpeg2000Reader.FromFile(new FileInfo(tempFile));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempFile))
        File.Delete(tempFile);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb() {
    var rawImage = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[4 * 4 * 3],
    };
    for (var i = 0; i < rawImage.PixelData.Length; ++i)
      rawImage.PixelData[i] = (byte)(i * 13 % 256);

    var jp2 = Jpeg2000File.FromRawImage(rawImage);
    var bytes = Jpeg2000Writer.ToBytes(jp2);
    var restored = Jpeg2000Reader.FromBytes(bytes);
    var restoredRaw = Jpeg2000File.ToRawImage(restored);

    Assert.That(restoredRaw.Width, Is.EqualTo(rawImage.Width));
    Assert.That(restoredRaw.Height, Is.EqualTo(rawImage.Height));
    Assert.That(restoredRaw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(restoredRaw.PixelData, Is.EqualTo(rawImage.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Grayscale() {
    var rawImage = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Gray8,
      PixelData = new byte[4 * 4],
    };
    for (var i = 0; i < rawImage.PixelData.Length; ++i)
      rawImage.PixelData[i] = (byte)(i * 17 % 256);

    var jp2 = Jpeg2000File.FromRawImage(rawImage);
    var bytes = Jpeg2000Writer.ToBytes(jp2);
    var restored = Jpeg2000Reader.FromBytes(bytes);
    var restoredRaw = Jpeg2000File.ToRawImage(restored);

    Assert.That(restoredRaw.Width, Is.EqualTo(rawImage.Width));
    Assert.That(restoredRaw.Height, Is.EqualTo(rawImage.Height));
    Assert.That(restoredRaw.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(restoredRaw.PixelData, Is.EqualTo(rawImage.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage_Rgb() {
    var width = 16;
    var height = 16;
    var pixelData = new byte[width * height * 3];
    var rng = new Random(42);
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)rng.Next(256);

    var original = new Jpeg2000File {
      Width = width,
      Height = height,
      ComponentCount = 3,
      BitsPerComponent = 8,
      PixelData = pixelData,
    };

    var bytes = Jpeg2000Writer.ToBytes(original);
    var restored = Jpeg2000Reader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_OddDimensions_Rgb() {
    var width = 7;
    var height = 5;
    var pixelData = new byte[width * height * 3];
    var rng = new Random(123);
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)rng.Next(256);

    var original = new Jpeg2000File {
      Width = width,
      Height = height,
      ComponentCount = 3,
      BitsPerComponent = 8,
      DecompositionLevels = 2,
      PixelData = pixelData,
    };

    var bytes = Jpeg2000Writer.ToBytes(original);
    var restored = Jpeg2000Reader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DecompositionLevels_Preserved() {
    var pixelData = new byte[8 * 8 * 3];
    var original = new Jpeg2000File {
      Width = 8,
      Height = 8,
      ComponentCount = 3,
      BitsPerComponent = 8,
      DecompositionLevels = 2,
      PixelData = pixelData,
    };

    var bytes = Jpeg2000Writer.ToBytes(original);
    var restored = Jpeg2000Reader.FromBytes(bytes);

    Assert.That(restored.DecompositionLevels, Is.EqualTo(2));
  }
}
