using System;
using System.Buffers.Binary;

namespace FileFormat.GrfBitmap;

/// <summary>Assembles a .grf bitmap: the stated length, the load address, then the bits.</summary>
public static class GrfBitmapWriter {

  public static byte[] ToBytes(GrfBitmapFile file) {
    var length = GrfBitmapFile.BytesPerRow * file.Height;
    var pixels = file.PixelData ?? [];
    var result = new byte[GrfBitmapFile.HeaderSize + length];

    BinaryPrimitives.WriteUInt16LittleEndian(result, (ushort)length);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), file.LoadAddress);
    pixels.AsSpan(0, Math.Min(pixels.Length, length)).CopyTo(result.AsSpan(GrfBitmapFile.HeaderSize));

    return result;
  }
}
