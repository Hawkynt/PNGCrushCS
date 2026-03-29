using System;
using System.IO;
using FileFormat.Msp;

namespace FileFormat.Msp.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_V1_8x8() {
    var bytesPerRow = 1; // 8 pixels = 1 byte per row
    var pixelData = new byte[bytesPerRow * 8];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 37 % 256);

    var original = new MspFile {
      Width = 8,
      Height = 8,
      Version = MspVersion.V1,
      PixelData = pixelData
    };

    var bytes = MspWriter.ToBytes(original);
    var restored = MspReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Version, Is.EqualTo(MspVersion.V1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_V1_NonAlignedWidth() {
    // 13 pixels wide = ceil(13/8) = 2 bytes per row
    var bytesPerRow = 2;
    var pixelData = new byte[bytesPerRow * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new MspFile {
      Width = 13,
      Height = 4,
      Version = MspVersion.V1,
      PixelData = pixelData
    };

    var bytes = MspWriter.ToBytes(original);
    var restored = MspReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(13));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.Version, Is.EqualTo(MspVersion.V1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_V2_WithRle() {
    var bytesPerRow = 4; // 32 pixels wide
    var pixelData = new byte[bytesPerRow * 4];
    // Create data with runs for good RLE compression
    for (var y = 0; y < 4; ++y)
      for (var x = 0; x < bytesPerRow; ++x)
        pixelData[y * bytesPerRow + x] = (byte)(y % 2 == 0 ? 0xFF : 0x00);

    var original = new MspFile {
      Width = 32,
      Height = 4,
      Version = MspVersion.V2,
      PixelData = pixelData
    };

    var bytes = MspWriter.ToBytes(original);
    var restored = MspReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Version, Is.EqualTo(MspVersion.V2));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_V1_ViaFile() {
    var pixelData = new byte[] { 0b10101010 };
    var original = new MspFile {
      Width = 8,
      Height = 1,
      Version = MspVersion.V1,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".msp");
    try {
      var bytes = MspWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = MspReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(8));
      Assert.That(restored.Height, Is.EqualTo(1));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
