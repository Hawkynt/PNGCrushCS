using System;
using System.Buffers.Binary;
using FileFormat.Eps;

namespace FileFormat.Eps.Tests;

[TestFixture]
public sealed class EpsWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => EpsWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagicBytes() {
    var file = new EpsFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3]
    };

    var bytes = EpsWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo(0xC5));
    Assert.That(bytes[1], Is.EqualTo(0xD0));
    Assert.That(bytes[2], Is.EqualTo(0xD3));
    Assert.That(bytes[3], Is.EqualTo(0xC6));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PsSectionPresent() {
    var file = new EpsFile {
      Width = 4,
      Height = 3,
      PixelData = new byte[4 * 3 * 3]
    };

    var bytes = EpsWriter.ToBytes(file);

    var psOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
    var psLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));

    Assert.That(psOffset, Is.EqualTo(30u));
    Assert.That(psLength, Is.GreaterThan(0u));

    // Verify PS content starts at offset
    Assert.That(bytes[(int)psOffset], Is.EqualTo((byte)'%'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TiffOffsetNonZero() {
    var file = new EpsFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = EpsWriter.ToBytes(file);

    var tiffOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20));
    var tiffLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24));

    Assert.That(tiffOffset, Is.GreaterThan(0u));
    Assert.That(tiffLength, Is.GreaterThan(0u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize() {
    var file = new EpsFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3]
    };

    var bytes = EpsWriter.ToBytes(file);

    // Must be at least 30 (header) + some PS + some TIFF
    Assert.That(bytes.Length, Is.GreaterThan(30));

    // Verify total size matches header + PS + TIFF
    var psOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
    var psLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
    var tiffOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20));
    var tiffLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24));

    Assert.That((uint)bytes.Length, Is.EqualTo(tiffOffset + tiffLength));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WmfFieldsAreZero() {
    var file = new EpsFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3]
    };

    var bytes = EpsWriter.ToBytes(file);

    var wmfOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
    var wmfLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16));

    Assert.That(wmfOffset, Is.EqualTo(0u));
    Assert.That(wmfLength, Is.EqualTo(0u));
  }
}
