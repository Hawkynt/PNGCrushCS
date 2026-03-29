using System;
using System.IO;
using FileFormat.Otb;

namespace FileFormat.Otb.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SmallImage() {
    var original = new OtbFile {
      Width = 8,
      Height = 8,
      PixelData = new byte[] { 0xFF, 0x00, 0xAA, 0x55, 0xF0, 0x0F, 0xCC, 0x33 }
    };

    var bytes = OtbWriter.ToBytes(original);
    var restored = OtbReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NonByteAlignedWidth() {
    // 7 pixels wide = ceil(7/8) = 1 byte per row, 5 rows
    var original = new OtbFile {
      Width = 7,
      Height = 5,
      PixelData = new byte[] { 0b11010000, 0b10100000, 0b01110000, 0b11111110, 0b00000010 }
    };

    var bytes = OtbWriter.ToBytes(original);
    var restored = OtbReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(7));
    Assert.That(restored.Height, Is.EqualTo(5));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MaxSize() {
    var width = 255;
    var height = 255;
    var bytesPerRow = (width + 7) / 8; // 32
    var pixelData = new byte[bytesPerRow * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new OtbFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = OtbWriter.ToBytes(original);
    var restored = OtbReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixel() {
    // 1x1: 1 byte per row, 1 row
    var original = new OtbFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[] { 0x80 }
    };

    var bytes = OtbWriter.ToBytes(original);
    var restored = OtbReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = new OtbFile {
      Width = 16,
      Height = 4,
      PixelData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE }
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".otb");
    try {
      var bytes = OtbWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = OtbReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
