using System;
using System.Buffers.Binary;
using FileFormat.Ccitt;

namespace FileFormat.LaserData;

/// <summary>Writes the verified LaserData header followed by CCITT Group 4 coding.</summary>
public static class LaserDataWriter {

  public static byte[] ToBytes(LaserDataFile file) {
    if (file.Width is < 1 or > ushort.MaxValue || file.Height is < 1 or > ushort.MaxValue)
      throw new ArgumentException($"LaserData dimensions must fit 16-bit fields; got {file.Width}x{file.Height}.", nameof(file));
    var bytesPerRow = checked((file.Width + 7) / 8);
    var expected = checked(bytesPerRow * file.Height);
    if (file.PixelData == null || file.PixelData.Length < expected)
      throw new ArgumentException($"LaserData needs {expected} packed 1bpp bytes.", nameof(file));

    var coded = CcittG4Encoder.Encode(file.PixelData, file.Width, file.Height);
    if (!file.IsMostSignificantBitFirst)
      coded = CcittFillOrder.Reverse(coded);

    var output = new byte[checked(LaserDataFile.HeaderSize + coded.Length)];
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(0, 2), LaserDataFile.Magic);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(6, 2), checked((ushort)file.Height));
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(8, 2), checked((ushort)file.Width));
    output[12] = (byte)LaserDataCompression.Group4;
    output[13] = file.IsMostSignificantBitFirst ? (byte)1 : (byte)0;
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(16, 2), file.VerticalResolution);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(18, 2), file.HorizontalResolution);
    coded.CopyTo(output, LaserDataFile.HeaderSize);
    return output;
  }
}
