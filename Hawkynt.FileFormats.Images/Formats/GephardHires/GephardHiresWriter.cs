using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.GephardHires;

/// <summary>Assembles a Gephard Hires picture: the size, then the bitmap.</summary>
public static class GephardHiresWriter {

  public static byte[] ToBytes(GephardHiresFile file) {
    var stride = MonochromePage.BytesPerRow(file.Width);
    var pixels = file.PixelData ?? [];
    var result = new byte[GephardHiresFile.HeaderSize + stride * file.Height];

    BinaryPrimitives.WriteUInt16LittleEndian(result, (ushort)file.Width);
    result[2] = (byte)file.Height;
    pixels.AsSpan(0, Math.Min(pixels.Length, stride * file.Height))
      .CopyTo(result.AsSpan(GephardHiresFile.HeaderSize));

    return result;
  }
}
