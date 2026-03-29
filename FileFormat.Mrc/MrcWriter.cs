using System;
using System.Buffers.Binary;

namespace FileFormat.Mrc;

/// <summary>Assembles MRC2014 file bytes from an MrcFile model.</summary>
public static class MrcWriter {

  public static byte[] ToBytes(MrcFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file);
  }

  internal static byte[] Assemble(MrcFile file) {
    var extHeaderSize = file.ExtendedHeader.Length;
    var totalSize = MrcFile.HeaderSize + extHeaderSize + file.PixelData.Length;
    var result = new byte[totalSize];
    var span = result.AsSpan();

    // NX (columns) at offset 0
    BinaryPrimitives.WriteInt32LittleEndian(span, file.Width);

    // NY (rows) at offset 4
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], file.Height);

    // NZ (sections) at offset 8
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], file.Sections);

    // MODE at offset 12
    BinaryPrimitives.WriteInt32LittleEndian(span[12..], file.Mode);

    // NSYMBT (extended header size) at offset 92
    BinaryPrimitives.WriteInt32LittleEndian(span[92..], extHeaderSize);

    // MAP magic at offset 208
    MrcFile.MapMagic.CopyTo(span[208..]);

    // MACHST (machine stamp) at offset 212 - LE
    span[212] = MrcFile.MachineStampLE;
    span[213] = MrcFile.MachineStampLE;

    // Extended header
    if (extHeaderSize > 0)
      file.ExtendedHeader.AsSpan().CopyTo(span[MrcFile.HeaderSize..]);

    // Pixel data
    var dataOffset = MrcFile.HeaderSize + extHeaderSize;
    file.PixelData.AsSpan().CopyTo(span[dataOffset..]);

    return result;
  }
}
