using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.PalmPdb;

namespace FileFormat.PalmPdb.Tests;

[TestFixture]
public sealed class PalmPdbWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws() {
    Assert.Throws<ArgumentNullException>(() => PalmPdbWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TypeField_IsImgSpace() {
    var file = new PalmPdbFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = PalmPdbWriter.ToBytes(file);

    var type = Encoding.ASCII.GetString(bytes, 60, 4);
    Assert.That(type, Is.EqualTo("Img "));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CreatorField_IsView() {
    var file = new PalmPdbFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = PalmPdbWriter.ToBytes(file);

    var creator = Encoding.ASCII.GetString(bytes, 64, 4);
    Assert.That(creator, Is.EqualTo("View"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RecordCount_IsOne() {
    var file = new PalmPdbFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = PalmPdbWriter.ToBytes(file);

    var recordCount = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(76));
    Assert.That(recordCount, Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Dimensions_Correct() {
    var file = new PalmPdbFile {
      Width = 320,
      Height = 240,
      PixelData = new byte[320 * 240 * 3]
    };

    var bytes = PalmPdbWriter.ToBytes(file);

    // Record offset is at 78 (header) + 8 (record entry) = 86
    var recordOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(78));
    var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(recordOffset));
    var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(recordOffset + 2));

    Assert.That(width, Is.EqualTo(320));
    Assert.That(height, Is.EqualTo(240));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TotalSize_Correct() {
    var w = 4;
    var h = 3;
    var file = new PalmPdbFile {
      Width = w,
      Height = h,
      PixelData = new byte[w * h * 3]
    };

    var bytes = PalmPdbWriter.ToBytes(file);

    // 78 header + 8 record entry + 4 image header + pixel data
    var expected = 78 + 8 + 4 + w * h * 3;
    Assert.That(bytes.Length, Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Name_WrittenToHeader() {
    var file = new PalmPdbFile {
      Width = 1,
      Height = 1,
      Name = "MyImage",
      PixelData = new byte[3]
    };

    var bytes = PalmPdbWriter.ToBytes(file);

    var nameEnd = 0;
    while (nameEnd < 32 && bytes[nameEnd] != 0)
      ++nameEnd;
    var name = Encoding.ASCII.GetString(bytes, 0, nameEnd);
    Assert.That(name, Is.EqualTo("MyImage"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LongName_TruncatedTo31() {
    var longName = new string('A', 50);
    var file = new PalmPdbFile {
      Width = 1,
      Height = 1,
      Name = longName,
      PixelData = new byte[3]
    };

    var bytes = PalmPdbWriter.ToBytes(file);

    var nameEnd = 0;
    while (nameEnd < 32 && bytes[nameEnd] != 0)
      ++nameEnd;
    Assert.That(nameEnd, Is.EqualTo(31));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PixelDataPreserved() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new PalmPdbFile {
      Width = 1,
      Height = 1,
      PixelData = pixels
    };

    var bytes = PalmPdbWriter.ToBytes(file);

    var recordOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(78));
    var pixelStart = recordOffset + 4;

    Assert.That(bytes[pixelStart], Is.EqualTo(0xAA));
    Assert.That(bytes[pixelStart + 1], Is.EqualTo(0xBB));
    Assert.That(bytes[pixelStart + 2], Is.EqualTo(0xCC));
  }
}
