using System;
using System.Buffers.Binary;
using FileFormat.GemImg;

namespace FileFormat.GemImg.Tests;

[TestFixture]
public sealed class GemImgWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_ValidMonochrome_StartsWithValidHeader() {
    var file = new GemImgFile {
      Version = 1,
      Width = 16,
      Height = 2,
      NumPlanes = 1,
      PatternLength = 1,
      PixelWidth = 85,
      PixelHeight = 85,
      PixelData = new byte[2 * 2] // 16 px = 2 bytes/row, 2 rows
    };

    var bytes = GemImgWriter.ToBytes(file);

    // Version at offset 0 (big-endian short)
    var version = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(0));
    Assert.That(version, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectDimensionsInHeader() {
    var file = new GemImgFile {
      Version = 1,
      Width = 32,
      Height = 10,
      NumPlanes = 1,
      PatternLength = 2,
      PixelWidth = 85,
      PixelHeight = 85,
      PixelData = new byte[4 * 10] // 32 px = 4 bytes/row, 10 rows
    };

    var bytes = GemImgWriter.ToBytes(file);

    var scanWidth = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(12));
    var scanLines = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(14));
    Assert.That(scanWidth, Is.EqualTo(32));
    Assert.That(scanLines, Is.EqualTo(10));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderLength_IsCorrect() {
    var file = new GemImgFile {
      Version = 1,
      Width = 8,
      Height = 1,
      NumPlanes = 1,
      PatternLength = 1,
      PixelWidth = 85,
      PixelHeight = 85,
      PixelData = new byte[1]
    };

    var bytes = GemImgWriter.ToBytes(file);

    var headerLength = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(2));
    Assert.That(headerLength, Is.EqualTo(GemImgHeader.StructSize / 2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_NumPlanes_IsWritten() {
    var file = new GemImgFile {
      Version = 1,
      Width = 8,
      Height = 1,
      NumPlanes = 4,
      PatternLength = 1,
      PixelWidth = 85,
      PixelHeight = 85,
      PixelData = new byte[4] // 4 planes x 1 byte/row x 1 row
    };

    var bytes = GemImgWriter.ToBytes(file);

    var numPlanes = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(4));
    Assert.That(numPlanes, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PatternLength_IsWritten() {
    var file = new GemImgFile {
      Version = 1,
      Width = 8,
      Height = 1,
      NumPlanes = 1,
      PatternLength = 4,
      PixelWidth = 85,
      PixelHeight = 85,
      PixelData = new byte[1]
    };

    var bytes = GemImgWriter.ToBytes(file);

    var patternLength = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(6));
    Assert.That(patternLength, Is.EqualTo(4));
  }
}
