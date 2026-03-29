using System;
using System.Buffers.Binary;
using System.Text;

namespace FileFormat.PalmPdb;

/// <summary>Assembles Palm PDB image file bytes from a <see cref="PalmPdbFile"/>.</summary>
public static class PalmPdbWriter {

  /// <summary>PDB header size (78 bytes).</summary>
  private const int _HEADER_SIZE = 78;

  /// <summary>Each record list entry is 8 bytes.</summary>
  private const int _RECORD_ENTRY_SIZE = 8;

  /// <summary>Image record header: uint16 width + uint16 height = 4 bytes.</summary>
  private const int _IMAGE_RECORD_HEADER_SIZE = 4;

  public static byte[] ToBytes(PalmPdbFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height, file.Name);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height, string name) {
    var expectedPixelBytes = width * height * 3;
    var recordDataOffset = _HEADER_SIZE + _RECORD_ENTRY_SIZE;
    var totalSize = recordDataOffset + _IMAGE_RECORD_HEADER_SIZE + expectedPixelBytes;

    var result = new byte[totalSize];
    var span = result.AsSpan();

    // Name: 32 bytes, null-terminated ASCII
    var nameBytes = Encoding.ASCII.GetBytes(name.Length > 31 ? name[..31] : name);
    nameBytes.CopyTo(span);
    // Remaining bytes in 0..31 are already zero

    // Attributes: uint16 BE at offset 32 (0)
    // Version: uint16 BE at offset 34 (0)
    // Creation date: uint32 BE at offset 36 (0)
    // Modification date: uint32 BE at offset 40 (0)
    // Backup date: uint32 BE at offset 44 (0)
    // Modification number: uint32 BE at offset 48 (0)
    // App info offset: uint32 BE at offset 52 (0)
    // Sort info offset: uint32 BE at offset 56 (0)

    // Type: 4 bytes "Img " at offset 60
    span[60] = (byte)'I';
    span[61] = (byte)'m';
    span[62] = (byte)'g';
    span[63] = (byte)' ';

    // Creator: 4 bytes "View" at offset 64
    span[64] = (byte)'V';
    span[65] = (byte)'i';
    span[66] = (byte)'e';
    span[67] = (byte)'w';

    // Unique ID seed: uint32 BE at offset 68 (0)
    // Next record list: uint32 BE at offset 72 (0)

    // Record count: uint16 BE at offset 76
    BinaryPrimitives.WriteUInt16BigEndian(span[76..], 1);

    // Record list entry at offset 78:
    //   Offset: uint32 BE -> points to image record
    BinaryPrimitives.WriteUInt32BigEndian(span[78..], (uint)recordDataOffset);
    //   Attributes: byte at offset 82 (0)
    //   Unique ID: 3 bytes at offsets 83-85 (0)

    // Image record at recordDataOffset:
    //   Width: uint16 BE
    BinaryPrimitives.WriteUInt16BigEndian(span[recordDataOffset..], (ushort)width);
    //   Height: uint16 BE
    BinaryPrimitives.WriteUInt16BigEndian(span[(recordDataOffset + 2)..], (ushort)height);

    // RGB24 pixel data
    var copyLen = Math.Min(expectedPixelBytes, pixelData.Length);
    pixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(recordDataOffset + _IMAGE_RECORD_HEADER_SIZE));

    return result;
  }
}
