using System;
using System.Buffers.Binary;
using FileFormat.Emf;

namespace FileFormat.Emf.Tests;

[TestFixture]
public sealed class EmfWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => EmfWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmrHeaderType1() {
    var file = new EmfFile {
      Width = 4,
      Height = 3,
      PixelData = new byte[4 * 3 * 3]
    };

    var bytes = EmfWriter.ToBytes(file);
    var recordType = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0));

    Assert.That(recordType, Is.EqualTo(1u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmfSignatureAtOffset40() {
    var file = new EmfFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = EmfWriter.ToBytes(file);

    Assert.That(bytes[40], Is.EqualTo(0x20)); // ' '
    Assert.That(bytes[41], Is.EqualTo(0x45)); // 'E'
    Assert.That(bytes[42], Is.EqualTo(0x4D)); // 'M'
    Assert.That(bytes[43], Is.EqualTo(0x46)); // 'F'
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmrEofPresent() {
    var file = new EmfFile {
      Width = 1,
      Height = 1,
      PixelData = new byte[3]
    };

    var bytes = EmfWriter.ToBytes(file);

    // EOF record is the last 20 bytes
    var eofOffset = bytes.Length - 20;
    var eofType = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(eofOffset));
    var eofSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(eofOffset + 4));

    Assert.That(eofType, Is.EqualTo(14u));
    Assert.That(eofSize, Is.EqualTo(20u));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSizeField() {
    var file = new EmfFile {
      Width = 4,
      Height = 3,
      PixelData = new byte[4 * 3 * 3]
    };

    var bytes = EmfWriter.ToBytes(file);
    var storedSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(48));

    Assert.That(storedSize, Is.EqualTo((uint)bytes.Length));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StretchDiBitsRecordPresent() {
    var file = new EmfFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = EmfWriter.ToBytes(file);

    // StretchDIBits record starts at offset 88 (after header)
    var recordType = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(88));

    Assert.That(recordType, Is.EqualTo(81u));
  }
}
