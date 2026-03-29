using System;
using System.Buffers.Binary;
using FileFormat.CokeAtari;

namespace FileFormat.CokeAtari.Tests;

[TestFixture]
public sealed class CokeAtariWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithDimensionsBigEndian() {
    var file = new CokeAtariFile {
      Width = 320,
      Height = 200,
      PixelData = new byte[320 * 200 * 2],
    };

    var bytes = CokeAtariWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0));
    var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2));

    Assert.That(width, Is.EqualTo(320));
    Assert.That(height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSizeCorrect() {
    var file = new CokeAtariFile {
      Width = 16,
      Height = 8,
      PixelData = new byte[16 * 8 * 2],
    };

    var bytes = CokeAtariWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(CokeAtariHeader.StructSize + 16 * 8 * 2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[4 * 2 * 2];
    pixelData[0] = 0xF8;
    pixelData[1] = 0x00;
    pixelData[pixelData.Length - 2] = 0x07;
    pixelData[pixelData.Length - 1] = 0xE0;

    var file = new CokeAtariFile {
      Width = 4,
      Height = 2,
      PixelData = pixelData,
    };

    var bytes = CokeAtariWriter.ToBytes(file);

    Assert.That(bytes[CokeAtariHeader.StructSize], Is.EqualTo(0xF8));
    Assert.That(bytes[CokeAtariHeader.StructSize + 1], Is.EqualTo(0x00));
    Assert.That(bytes[bytes.Length - 2], Is.EqualTo(0x07));
    Assert.That(bytes[bytes.Length - 1], Is.EqualTo(0xE0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ShortPixelData_PadsWithZeros() {
    var file = new CokeAtariFile {
      Width = 4,
      Height = 2,
      PixelData = new byte[2],
    };

    var bytes = CokeAtariWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(CokeAtariHeader.StructSize + 4 * 2 * 2));
    Assert.That(bytes[CokeAtariHeader.StructSize + 4], Is.EqualTo(0));
  }
}
