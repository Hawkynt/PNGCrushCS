using System;
using System.Buffers.Binary;
using System.Text;

namespace FileFormat.PalmPdb;

/// <summary>Assembles a Palm Image Viewer picture into a PDB database.</summary>
/// <remarks>
/// Two of the choices here are not free, and both were settled by trying them against ImageMagick:
/// the pixels go in uncompressed, because the Image Viewer's compression is literal PackBits runs
/// only and a repeat run makes ImageMagick hand back an empty image; and the record's unique id is
/// the one ImageMagick itself writes, 0x6F8000, because it will not open a record carrying any
/// other — including a plain 1.
/// </remarks>
public static class PalmPdbWriter {

  /// <summary>The PDB database header, before the record list.</summary>
  private const int _DATABASE_HEADER_SIZE = 78;

  /// <summary>One entry per record: a four-byte offset, then attributes and a three-byte id.</summary>
  private const int _RECORD_ENTRY_SIZE = 8;

  public static byte[] ToBytes(PalmPdbFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData ?? [], file.Width, file.Height, file.Name ?? string.Empty);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height, string name) {
    var stride = ((width * PalmPdbReader.BitsPerPixel) + 7) / 8;
    var expected = stride * height;

    // Stored uncompressed. The reader here takes PackBits either way, but the Image Viewer's own
    // compression is literal runs only — a repeat run makes ImageMagick hand back an empty image —
    // and literal runs cost a byte in every 128, so plain rows are both smaller and readable.
    var rows = new byte[expected];
    pixelData.AsSpan(0, Math.Min(expected, pixelData.Length)).CopyTo(rows);

    var recordOffset = _DATABASE_HEADER_SIZE + _RECORD_ENTRY_SIZE;
    var result = new byte[recordOffset + PalmPdbReader.ImageHeaderSize + rows.Length];
    var span = result.AsSpan();

    var trimmed = name.Length > 31 ? name[..31] : name;
    Encoding.ASCII.GetBytes(trimmed).CopyTo(span);

    // The type and creator at 60 and 64 are what make a database an Image Viewer picture rather than
    // an address book.
    PalmPdbReader.ExpectedType.CopyTo(span[60..]);
    PalmPdbReader.ExpectedCreator.CopyTo(span[64..]);
    BinaryPrimitives.WriteUInt16BigEndian(span[76..], 1); // one record

    // The one record: where it starts, what it is, and which record it is.
    BinaryPrimitives.WriteUInt32BigEndian(span[_DATABASE_HEADER_SIZE..], (uint)recordOffset);
    span[_DATABASE_HEADER_SIZE + 4] = 0x40; // attributes: dirty, which is what a fresh record is
    span[_DATABASE_HEADER_SIZE + 5] = 0x6F; // unique id, three bytes
    span[_DATABASE_HEADER_SIZE + 6] = 0x80;
    span[_DATABASE_HEADER_SIZE + 7] = 0x00;

    var record = span[recordOffset..];
    Encoding.ASCII.GetBytes(trimmed).CopyTo(record);
    record[32] = 0; // version: the pixels below are stored as they are
    record[33] = 0; // type: four greys
    BinaryPrimitives.WriteInt16BigEndian(record[42..], (short)(width - 1));  // x_last
    BinaryPrimitives.WriteInt16BigEndian(record[44..], (short)(height - 1)); // y_last
    BinaryPrimitives.WriteInt16BigEndian(record[50..], -1); // x_anchor: no anchor
    BinaryPrimitives.WriteInt16BigEndian(record[52..], -1); // y_anchor
    BinaryPrimitives.WriteInt16BigEndian(record[54..], (short)width);
    BinaryPrimitives.WriteInt16BigEndian(record[56..], (short)height);

    rows.CopyTo(record[PalmPdbReader.ImageHeaderSize..]);

    return result;
  }

}
