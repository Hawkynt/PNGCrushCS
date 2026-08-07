using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.PcpBitmap;

/// <summary>Assembles a .pcp bitmap: the largest coordinates, then one bit a pixel.</summary>
public static class PcpBitmapWriter {

  public static byte[] ToBytes(PcpBitmapFile file) {
    var stride = MonochromePage.BytesPerRow(file.Width);
    var pixels = file.PixelData ?? [];
    var result = new byte[PcpBitmapFile.HeaderSize + stride * file.Height];

    BinaryPrimitives.WriteUInt16BigEndian(result, (ushort)Math.Max(0, file.Width - 1));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(2), (ushort)Math.Max(0, file.Height - 1));
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4), file.Trailer);

    pixels.AsSpan(0, Math.Min(pixels.Length, stride * file.Height))
      .CopyTo(result.AsSpan(PcpBitmapFile.HeaderSize));

    return result;
  }
}
