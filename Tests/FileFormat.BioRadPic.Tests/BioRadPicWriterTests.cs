using System;
using System.Buffers.Binary;
using FileFormat.BioRadPic;

namespace FileFormat.BioRadPic.Tests;

[TestFixture]
public sealed class BioRadPicWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BioRadPicWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HeaderSize76() {
    var file = new BioRadPicFile {
      Width = 2,
      Height = 2,
      ByteFormat = true,
      PixelData = new byte[4]
    };

    var bytes = BioRadPicWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(BioRadPicHeader.StructSize + 4));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileIdAtOffset54() {
    var file = new BioRadPicFile {
      Width = 1,
      Height = 1,
      ByteFormat = true,
      PixelData = new byte[1]
    };

    var bytes = BioRadPicWriter.ToBytes(file);
    var fileId = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(54));

    Assert.That(fileId, Is.EqualTo(BioRadPicHeader.MagicFileId));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DimensionsWrittenCorrectly() {
    var file = new BioRadPicFile {
      Width = 320,
      Height = 240,
      ByteFormat = true,
      PixelData = new byte[320 * 240]
    };

    var bytes = BioRadPicWriter.ToBytes(file);
    var nx = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0));
    var ny = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2));

    Assert.That(nx, Is.EqualTo(320));
    Assert.That(ny, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ByteFormatFieldCorrect_8Bit() {
    var file = new BioRadPicFile {
      Width = 1,
      Height = 1,
      ByteFormat = true,
      PixelData = new byte[1]
    };

    var bytes = BioRadPicWriter.ToBytes(file);
    var bf = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(14));

    Assert.That(bf, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ByteFormatFieldCorrect_16Bit() {
    var file = new BioRadPicFile {
      Width = 1,
      Height = 1,
      ByteFormat = false,
      PixelData = new byte[2]
    };

    var bytes = BioRadPicWriter.ToBytes(file);
    var bf = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(14));

    Assert.That(bf, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_16BitFileSize() {
    var file = new BioRadPicFile {
      Width = 4,
      Height = 3,
      ByteFormat = false,
      PixelData = new byte[4 * 3 * 2]
    };

    var bytes = BioRadPicWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(BioRadPicHeader.StructSize + 24));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixelData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
    var file = new BioRadPicFile {
      Width = 2,
      Height = 2,
      ByteFormat = true,
      PixelData = pixelData
    };

    var bytes = BioRadPicWriter.ToBytes(file);

    Assert.That(bytes[BioRadPicHeader.StructSize], Is.EqualTo(0xDE));
    Assert.That(bytes[BioRadPicHeader.StructSize + 1], Is.EqualTo(0xAD));
    Assert.That(bytes[BioRadPicHeader.StructSize + 2], Is.EqualTo(0xBE));
    Assert.That(bytes[BioRadPicHeader.StructSize + 3], Is.EqualTo(0xEF));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LensWritten() {
    var file = new BioRadPicFile {
      Width = 1,
      Height = 1,
      ByteFormat = true,
      Lens = 100,
      PixelData = new byte[1]
    };

    var bytes = BioRadPicWriter.ToBytes(file);
    var lens = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(64));

    Assert.That(lens, Is.EqualTo(100));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MagFactorWritten() {
    var file = new BioRadPicFile {
      Width = 1,
      Height = 1,
      ByteFormat = true,
      MagFactor = 3.14f,
      PixelData = new byte[1]
    };

    var bytes = BioRadPicWriter.ToBytes(file);
    var mag = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(66));

    Assert.That(mag, Is.EqualTo(3.14f));
  }
}
