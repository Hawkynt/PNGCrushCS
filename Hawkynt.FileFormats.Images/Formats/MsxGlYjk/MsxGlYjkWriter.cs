using System;
using System.Buffers.Binary;

namespace FileFormat.MsxGlYjk;

/// <summary>Assembles MSX2+ GL/SH YJK picture bytes.</summary>
public static class MsxGlYjkWriter {

  public static byte[] ToBytes(MsxGlYjkFile file) {
    var pixels = file.PixelData ?? [];
    var result = new byte[MsxGlYjkFile.HeaderSize + file.Width * file.Height];

    BinaryPrimitives.WriteUInt16LittleEndian(result, (ushort)file.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), (ushort)file.Height);
    pixels.AsSpan(0, Math.Min(pixels.Length, result.Length - MsxGlYjkFile.HeaderSize))
      .CopyTo(result.AsSpan(MsxGlYjkFile.HeaderSize));

    return result;
  }
}
