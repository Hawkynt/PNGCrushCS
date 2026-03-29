using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Fsh;

namespace FileFormat.Fsh.Tests;

[TestFixture]
public sealed class FshWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => FshWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Magic_IsShpi() {
    var file = new FshFile {
      Entries = [
        new FshEntry {
          Tag = "img0",
          RecordCode = FshRecordCode.Argb8888,
          Width = 1,
          Height = 1,
          PixelData = new byte[4]
        }
      ]
    };

    var bytes = FshWriter.ToBytes(file);

    Assert.That(bytes[0], Is.EqualTo((byte)'S'));
    Assert.That(bytes[1], Is.EqualTo((byte)'H'));
    Assert.That(bytes[2], Is.EqualTo((byte)'P'));
    Assert.That(bytes[3], Is.EqualTo((byte)'I'));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSizeField_MatchesActualLength() {
    var file = new FshFile {
      Entries = [
        new FshEntry {
          Tag = "img0",
          RecordCode = FshRecordCode.Argb8888,
          Width = 2,
          Height = 2,
          PixelData = new byte[16]
        }
      ]
    };

    var bytes = FshWriter.ToBytes(file);

    var fileSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4));
    Assert.That(fileSize, Is.EqualTo(bytes.Length));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EntryCount_MatchesEntries() {
    var file = new FshFile {
      Entries = [
        new FshEntry {
          Tag = "img0",
          RecordCode = FshRecordCode.Argb8888,
          Width = 1,
          Height = 1,
          PixelData = new byte[4]
        },
        new FshEntry {
          Tag = "img1",
          RecordCode = FshRecordCode.Rgb888,
          Width = 1,
          Height = 1,
          PixelData = new byte[3]
        }
      ]
    };

    var bytes = FshWriter.ToBytes(file);

    var entryCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
    Assert.That(entryCount, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_DirectoryId_IsWritten() {
    var file = new FshFile {
      DirectoryId = "TEST",
      Entries = [
        new FshEntry {
          Tag = "img0",
          RecordCode = FshRecordCode.Argb8888,
          Width = 1,
          Height = 1,
          PixelData = new byte[4]
        }
      ]
    };

    var bytes = FshWriter.ToBytes(file);

    var dirId = Encoding.ASCII.GetString(bytes, 12, 4);
    Assert.That(dirId, Is.EqualTo("TEST"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EntryTag_IsWritten() {
    var file = new FshFile {
      Entries = [
        new FshEntry {
          Tag = "butn",
          RecordCode = FshRecordCode.Argb8888,
          Width = 1,
          Height = 1,
          PixelData = new byte[4]
        }
      ]
    };

    var bytes = FshWriter.ToBytes(file);

    var tag = Encoding.ASCII.GetString(bytes, 16, 4);
    Assert.That(tag, Is.EqualTo("butn"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RecordCode_IsWritten() {
    var file = new FshFile {
      Entries = [
        new FshEntry {
          Tag = "img0",
          RecordCode = FshRecordCode.Rgb888,
          Width = 1,
          Height = 1,
          PixelData = new byte[3]
        }
      ]
    };

    var bytes = FshWriter.ToBytes(file);

    // Entry data starts at offset 16 (header) + 8 (directory) = 24
    Assert.That(bytes[24], Is.EqualTo((byte)FshRecordCode.Rgb888));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Dimensions_AreWritten() {
    var file = new FshFile {
      Entries = [
        new FshEntry {
          Tag = "img0",
          RecordCode = FshRecordCode.Argb8888,
          Width = 320,
          Height = 240,
          PixelData = new byte[320 * 240 * 4]
        }
      ]
    };

    var bytes = FshWriter.ToBytes(file);

    var entryOffset = 16 + 8;
    var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entryOffset + 4));
    var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entryOffset + 6));
    Assert.That(width, Is.EqualTo(320));
    Assert.That(height, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CenterCoordinates_AreWritten() {
    var file = new FshFile {
      Entries = [
        new FshEntry {
          Tag = "img0",
          RecordCode = FshRecordCode.Argb8888,
          Width = 1,
          Height = 1,
          PixelData = new byte[4],
          CenterX = 15,
          CenterY = 25,
        }
      ]
    };

    var bytes = FshWriter.ToBytes(file);

    var entryOffset = 16 + 8;
    var cx = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entryOffset + 8));
    var cy = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entryOffset + 10));
    Assert.That(cx, Is.EqualTo(15));
    Assert.That(cy, Is.EqualTo(25));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Indexed8_IncludesPalette() {
    var palette = new byte[1024];
    palette[0] = 0xAA;
    var file = new FshFile {
      Entries = [
        new FshEntry {
          Tag = "pal0",
          RecordCode = FshRecordCode.Indexed8,
          Width = 2,
          Height = 2,
          PixelData = new byte[4],
          Palette = palette
        }
      ]
    };

    var bytes = FshWriter.ToBytes(file);

    // Header(16) + Dir(8) + RecordHeader(16) = 40 -> palette starts
    Assert.That(bytes[40], Is.EqualTo(0xAA));
    // Total = 16 + 8 + 16 + 1024 + 4 = 1068
    Assert.That(bytes.Length, Is.EqualTo(1068));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_EmptyEntries_ProducesValidHeader() {
    var file = new FshFile {
      Entries = []
    };

    var bytes = FshWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(16));
    var entryCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
    Assert.That(entryCount, Is.EqualTo(0));
  }
}
