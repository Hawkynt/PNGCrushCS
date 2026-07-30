using System;
using System.Buffers.Binary;
using FileFormat.Msp;

namespace FileFormat.Msp.Tests;

[TestFixture]
public sealed class MspWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_V1_StartsWithV1Magic() {
    var file = new MspFile {
      Width = 8,
      Height = 1,
      Version = MspVersion.V1,
      PixelData = new byte[1]
    };

    var bytes = MspWriter.ToBytes(file);

    Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0)), Is.EqualTo(MspHeader.V1Key1));
    Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)), Is.EqualTo(MspHeader.V1Key2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_V1_CorrectDimensions() {
    var file = new MspFile {
      Width = 16,
      Height = 4,
      Version = MspVersion.V1,
      PixelData = new byte[2 * 4]
    };

    var bytes = MspWriter.ToBytes(file);

    Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)), Is.EqualTo(16));
    Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6)), Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_V1_CorrectFileSize() {
    var file = new MspFile {
      Width = 16,
      Height = 3,
      Version = MspVersion.V1,
      PixelData = new byte[2 * 3]
    };

    var bytes = MspWriter.ToBytes(file);

    // header(32) + pixelData(6) = 38
    Assert.That(bytes.Length, Is.EqualTo(38));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_V1_PixelDataPreserved() {
    var pixelData = new byte[] { 0b11001100, 0b00110011 };
    var file = new MspFile {
      Width = 8,
      Height = 2,
      Version = MspVersion.V1,
      PixelData = pixelData
    };

    var bytes = MspWriter.ToBytes(file);

    Assert.That(bytes[MspHeader.StructSize], Is.EqualTo(0b11001100));
    Assert.That(bytes[MspHeader.StructSize + 1], Is.EqualTo(0b00110011));
  }
}
