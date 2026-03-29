using System;
using System.Buffers.Binary;
using FileFormat.Cmu;

namespace FileFormat.Cmu.Tests;

[TestFixture]
public sealed class CmuWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithDimensions() {
    var file = new CmuFile {
      Width = 16,
      Height = 4,
      PixelData = new byte[8] // 2 bytes per row * 4 rows
    };

    var bytes = CmuWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0));
    var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4));

    Assert.That(width, Is.EqualTo(16));
    Assert.That(height, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSizeCorrect() {
    // 16x4: bytesPerRow=2, pixelData=8 bytes, total=8+8=16
    var file = new CmuFile {
      Width = 16,
      Height = 4,
      PixelData = new byte[8]
    };

    var bytes = CmuWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[] { 0b10110100, 0b01001011 };
    var file = new CmuFile {
      Width = 8,
      Height = 2,
      PixelData = pixelData
    };

    var bytes = CmuWriter.ToBytes(file);

    Assert.That(bytes[8], Is.EqualTo(0b10110100));
    Assert.That(bytes[9], Is.EqualTo(0b01001011));
  }
}
