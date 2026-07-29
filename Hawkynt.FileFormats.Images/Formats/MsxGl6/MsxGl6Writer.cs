using System;
using System.Buffers.Binary;

namespace FileFormat.MsxGl6;

/// <summary>Assembles MSX2 GL6 picture bytes.</summary>
public static class MsxGl6Writer {

  public static byte[] ToBytes(MsxGl6File file) {
    var pixels = file.PixelData ?? [];
    var size = MsxGl6File.PixelDataSizeFor(file.Width, file.Height);
    var result = new byte[MsxGl6File.HeaderSize + size];

    BinaryPrimitives.WriteUInt16LittleEndian(result, (ushort)file.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), (ushort)file.Height);
    pixels.AsSpan(0, Math.Min(pixels.Length, size)).CopyTo(result.AsSpan(MsxGl6File.HeaderSize));

    return result;
  }
}
