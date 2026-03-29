using System;
using System.IO;

namespace FileFormat.DrHalo;

/// <summary>Assembles Dr. Halo CUT file bytes from pixel data.</summary>
public static class DrHaloWriter {

  public static byte[] ToBytes(DrHaloFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    using var ms = new MemoryStream();

    var header = new DrHaloHeader((short)width, (short)height, 0);
    Span<byte> headerBytes = stackalloc byte[DrHaloHeader.StructSize];
    header.WriteTo(headerBytes);
    ms.Write(headerBytes);

    Span<byte> lengthBytes = stackalloc byte[2];
    for (var row = 0; row < height; ++row) {
      var rowStart = row * width;
      var rowLength = Math.Min(width, pixelData.Length - rowStart);
      var rowSpan = pixelData.AsSpan(rowStart, rowLength);

      var compressed = DrHaloRleCompressor.CompressScanline(rowSpan);

      // Write 2-byte LE scanline length prefix
      lengthBytes[0] = (byte)(compressed.Length & 0xFF);
      lengthBytes[1] = (byte)((compressed.Length >> 8) & 0xFF);
      ms.Write(lengthBytes);

      ms.Write(compressed);
    }

    return ms.ToArray();
  }
}
