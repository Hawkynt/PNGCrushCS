using System;
using System.IO;
using FileFormat.Core;
using FileFormat.JpegLs;

namespace FileFormat.JpegLs.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale_2x2() {
    var pixelData = new byte[] { 10, 20, 30, 40 };
    var original = new JpegLsFile {
      Width = 2,
      Height = 2,
      BitsPerSample = 8,
      ComponentCount = 1,
      PixelData = pixelData
    };

    var bytes = JpegLsWriter.ToBytes(original);
    var restored = JpegLsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.ComponentCount, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale_AllZeros() {
    var pixelData = new byte[4 * 4];
    var original = new JpegLsFile {
      Width = 4,
      Height = 4,
      BitsPerSample = 8,
      ComponentCount = 1,
      PixelData = pixelData
    };

    var bytes = JpegLsWriter.ToBytes(original);
    var restored = JpegLsReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale_Gradient() {
    var width = 8;
    var height = 4;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new JpegLsFile {
      Width = width,
      Height = height,
      BitsPerSample = 8,
      ComponentCount = 1,
      PixelData = pixelData
    };

    var bytes = JpegLsWriter.ToBytes(original);
    var restored = JpegLsReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_2x2() {
    var pixelData = new byte[] {
      255, 0, 0,     // red
      0, 255, 0,     // green
      0, 0, 255,     // blue
      255, 255, 0    // yellow
    };

    var original = new JpegLsFile {
      Width = 2,
      Height = 2,
      BitsPerSample = 8,
      ComponentCount = 3,
      PixelData = pixelData
    };

    var bytes = JpegLsWriter.ToBytes(original);
    var restored = JpegLsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.ComponentCount, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb_Gradient() {
    var width = 8;
    var height = 4;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new JpegLsFile {
      Width = width,
      Height = height,
      BitsPerSample = 8,
      ComponentCount = 3,
      PixelData = pixelData
    };

    var bytes = JpegLsWriter.ToBytes(original);
    var restored = JpegLsReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale_1x1() {
    var original = new JpegLsFile {
      Width = 1,
      Height = 1,
      BitsPerSample = 8,
      ComponentCount = 1,
      PixelData = [128]
    };

    var bytes = JpegLsWriter.ToBytes(original);
    var restored = JpegLsReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale_UniformValue() {
    var pixelData = new byte[16];
    Array.Fill(pixelData, (byte)128);

    var original = new JpegLsFile {
      Width = 4,
      Height = 4,
      BitsPerSample = 8,
      ComponentCount = 1,
      PixelData = pixelData
    };

    var bytes = JpegLsWriter.ToBytes(original);
    var restored = JpegLsReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[] { 50, 100, 150, 200 };
    var original = new JpegLsFile {
      Width = 2,
      Height = 2,
      BitsPerSample = 8,
      ComponentCount = 1,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jls");
    try {
      var bytes = JpegLsWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = JpegLsReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(2));
      Assert.That(restored.Height, Is.EqualTo(2));
      Assert.That(restored.PixelData, Is.EqualTo(pixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Grayscale() {
    var rawImage = new RawImage {
      Width = 3,
      Height = 2,
      Format = PixelFormat.Gray8,
      PixelData = [10, 20, 30, 40, 50, 60]
    };

    var jlsFile = JpegLsFile.FromRawImage(rawImage);
    var bytes = JpegLsWriter.ToBytes(jlsFile);
    var restored = JpegLsReader.FromBytes(bytes);
    var restoredRaw = JpegLsFile.ToRawImage(restored);

    Assert.That(restoredRaw.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(restoredRaw.PixelData, Is.EqualTo(rawImage.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb() {
    var rawImage = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Rgb24,
      PixelData = [255, 0, 0, 0, 255, 0, 0, 0, 255, 128, 128, 128]
    };

    var jlsFile = JpegLsFile.FromRawImage(rawImage);
    var bytes = JpegLsWriter.ToBytes(jlsFile);
    var restored = JpegLsReader.FromBytes(bytes);
    var restoredRaw = JpegLsFile.ToRawImage(restored);

    Assert.That(restoredRaw.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(restoredRaw.PixelData, Is.EqualTo(rawImage.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale_LargerImage() {
    var width = 32;
    var height = 32;
    var pixelData = new byte[width * height];
    var rng = new Random(42);
    rng.NextBytes(pixelData);

    var original = new JpegLsFile {
      Width = width,
      Height = height,
      BitsPerSample = 8,
      ComponentCount = 1,
      PixelData = pixelData
    };

    var bytes = JpegLsWriter.ToBytes(original);
    var restored = JpegLsReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale_AllWhite() {
    var pixelData = new byte[8 * 8];
    Array.Fill(pixelData, (byte)255);

    var original = new JpegLsFile {
      Width = 8,
      Height = 8,
      BitsPerSample = 8,
      ComponentCount = 1,
      PixelData = pixelData
    };

    var bytes = JpegLsWriter.ToBytes(original);
    var restored = JpegLsReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaStream() {
    var pixelData = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80, 90 };
    var original = new JpegLsFile {
      Width = 3,
      Height = 3,
      BitsPerSample = 8,
      ComponentCount = 1,
      PixelData = pixelData
    };

    var bytes = JpegLsWriter.ToBytes(original);
    using var ms = new MemoryStream(bytes);
    var restored = JpegLsReader.FromStream(ms);

    Assert.That(restored.PixelData, Is.EqualTo(pixelData));
  }
}
