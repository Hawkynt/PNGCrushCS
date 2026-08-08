using System;
using System.Buffers.Binary;

namespace FileFormat.PrintTechnik;

/// <summary>Assembles a Print-Technik scan: the header with its size, then a byte a pixel.</summary>
public static class PrintTechnikWriter {

  public static byte[] ToBytes(PrintTechnikFile file) {
    var pixels = file.PixelData ?? [];
    var result = new byte[PrintTechnikFile.HeaderSize + file.Width * file.Height];

    (file.Header ?? []).AsSpan(0, Math.Min((file.Header ?? []).Length, PrintTechnikFile.HeaderSize)).CopyTo(result);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(PrintTechnikFile.WidthAt), (ushort)file.Width);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(PrintTechnikFile.HeightAt), (ushort)file.Height);
    pixels.AsSpan(0, Math.Min(pixels.Length, file.Width * file.Height))
      .CopyTo(result.AsSpan(PrintTechnikFile.HeaderSize));

    return result;
  }
}
