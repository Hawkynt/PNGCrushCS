using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.PalmPdb;

namespace FileFormat.PalmPdb.Tests;

/// <summary>Reads a Palm Image Viewer picture out of the database that carries it.</summary>
/// <remarks>
/// These used to describe a format that does not exist — type <c>Img&#32;</c>, and a record holding a
/// width, a height and RGB triples — so they passed against a reader that could not open a single
/// real file. A picture database declares type <c>vIMG</c>, creator <c>View</c>, and its record opens
/// with a 58-byte descriptor before any pixels; the pixels themselves are two bits each, four greys,
/// optionally PackBits compressed.
/// </remarks>
[TestFixture]
public sealed class PalmPdbReaderTests {

  private const int _DATABASE_HEADER_SIZE = 78;
  private const int _RECORD_ENTRY_SIZE = 8;
  private const int _IMAGE_HEADER_SIZE = 58;

  /// <summary>Builds the database a Palm picture arrives in, around the record data given.</summary>
  private static byte[] Build(int width, int height, byte[] payload, byte version = 0, string type = "vIMG") {
    var recordOffset = _DATABASE_HEADER_SIZE + _RECORD_ENTRY_SIZE;
    var data = new byte[recordOffset + _IMAGE_HEADER_SIZE + payload.Length];
    var span = data.AsSpan();

    Encoding.ASCII.GetBytes("Test").CopyTo(span);
    Encoding.ASCII.GetBytes(type).CopyTo(span[60..]);
    Encoding.ASCII.GetBytes("View").CopyTo(span[64..]);
    BinaryPrimitives.WriteUInt16BigEndian(span[76..], 1); // one record

    BinaryPrimitives.WriteUInt32BigEndian(span[_DATABASE_HEADER_SIZE..], (uint)recordOffset);
    span[_DATABASE_HEADER_SIZE + 4] = 0x40;
    span[_DATABASE_HEADER_SIZE + 5] = 0x6F;
    span[_DATABASE_HEADER_SIZE + 6] = 0x80;

    var record = span[recordOffset..];
    Encoding.ASCII.GetBytes("Test").CopyTo(record);
    record[32] = version;
    record[33] = 0; // type: four greys
    BinaryPrimitives.WriteInt16BigEndian(record[50..], -1);
    BinaryPrimitives.WriteInt16BigEndian(record[52..], -1);
    BinaryPrimitives.WriteInt16BigEndian(record[54..], (short)width);
    BinaryPrimitives.WriteInt16BigEndian(record[56..], (short)height);
    payload.CopyTo(record[_IMAGE_HEADER_SIZE..]);

    return data;
  }

  /// <summary>Sixteen pixels: four of each grey, lightest first.</summary>
  private static byte[] FourGreysRow() => [0x1B, 0x1B, 0x1B, 0x1B];

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PalmPdbReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => PalmPdbReader.FromBytes(new byte[32]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongDatabaseType_ThrowsInvalidDataException() {
    var data = Build(16, 1, FourGreysRow(), type: "Img ");
    Assert.Throws<InvalidDataException>(() => PalmPdbReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Uncompressed_ParsesDimensions() {
    var file = PalmPdbReader.FromBytes(Build(16, 1, FourGreysRow()));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(16));
      Assert.That(file.Height, Is.EqualTo(1));
      Assert.That(file.Name, Is.EqualTo("Test"));
      Assert.That(file.PixelData, Has.Length.EqualTo(4), "two bits a pixel over sixteen pixels");
    });
  }

  /// <summary>Index 0 is white here and index 3 is black, the way a Palm shows them.</summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_ResolvesTheFourGreys() {
    var rgb = PalmPdbFile.ToRawImage(PalmPdbReader.FromBytes(Build(16, 1, FourGreysRow()))).ToRgb24();

    Assert.Multiple(() => {
      Assert.That(rgb[0], Is.EqualTo(255), "index 0 is white");
      Assert.That(rgb[3], Is.EqualTo(170), "index 1");
      Assert.That(rgb[6], Is.EqualTo(85), "index 2");
      Assert.That(rgb[9], Is.EqualTo(0), "index 3 is black");
    });
  }

  /// <summary>Version 1 says the rows are PackBits compressed.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_Compressed_ExpandsItsRuns() {
    // Four literal bytes, then a run of four more: eight bytes out of seven.
    byte[] payload = [0x03, 0x1B, 0x1B, 0x1B, 0x1B, 0xFD, 0xE4];
    var file = PalmPdbReader.FromBytes(Build(16, 2, payload, version: 1));

    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 0x1B, 0x1B, 0x1B, 0x1B, 0xE4, 0xE4, 0xE4, 0xE4 }));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_UnknownPictureType_ThrowsNotSupportedException() {
    var data = Build(16, 1, FourGreysRow());
    data[_DATABASE_HEADER_SIZE + _RECORD_ENTRY_SIZE + 33] = 2; // a type there is nothing to check against

    Assert.Throws<NotSupportedException>(() => PalmPdbReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Valid_Parses() {
    using var ms = new MemoryStream(Build(16, 1, FourGreysRow()));
    Assert.That(PalmPdbReader.FromStream(ms).Width, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdb"));
    Assert.Throws<FileNotFoundException>(() => PalmPdbReader.FromFile(missing));
  }
}
