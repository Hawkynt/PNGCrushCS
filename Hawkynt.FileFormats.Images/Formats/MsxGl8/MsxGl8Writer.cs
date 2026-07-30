using System;
using System.Buffers.Binary;

namespace FileFormat.MsxGl8;

/// <summary>Assembles sized-header MSX2 Screen 8 picture bytes.</summary>
public static class MsxGl8Writer {

  public static byte[] ToBytes(MsxGl8File file) {
    var pixels = file.PixelData ?? [];
    var result = new byte[MsxGl8File.HeaderSize + file.Width * file.Height];

    BinaryPrimitives.WriteUInt16LittleEndian(result, (ushort)file.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), (ushort)file.Height);
    pixels.AsSpan(0, Math.Min(pixels.Length, result.Length - MsxGl8File.HeaderSize))
      .CopyTo(result.AsSpan(MsxGl8File.HeaderSize));

    return result;
  }
}
