using System;
using System.IO;
using FileFormat.Analyze;
using FileFormat.Core;

namespace FileFormat.Analyze.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale() {
    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new AnalyzeFile {
      Width = 4,
      Height = 3,
      DataType = AnalyzeDataType.UInt8,
      BitsPerPixel = 8,
      PixelData = pixelData
    };

    var bytes = AnalyzeWriter.ToBytes(original);
    var restored = AnalyzeReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.DataType, Is.EqualTo(original.DataType));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(original.BitsPerPixel));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24() {
    var pixelData = new byte[2 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new AnalyzeFile {
      Width = 2,
      Height = 2,
      DataType = AnalyzeDataType.Rgb24,
      BitsPerPixel = 24,
      PixelData = pixelData
    };

    var bytes = AnalyzeWriter.ToBytes(original);
    var restored = AnalyzeReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.DataType, Is.EqualTo(original.DataType));
    Assert.That(restored.BitsPerPixel, Is.EqualTo(original.BitsPerPixel));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var width = 64;
    var height = 48;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new AnalyzeFile {
      Width = width,
      Height = height,
      DataType = AnalyzeDataType.UInt8,
      BitsPerPixel = 8,
      PixelData = pixelData
    };

    var bytes = AnalyzeWriter.ToBytes(original);
    var restored = AnalyzeReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[3 * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 23 % 256);

    var original = new AnalyzeFile {
      Width = 3,
      Height = 2,
      DataType = AnalyzeDataType.UInt8,
      BitsPerPixel = 8,
      PixelData = pixelData
    };

    var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(tempDir);
    try {
      var hdrPath = Path.Combine(tempDir, "test.hdr");
      var imgPath = Path.Combine(tempDir, "test.img");

      var bytes = AnalyzeWriter.ToBytes(original);
      File.WriteAllBytes(hdrPath, bytes[..348]);
      File.WriteAllBytes(imgPath, bytes[348..]);

      var restored = AnalyzeReader.FromFile(new FileInfo(hdrPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.DataType, Is.EqualTo(original.DataType));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      Directory.Delete(tempDir, true);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Gray8() {
    var raw = new RawImage {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Gray8,
      PixelData = [0x10, 0x20, 0x30, 0x40]
    };

    var file = AnalyzeFile.FromRawImage(raw);
    var restored = AnalyzeFile.ToRawImage(file);

    Assert.That(restored.Width, Is.EqualTo(raw.Width));
    Assert.That(restored.Height, Is.EqualTo(raw.Height));
    Assert.That(restored.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(restored.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage_Rgb24() {
    var raw = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [0xAA, 0xBB, 0xCC]
    };

    var file = AnalyzeFile.FromRawImage(raw);
    var restored = AnalyzeFile.ToRawImage(file);

    Assert.That(restored.Width, Is.EqualTo(raw.Width));
    Assert.That(restored.Height, Is.EqualTo(raw.Height));
    Assert.That(restored.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(restored.PixelData, Is.EqualTo(raw.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new AnalyzeFile {
      Width = 4,
      Height = 4,
      DataType = AnalyzeDataType.UInt8,
      BitsPerPixel = 8,
      PixelData = new byte[16]
    };

    var bytes = AnalyzeWriter.ToBytes(original);
    var restored = AnalyzeReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
