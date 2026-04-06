using System;
using System.IO;
using FileFormat.BennetYeeFace;

namespace FileFormat.BennetYeeFace.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_16x8() {
    var original = new BennetYeeFaceFile {
      Width = 16,
      Height = 8,
      PixelData = new byte[] {
        0xFF, 0x00, 0x00, 0xFF, 0xAA, 0x55, 0x55, 0xAA,
        0xF0, 0x0F, 0x0F, 0xF0, 0xCC, 0x33, 0x33, 0xCC
      }
    };

    var bytes = BennetYeeFaceWriter.ToBytes(original);
    var restored = BennetYeeFaceReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NonWordAlignedWidth() {
    // 5 pixels wide: stride = ((5+15)/16)*2 = 2 bytes per row, 3 rows
    var original = new BennetYeeFaceFile {
      Width = 5,
      Height = 3,
      PixelData = new byte[] { 0b11010000, 0x00, 0b10100000, 0x00, 0b01110000, 0x00 }
    };

    var bytes = BennetYeeFaceWriter.ToBytes(original);
    var restored = BennetYeeFaceReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(5));
    Assert.That(restored.Height, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    var width = 640;
    var height = 480;
    var stride = ((width + 15) / 16) * 2; // 80
    var pixelData = new byte[stride * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new BennetYeeFaceFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = BennetYeeFaceWriter.ToBytes(original);
    var restored = BennetYeeFaceReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixel() {
    // 1x1: stride = ((1+15)/16)*2 = 2 bytes
    var original = new BennetYeeFaceFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[] { 0x80, 0x00 }
    };

    var bytes = BennetYeeFaceWriter.ToBytes(original);
    var restored = BennetYeeFaceReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = new BennetYeeFaceFile {
      Width = 32,
      Height = 2,
      PixelData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE }
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ybm");
    try {
      var bytes = BennetYeeFaceWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = BennetYeeFaceReader.FromFile(new FileInfo(tempPath));

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
  public void RoundTrip_ViaRawImage() {
    var original = new BennetYeeFaceFile {
      Width = 16,
      Height = 2,
      PixelData = new byte[] { 0xFF, 0x00, 0xAA, 0x55 }
    };

    var raw = BennetYeeFaceFile.ToRawImage(original);
    var restored = BennetYeeFaceFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
  }
}
