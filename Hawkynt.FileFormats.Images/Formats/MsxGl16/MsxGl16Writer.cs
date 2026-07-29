using System;
using System.Buffers.Binary;

namespace FileFormat.MsxGl16;

/// <summary>Assembles sixteen-colour MSX2 GL picture bytes.</summary>
public static class MsxGl16Writer {

  public static byte[] ToBytes(MsxGl16File file) {
    var pixels = file.PixelData ?? [];
    var size = MsxGl16File.PixelDataSizeFor(file.Width, file.Height);
    var result = new byte[MsxGl16File.HeaderSize + size];

    BinaryPrimitives.WriteUInt16LittleEndian(result, (ushort)file.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), (ushort)file.Height);
    pixels.AsSpan(0, Math.Min(pixels.Length, size)).CopyTo(result.AsSpan(MsxGl16File.HeaderSize));

    return result;
  }
}
