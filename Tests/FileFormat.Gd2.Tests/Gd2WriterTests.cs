using System;
using System.Buffers.Binary;
using FileFormat.Gd2;

namespace FileFormat.Gd2.Tests;

[TestFixture]
public sealed class Gd2WriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => Gd2Writer.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithGd2Signature() {
    var file = new Gd2File {
      Width = 1,
      Height = 1,
      ChunkSize = 1,
      PixelData = new byte[4],
    };

    var bytes = Gd2Writer.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0x67)); // 'g'
    Assert.That(bytes[1], Is.EqualTo(0x64)); // 'd'
    Assert.That(bytes[2], Is.EqualTo(0x32)); // '2'
    Assert.That(bytes[3], Is.EqualTo(0x00)); // '\0'
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesDimensionsBigEndian() {
    var file = new Gd2File {
      Width = 0x0102,
      Height = 0x0304,
      ChunkSize = 0x0304,
      PixelData = new byte[0x0102 * 0x0304 * 4],
    };

    var bytes = Gd2Writer.ToBytes(file);

    Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(6)), Is.EqualTo(0x0102));
    Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(8)), Is.EqualTo(0x0304));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesVersionBigEndian() {
    var file = new Gd2File {
      Width = 1,
      Height = 1,
      Version = 2,
      ChunkSize = 1,
      PixelData = new byte[4],
    };

    var bytes = Gd2Writer.ToBytes(file);

    Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4)), Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesChunkSizeBigEndian() {
    var file = new Gd2File {
      Width = 2,
      Height = 3,
      ChunkSize = 7,
      PixelData = new byte[2 * 3 * 4],
    };

    var bytes = Gd2Writer.ToBytes(file);

    Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(10)), Is.EqualTo(7));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesFormatBigEndian() {
    var file = new Gd2File {
      Width = 1,
      Height = 1,
      ChunkSize = 1,
      Format = 1,
      PixelData = new byte[4],
    };

    var bytes = Gd2Writer.ToBytes(file);

    Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(12)), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesChunkCounts() {
    var file = new Gd2File {
      Width = 10,
      Height = 5,
      ChunkSize = 4,
      PixelData = new byte[10 * 5 * 4],
    };

    var bytes = Gd2Writer.ToBytes(file);

    // xChunkCount = ceil(10/4) = 3
    Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(14)), Is.EqualTo(3));
    // yChunkCount = ceil(5/4) = 2
    Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(16)), Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSizeMatchesHeaderPlusPixels() {
    var file = new Gd2File {
      Width = 3,
      Height = 2,
      ChunkSize = 3,
      PixelData = new byte[3 * 2 * 4],
    };

    var bytes = Gd2Writer.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(Gd2File.HeaderSize + 3 * 2 * 4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixels = new byte[4];
    pixels[0] = 0x00; // A=opaque
    pixels[1] = 0xFF; // R
    pixels[2] = 0x80; // G
    pixels[3] = 0x40; // B

    var file = new Gd2File {
      Width = 1,
      Height = 1,
      ChunkSize = 1,
      PixelData = pixels,
    };

    var bytes = Gd2Writer.ToBytes(file);

    Assert.That(bytes[18], Is.EqualTo(0x00));
    Assert.That(bytes[19], Is.EqualTo(0xFF));
    Assert.That(bytes[20], Is.EqualTo(0x80));
    Assert.That(bytes[21], Is.EqualTo(0x40));
  }
}
