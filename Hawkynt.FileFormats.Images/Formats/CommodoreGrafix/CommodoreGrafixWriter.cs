using System;
using System.Text;

namespace FileFormat.CommodoreGrafix;

/// <summary>Assembles a Commodore Grafix file from a <see cref="CommodoreGrafixFile"/>.</summary>
public static class CommodoreGrafixWriter {

  /// <summary>Bytes the format chunk's payload occupies.</summary>
  private const int _FORMAT_LENGTH = 12;

  /// <summary>Where a frame's bytes start: past the RIFF header and the whole format chunk.</summary>
  public const int DataChunkOffset = 12 + 8 + _FORMAT_LENGTH + 8;

  /// <summary>Writes the file, which is already whole because its chunks are one after another.</summary>
  public static byte[] ToBytes(CommodoreGrafixFile file) => (byte[])(file.Data ?? []).Clone();

  /// <summary>Wraps one frame in the RIFF container the format borrows.</summary>
  /// <remarks>
  /// The chunked wrapper is what lets a file carry metadata a decoder need not understand, so a
  /// 'META' chunk is passed over rather than rejected — but writing one would be inventing metadata
  /// nobody asked for, so only the two chunks that are the picture are written.
  /// </remarks>
  public static byte[] Assemble(int columns, int rows, ReadOnlySpan<byte> frame) {
    var data = new byte[DataChunkOffset + frame.Length];

    Encoding.ASCII.GetBytes("RIFF").CopyTo(data, 0);
    _Length(data, 4, data.Length - 8);
    Encoding.ASCII.GetBytes("CGFX").CopyTo(data, 8);

    Encoding.ASCII.GetBytes("FRMT").CopyTo(data, 12);
    _Length(data, 16, _FORMAT_LENGTH);

    // One frame, so the matrix is one by one; the frame count is stated a second time and four is
    // the only pixel depth there is.
    data[20] = 1;
    data[21] = 1;
    data[24] = 1;
    data[28] = (byte)rows;
    data[29] = (byte)columns;
    data[30] = 4;

    Encoding.ASCII.GetBytes("DATA").CopyTo(data, 32);
    _Length(data, 36, frame.Length);
    frame.CopyTo(data.AsSpan(DataChunkOffset));

    return data;
  }

  private static void _Length(Span<byte> data, int offset, int value) {
    data[offset] = (byte)value;
    data[offset + 1] = (byte)(value >> 8);
    data[offset + 2] = (byte)(value >> 16);
    data[offset + 3] = (byte)(value >> 24);
  }
}
