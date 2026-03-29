using System;
using System.Buffers.Binary;
using FileFormat.Psp;

namespace FileFormat.Psp.Tests;

[TestFixture]
public sealed class PspWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PspWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithMagicBytes() {
    var file = new PspFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3]
    };

    var bytes = PspWriter.ToBytes(file);

    for (var i = 0; i < PspFile.Magic.Length; ++i)
      Assert.That(bytes[i], Is.EqualTo(PspFile.Magic[i]), $"Magic byte mismatch at index {i}");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsVersionAfterMagic() {
    var file = new PspFile {
      Width = 1,
      Height = 1,
      MajorVersion = 5,
      MinorVersion = 2,
      PixelData = new byte[3]
    };

    var bytes = PspWriter.ToBytes(file);
    var major = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(32));
    var minor = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(34));

    Assert.That(major, Is.EqualTo(5));
    Assert.That(minor, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsGeneralAttributesBlockId() {
    var file = new PspFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = PspWriter.ToBytes(file);
    var blockId = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(36));

    Assert.That(blockId, Is.EqualTo(PspFile.BlockIdGeneralAttributes));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsWidthInGeneralAttributes() {
    var file = new PspFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 3]
    };

    var bytes = PspWriter.ToBytes(file);
    // Block header starts at 36, data starts at 46 (36 + 10 byte block header)
    var width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(46));

    Assert.That(width, Is.EqualTo(320));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsHeightInGeneralAttributes() {
    var file = new PspFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 3]
    };

    var bytes = PspWriter.ToBytes(file);
    var height = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(50));

    Assert.That(height, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsCompositeBlockId() {
    var file = new PspFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3]
    };

    var bytes = PspWriter.ToBytes(file);
    // General attributes block: header(10) + data(27) = 37 bytes starting at 36, so composite block starts at 73
    var compositeOffset = 36 + 10 + 27;
    var blockId = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(compositeOffset));

    Assert.That(blockId, Is.EqualTo(PspFile.BlockIdCompositeImage));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new PspFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    var bytes = PspWriter.ToBytes(file);
    // Composite block data starts at: 36 + 37 (general block) + 10 (composite header) = 83
    var compositeDataOffset = 36 + 10 + 27 + 10;

    Assert.That(bytes[compositeDataOffset], Is.EqualTo(0xAA));
    Assert.That(bytes[compositeDataOffset + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[compositeDataOffset + 2], Is.EqualTo(0xCC));
  }
}
