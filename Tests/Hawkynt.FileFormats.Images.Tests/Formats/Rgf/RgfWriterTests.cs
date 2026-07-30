using System;
using FileFormat.Rgf;

namespace FileFormat.Rgf.Tests;

[TestFixture]
public sealed class RgfWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderContainsDimensions() {
    var file = new RgfFile {
      Width = 16,
      Height = 4,
      PixelData = new byte[8] // 2 bytes per row * 4 rows
    };

    var bytes = RgfWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(16));
    Assert.That(bytes[1], Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TotalLength() {
    // 16x4: bytesPerRow=2, pixelData=8 bytes, total=2+8=10
    var file = new RgfFile {
      Width = 16,
      Height = 4,
      PixelData = new byte[8]
    };

    var bytes = RgfWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(10));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[] { 0b10110100, 0b01001011 };
    var file = new RgfFile {
      Width = 8,
      Height = 2,
      PixelData = pixelData
    };

    var bytes = RgfWriter.ToBytes(file);

    Assert.That(bytes[2], Is.EqualTo(0b10110100));
    Assert.That(bytes[3], Is.EqualTo(0b01001011));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MaxDimensions() {
    // Max width=178, max height=128
    var bytesPerRow = (178 + 7) / 8; // 23
    var pixelData = new byte[bytesPerRow * 128];
    var file = new RgfFile {
      Width = 178,
      Height = 128,
      PixelData = pixelData
    };

    var bytes = RgfWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(178));
    Assert.That(bytes[1], Is.EqualTo(128));
    Assert.That(bytes.Length, Is.EqualTo(2 + bytesPerRow * 128));
  }
}
