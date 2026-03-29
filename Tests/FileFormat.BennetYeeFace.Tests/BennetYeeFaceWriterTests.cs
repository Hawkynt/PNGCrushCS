using System;
using System.Buffers.Binary;
using FileFormat.BennetYeeFace;

namespace FileFormat.BennetYeeFace.Tests;

[TestFixture]
public sealed class BennetYeeFaceWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => BennetYeeFaceWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderDimensions() {
    var file = new BennetYeeFaceFile {
      Width = 32,
      Height = 4,
      PixelData = new byte[16] // stride=4, 4 rows
    };

    var bytes = BennetYeeFaceWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0));
    var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2));

    Assert.That(width, Is.EqualTo(32));
    Assert.That(height, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSizeCorrect() {
    // 16x4: stride=2, 4 rows = 8 bytes pixel data, total = 4 + 8 = 12
    var file = new BennetYeeFaceFile {
      Width = 16,
      Height = 4,
      PixelData = new byte[8]
    };

    var bytes = BennetYeeFaceWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WordPaddedStride() {
    // 5 pixels wide: stride = ((5+15)/16)*2 = 2 bytes, 1 row
    // total = 4 + 2 = 6
    var file = new BennetYeeFaceFile {
      Width = 5,
      Height = 1,
      PixelData = new byte[2]
    };

    var bytes = BennetYeeFaceWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[] { 0b10110100, 0x00, 0b01001011, 0x00 };
    var file = new BennetYeeFaceFile {
      Width = 8,
      Height = 2,
      PixelData = pixelData
    };

    var bytes = BennetYeeFaceWriter.ToBytes(file);

    Assert.That(bytes[4], Is.EqualTo(0b10110100));
    Assert.That(bytes[5], Is.EqualTo(0x00));
    Assert.That(bytes[6], Is.EqualTo(0b01001011));
    Assert.That(bytes[7], Is.EqualTo(0x00));
  }
}
