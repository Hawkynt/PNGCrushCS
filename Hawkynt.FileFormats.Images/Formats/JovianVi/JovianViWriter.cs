using System;
using System.Buffers.Binary;

namespace FileFormat.JovianVi;

/// <summary>Assembles Jovian Logic VI image bytes.</summary>
public static class JovianViWriter {

  public static byte[] ToBytes(JovianViFile file) {
    var pixels = file.Width * file.Height;
    var paletteOffset = JovianViFile.HeaderSize;
    var pixelOffset = paletteOffset + JovianViFile.PaletteSize;
    var result = new byte[pixelOffset + pixels];

    result[0] = (byte)'V';
    result[1] = (byte)'I';
    result[2] = file.Version == 0 ? (byte)'0' : file.Version;
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(3), (ushort)file.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(5), (ushort)file.Height);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(12), (ushort)paletteOffset);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(14), (ushort)pixelOffset);

    var palette = file.Palette ?? [];
    palette.AsSpan(0, Math.Min(palette.Length, JovianViFile.PaletteSize)).CopyTo(result.AsSpan(paletteOffset));

    var data = file.PixelData ?? [];
    data.AsSpan(0, Math.Min(data.Length, pixels)).CopyTo(result.AsSpan(pixelOffset));

    return result;
  }
}
