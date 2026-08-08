using System;
using System.Buffers.Binary;

namespace FileFormat.CelGrey;

/// <summary>Assembles a four-bit greyscale .cel: the size, then two pixels a byte.</summary>
public static class CelGreyWriter {

  public static byte[] ToBytes(CelGreyFile file) {
    var stride = CelGreyFile.BytesPerRow(file.Width);
    var pixels = file.PixelData ?? [];
    var result = new byte[CelGreyFile.HeaderSize + stride * file.Height];

    BinaryPrimitives.WriteUInt16LittleEndian(result, (ushort)file.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), (ushort)file.Height);
    pixels.AsSpan(0, Math.Min(pixels.Length, stride * file.Height)).CopyTo(result.AsSpan(CelGreyFile.HeaderSize));

    return result;
  }
}
