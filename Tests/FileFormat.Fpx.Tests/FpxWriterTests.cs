using System;
using System.Buffers.Binary;
using FileFormat.Fpx;

namespace FileFormat.Fpx.Tests;

[TestFixture]
public sealed class FpxWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FpxWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesMagic() {
    var file = new FpxFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3]
    };

    var bytes = FpxWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'F'));
    Assert.That(bytes[1], Is.EqualTo((byte)'P'));
    Assert.That(bytes[2], Is.EqualTo((byte)'X'));
    Assert.That(bytes[3], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesVersion1() {
    var file = new FpxFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3]
    };

    var bytes = FpxWriter.ToBytes(file);
    var version = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));

    Assert.That(version, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesReservedAsZero() {
    var file = new FpxFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3]
    };

    var bytes = FpxWriter.ToBytes(file);
    var reserved = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6));

    Assert.That(reserved, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesCorrectDimensions() {
    var file = new FpxFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 3]
    };

    var bytes = FpxWriter.ToBytes(file);

    var width = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
    var height = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
    Assert.That(width, Is.EqualTo(320u));
    Assert.That(height, Is.EqualTo(240u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSizeCorrect() {
    var file = new FpxFile {
      Width = 4,
      Height = 2,
      PixelData = new byte[4 * 2 * 3]
    };

    var bytes = FpxWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(FpxHeader.StructSize + 4 * 2 * 3));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new FpxFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    var bytes = FpxWriter.ToBytes(file);

    Assert.That(bytes[FpxHeader.StructSize], Is.EqualTo(0xAA));
    Assert.That(bytes[FpxHeader.StructSize + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[FpxHeader.StructSize + 2], Is.EqualTo(0xCC));
  }
}
