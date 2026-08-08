using System;
using System.Buffers.Binary;

namespace FileFormat.PsionPic;

/// <summary>Assembles Psion PIC bytes from a <see cref="PsionPicFile"/>.</summary>
/// <remarks>
/// Only the picture is written, never a mask. A second bitmap is the negative of the first and adds
/// nothing a viewer draws, so a file carrying one would be twice the size for the same image.
/// </remarks>
public static class PsionPicWriter {

  public static byte[] ToBytes(PsionPicFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;
    var bytesPerRow = (width + 15) / 16 * 2;
    var bitmap = bytesPerRow * height;
    var data = new byte[PsionPicFile.FirstRecord + PsionPicFile.RecordSize + bitmap];

    PsionPicFile.Magic.CopyTo(data);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), 1);

    var record = data.AsSpan(PsionPicFile.FirstRecord);
    BinaryPrimitives.WriteUInt16LittleEndian(record[2..], (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(record[4..], (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(record[6..], (ushort)bitmap);

    // The offset is measured from the end of the record, and the only bitmap starts there.
    BinaryPrimitives.WriteUInt32LittleEndian(record[8..], 0);

    var pixels = file.PixelData ?? [];
    var target = PsionPicFile.FirstRecord + PsionPicFile.RecordSize;

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var from = y * width + x;
      if (from >= pixels.Length || pixels[from] == 0)
        continue;

      // The bits run from the least significant end of each byte.
      data[target + y * bytesPerRow + x / 8] |= (byte)(1 << (x & 7));
    }

    return data;
  }
}
