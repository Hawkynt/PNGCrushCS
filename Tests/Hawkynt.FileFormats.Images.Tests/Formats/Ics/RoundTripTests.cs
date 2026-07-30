using System;
using System.IO;
using FileFormat.Ics;

namespace FileFormat.Ics.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale8_Uncompressed() {
    var pixelData = new byte[4 * 3]; // 4x3 grayscale
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new IcsFile {
      Width = 4,
      Height = 3,
      Channels = 1,
      BitsPerSample = 8,
      Compression = IcsCompression.Uncompressed,
      PixelData = pixelData
    };

    var bytes = IcsWriter.ToBytes(original);
    var restored = IcsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Channels, Is.EqualTo(1));
    Assert.That(restored.BitsPerSample, Is.EqualTo(8));
    Assert.That(restored.Compression, Is.EqualTo(IcsCompression.Uncompressed));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb8_Uncompressed() {
    var pixelData = new byte[3 * 2 * 3]; // 3x2 RGB (3 channels)
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new IcsFile {
      Width = 3,
      Height = 2,
      Channels = 3,
      BitsPerSample = 8,
      Compression = IcsCompression.Uncompressed,
      PixelData = pixelData
    };

    var bytes = IcsWriter.ToBytes(original);
    var restored = IcsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(3));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.Channels, Is.EqualTo(3));
    Assert.That(restored.BitsPerSample, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale8_Gzip() {
    var pixelData = new byte[8 * 8]; // 8x8 grayscale
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new IcsFile {
      Width = 8,
      Height = 8,
      Channels = 1,
      BitsPerSample = 8,
      Compression = IcsCompression.Gzip,
      PixelData = pixelData
    };

    var bytes = IcsWriter.ToBytes(original);
    var restored = IcsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(8));
    Assert.That(restored.Channels, Is.EqualTo(1));
    Assert.That(restored.Compression, Is.EqualTo(IcsCompression.Gzip));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb8_Gzip() {
    var pixelData = new byte[4 * 4 * 3]; // 4x4 RGB
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new IcsFile {
      Width = 4,
      Height = 4,
      Channels = 3,
      BitsPerSample = 8,
      Compression = IcsCompression.Gzip,
      PixelData = pixelData
    };

    var bytes = IcsWriter.ToBytes(original);
    var restored = IcsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.Channels, Is.EqualTo(3));
    Assert.That(restored.Compression, Is.EqualTo(IcsCompression.Gzip));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[5 * 4]; // 5x4 grayscale
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var original = new IcsFile {
      Width = 5,
      Height = 4,
      Channels = 1,
      BitsPerSample = 8,
      Compression = IcsCompression.Uncompressed,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ics");
    try {
      var bytes = IcsWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = IcsReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Channels, Is.EqualTo(original.Channels));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    var width = 64;
    var height = 32;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new IcsFile {
      Width = width,
      Height = height,
      Channels = 3,
      BitsPerSample = 8,
      Compression = IcsCompression.Uncompressed,
      PixelData = pixelData
    };

    var bytes = IcsWriter.ToBytes(original);
    var restored = IcsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_VersionPreserved() {
    var original = new IcsFile {
      Version = "2.0",
      Width = 2,
      Height = 2,
      Channels = 1,
      BitsPerSample = 8,
      PixelData = new byte[4]
    };

    var bytes = IcsWriter.ToBytes(original);
    var restored = IcsReader.FromBytes(bytes);

    Assert.That(restored.Version, Is.EqualTo("2.0"));
  }
}
