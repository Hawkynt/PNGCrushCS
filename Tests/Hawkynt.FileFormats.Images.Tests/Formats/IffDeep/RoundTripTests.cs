using System;
using System.IO;
using FileFormat.Core;
using FileFormat.IffDeep;

namespace FileFormat.IffDeep.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_SmallImage() {
    var pixelData = new byte[4 * 3 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new IffDeepFile {
      Width = 4,
      Height = 3,
      HasAlpha = false,
      Compression = IffDeepCompression.None,
      PixelData = pixelData
    };

    var bytes = IffDeepWriter.ToBytes(original);
    var restored = IffDeepReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.HasAlpha, Is.False);
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba_SmallImage() {
    var pixelData = new byte[4 * 3 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new IffDeepFile {
      Width = 4,
      Height = 3,
      HasAlpha = true,
      Compression = IffDeepCompression.None,
      PixelData = pixelData
    };

    var bytes = IffDeepWriter.ToBytes(original);
    var restored = IffDeepReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.HasAlpha, Is.True);
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new IffDeepFile {
      Width = 8,
      Height = 8,
      HasAlpha = false,
      Compression = IffDeepCompression.None,
      PixelData = new byte[8 * 8 * 3]
    };

    var bytes = IffDeepWriter.ToBytes(original);
    var restored = IffDeepReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(8));
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
        pixelData[idx + 2] = 128;
      }

    var original = new IffDeepFile {
      Width = width,
      Height = height,
      HasAlpha = false,
      Compression = IffDeepCompression.None,
      PixelData = pixelData
    };

    var bytes = IffDeepWriter.ToBytes(original);
    var restored = IffDeepReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[8 * 6 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new IffDeepFile {
      Width = 8,
      Height = 6,
      HasAlpha = false,
      Compression = IffDeepCompression.None,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".deep");
    try {
      var bytes = IffDeepWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = IffDeepReader.FromFile(new FileInfo(tempPath));

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
  public void RoundTrip_ViaRawImage_Rgb24() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var rawImage = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Rgb24,
      PixelData = pixelData
    };

    var deepFile = IffDeepFile.FromRawImage(rawImage);
    var roundTripped = IffDeepFile.ToRawImage(deepFile);

    Assert.That(roundTripped.Width, Is.EqualTo(4));
    Assert.That(roundTripped.Height, Is.EqualTo(4));
    Assert.That(roundTripped.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(roundTripped.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgba32() {
    var pixelData = new byte[4 * 4 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var rawImage = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Rgba32,
      PixelData = pixelData
    };

    var deepFile = IffDeepFile.FromRawImage(rawImage);
    var roundTripped = IffDeepFile.ToRawImage(deepFile);

    Assert.That(roundTripped.Width, Is.EqualTo(4));
    Assert.That(roundTripped.Height, Is.EqualTo(4));
    Assert.That(roundTripped.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(roundTripped.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixel() {
    var original = new IffDeepFile {
      Width = 1,
      Height = 1,
      HasAlpha = false,
      Compression = IffDeepCompression.None,
      PixelData = [0xFF, 0x00, 0x80]
    };

    var bytes = IffDeepWriter.ToBytes(original);
    var restored = IffDeepReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var width = 64;
    var height = 48;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new IffDeepFile {
      Width = width,
      Height = height,
      HasAlpha = false,
      Compression = IffDeepCompression.None,
      PixelData = pixelData
    };

    var bytes = IffDeepWriter.ToBytes(original);
    var restored = IffDeepReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RleCompression_Rgb() {
    var pixelData = new byte[8 * 8 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 5 % 256);

    var original = new IffDeepFile {
      Width = 8,
      Height = 8,
      HasAlpha = false,
      Compression = IffDeepCompression.Rle,
      PixelData = pixelData
    };

    var bytes = IffDeepWriter.ToBytes(original);
    var restored = IffDeepReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Compression, Is.EqualTo(IffDeepCompression.Rle));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_RleCompression_Rgba() {
    var pixelData = new byte[4 * 4 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var original = new IffDeepFile {
      Width = 4,
      Height = 4,
      HasAlpha = true,
      Compression = IffDeepCompression.Rle,
      PixelData = pixelData
    };

    var bytes = IffDeepWriter.ToBytes(original);
    var restored = IffDeepReader.FromBytes(bytes);

    Assert.That(restored.HasAlpha, Is.True);
    Assert.That(restored.Compression, Is.EqualTo(IffDeepCompression.Rle));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CompressionPreserved() {
    var original = new IffDeepFile {
      Width = 2,
      Height = 2,
      HasAlpha = false,
      Compression = IffDeepCompression.Rle,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = IffDeepWriter.ToBytes(original);
    var restored = IffDeepReader.FromBytes(bytes);

    Assert.That(restored.Compression, Is.EqualTo(IffDeepCompression.Rle));
  }
}
