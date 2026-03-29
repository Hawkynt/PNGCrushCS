using System;
using System.IO;
using FileFormat.Rgf;

namespace FileFormat.Rgf.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_8x8() {
    var original = new RgfFile {
      Width = 8,
      Height = 8,
      PixelData = new byte[] { 0xFF, 0x00, 0xAA, 0x55, 0xF0, 0x0F, 0xCC, 0x33 }
    };

    var bytes = RgfWriter.ToBytes(original);
    var restored = RgfReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new RgfFile {
      Width = 16,
      Height = 8,
      PixelData = new byte[16] // 2 bytes per row * 8 rows, all zeros
    };

    var bytes = RgfWriter.ToBytes(original);
    var restored = RgfReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = new RgfFile {
      Width = 16,
      Height = 4,
      PixelData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE }
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rgf");
    try {
      var bytes = RgfWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = RgfReader.FromFile(new FileInfo(tempPath));

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
  public void RoundTrip_NonByteAlignedWidth() {
    // 10 pixels wide = ceil(10/8) = 2 bytes per row, 3 rows
    var original = new RgfFile {
      Width = 10,
      Height = 3,
      PixelData = new byte[] { 0b11010000, 0b11000000, 0b10100000, 0b10000000, 0b01110000, 0b01000000 }
    };

    var bytes = RgfWriter.ToBytes(original);
    var restored = RgfReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(10));
    Assert.That(restored.Height, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MaxDimensions() {
    var width = 178;
    var height = 128;
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new RgfFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = RgfWriter.ToBytes(original);
    var restored = RgfReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
