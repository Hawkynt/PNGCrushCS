using System;

namespace FileFormat.Gd2;

/// <summary>Assembles GD2 file bytes from pixel data.</summary>
public static class Gd2Writer {

  public static byte[] ToBytes(Gd2File file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height, file.Version, file.ChunkSize, file.Format);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height, int version, int chunkSize, int format) {
    var expectedPixelBytes = width * height * 4;
    var fileSize = Gd2Header.StructSize + expectedPixelBytes;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    // Signature: "gd2\0"
    Gd2File.Signature.CopyTo(span);

    // Header fields (all uint16 BE)
    var xChunkCount = chunkSize > 0 ? (width + chunkSize - 1) / chunkSize : 1;
    var yChunkCount = chunkSize > 0 ? (height + chunkSize - 1) / chunkSize : 1;
    new Gd2Header((ushort)version, (ushort)width, (ushort)height, (ushort)chunkSize, (ushort)format, (ushort)xChunkCount, (ushort)yChunkCount).WriteTo(span);

    // Pixel data
    pixelData.AsSpan(0, Math.Min(expectedPixelBytes, pixelData.Length)).CopyTo(result.AsSpan(Gd2Header.StructSize));

    return result;
  }
}
