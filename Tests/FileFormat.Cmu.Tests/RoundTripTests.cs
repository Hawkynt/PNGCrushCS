using System;
using System.IO;
using FileFormat.Cmu;

namespace FileFormat.Cmu.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_8x8() {
    var original = new CmuFile {
      Width = 8,
      Height = 8,
      PixelData = new byte[] { 0xFF, 0x00, 0xAA, 0x55, 0xF0, 0x0F, 0xCC, 0x33 }
    };

    var bytes = CmuWriter.ToBytes(original);
    var restored = CmuReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NonByteAlignedWidth() {
    // 5 pixels wide = ceil(5/8) = 1 byte per row, 3 rows
    var original = new CmuFile {
      Width = 5,
      Height = 3,
      PixelData = new byte[] { 0b11010000, 0b10100000, 0b01110000 }
    };

    var bytes = CmuWriter.ToBytes(original);
    var restored = CmuReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(5));
    Assert.That(restored.Height, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
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

    var original = new CmuFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = CmuWriter.ToBytes(original);
    var restored = CmuReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = new CmuFile {
      Width = 16,
      Height = 4,
      PixelData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE }
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cmu");
    try {
      var bytes = CmuWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = CmuReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
