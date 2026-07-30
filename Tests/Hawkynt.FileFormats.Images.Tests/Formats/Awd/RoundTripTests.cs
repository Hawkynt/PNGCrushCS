using System;
using System.IO;
using FileFormat.Awd;

namespace FileFormat.Awd.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllWhite() {
    var original = new AwdFile {
      Width = 8,
      Height = 2,
      PixelData = new byte[] { 0xFF, 0xFF },
    };

    var bytes = AwdWriter.ToBytes(original);
    var restored = AwdReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBlack() {
    var original = new AwdFile {
      Width = 8,
      Height = 2,
      PixelData = new byte[] { 0x00, 0x00 },
    };

    var bytes = AwdWriter.ToBytes(original);
    var restored = AwdReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Checkerboard() {
    var original = new AwdFile {
      Width = 8,
      Height = 4,
      PixelData = new byte[] { 0xAA, 0x55, 0xAA, 0x55 },
    };

    var bytes = AwdWriter.ToBytes(original);
    var restored = AwdReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NonByteAlignedWidth() {
    // 5 pixels wide = ceil(5/8) = 1 byte per row, 3 rows
    var original = new AwdFile {
      Width = 5,
      Height = 3,
      PixelData = new byte[] { 0b11010000, 0b10100000, 0b01110000 },
    };

    var bytes = AwdWriter.ToBytes(original);
    var restored = AwdReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(5));
    Assert.That(restored.Height, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = new AwdFile {
      Width = 16,
      Height = 4,
      PixelData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE },
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".awd");
    try {
      var bytes = AwdWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = AwdReader.FromFile(new FileInfo(tempPath));

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
  public void RoundTrip_LargeImage() {
    var width = 640;
    var height = 480;
    var bytesPerRow = (width + 7) / 8; // 80
    var pixelData = new byte[bytesPerRow * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new AwdFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var bytes = AwdWriter.ToBytes(original);
    var restored = AwdReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
