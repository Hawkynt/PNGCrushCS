using System;
using System.Buffers.Binary;
using FileFormat.Awd;

namespace FileFormat.Awd.Tests;

[TestFixture]
public sealed class AwdWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AwdWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithMagic() {
    var file = new AwdFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[1],
    };

    var bytes = AwdWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'A'));
    Assert.That(bytes[1], Is.EqualTo((byte)'W'));
    Assert.That(bytes[2], Is.EqualTo((byte)'D'));
    Assert.That(bytes[3], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_VersionIsOne() {
    var file = new AwdFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[1],
    };

    var bytes = AwdWriter.ToBytes(file);
    var version = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));

    Assert.That(version, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsCorrect() {
    var file = new AwdFile {
      Width = 16,
      Height = 4,
      PixelData = new byte[8],
    };

    var bytes = AwdWriter.ToBytes(file);
    var width = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(6));
    var height = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(10));

    Assert.That(width, Is.EqualTo(16));
    Assert.That(height, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSizeCorrect() {
    // 16x4: bytesPerRow=2, pixelData=8 bytes, total=16+8=24
    var file = new AwdFile {
      Width = 16,
      Height = 4,
      PixelData = new byte[8],
    };

    var bytes = AwdWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(24));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[] { 0b10110100, 0b01001011 };
    var file = new AwdFile {
      Width = 8,
      Height = 2,
      PixelData = pixelData,
    };

    var bytes = AwdWriter.ToBytes(file);

    Assert.That(bytes[16], Is.EqualTo(0b10110100));
    Assert.That(bytes[17], Is.EqualTo(0b01001011));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ReservedIsZero() {
    var file = new AwdFile {
      Width = 8,
      Height = 1,
      PixelData = new byte[1],
    };

    var bytes = AwdWriter.ToBytes(file);
    var reserved = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14));

    Assert.That(reserved, Is.EqualTo(0));
  }
}
