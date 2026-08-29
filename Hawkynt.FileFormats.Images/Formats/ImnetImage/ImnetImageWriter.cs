using System;
using System.Buffers.Binary;
using FileFormat.Ccitt;

namespace FileFormat.ImnetImage;

/// <summary>Writes the verified mixed-endian IMNET header followed by CCITT Group 4 coding.</summary>
public static class ImnetImageWriter {

  public static byte[] ToBytes(ImnetImageFile file) {
    if (file.Width < 8 || (file.Width & 7) != 0 || file.Height <= 0)
      throw new ArgumentException($"IMNET width must be a positive multiple of eight; got {file.Width}x{file.Height}.", nameof(file));
    var bytesPerRow = file.Width / 8;
    var expected = checked(bytesPerRow * file.Height);
    if (file.PixelData == null || file.PixelData.Length < expected)
      throw new ArgumentException($"IMNET needs {expected} packed 1bpp bytes.", nameof(file));

    var coded = CcittG4Encoder.Encode(file.PixelData, file.Width, file.Height);
    if (!file.IsMostSignificantBitFirst)
      coded = CcittFillOrder.Reverse(coded);

    var output = new byte[checked(ImnetImageFile.HeaderSize + coded.Length)];
    BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(0, 4), ImnetImageFile.Magic);
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(8, 4), checked((uint)file.Height));
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(12, 4), checked((uint)bytesPerRow));
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(16, 2), file.Resolution);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(18, 2), file.IsMostSignificantBitFirst ? (ushort)0 : (ushort)1);
    coded.CopyTo(output, ImnetImageFile.HeaderSize);
    return output;
  }
}
