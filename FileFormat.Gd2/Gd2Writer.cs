using System;
using System.Buffers.Binary;

namespace FileFormat.Gd2;

/// <summary>Assembles GD2 file bytes from pixel data.</summary>
public static class Gd2Writer {

  public static byte[] ToBytes(Gd2File file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height, file.Version, file.ChunkSize, file.Format);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height, int version, int chunkSize, int format) {
    var expectedPixelBytes = width * height * 4;
    var fileSize = Gd2File.HeaderSize + expectedPixelBytes;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    // Signature: "gd2\0"
    Gd2File.Signature.CopyTo(span);

    // Header fields (all uint16 BE)
    BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)version);
    BinaryPrimitives.WriteUInt16BigEndian(span[6..], (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(span[8..], (ushort)height);
    BinaryPrimitives.WriteUInt16BigEndian(span[10..], (ushort)chunkSize);
    BinaryPrimitives.WriteUInt16BigEndian(span[12..], (ushort)format);

    // Chunk counts: for a single chunk, xChunkCount=1, yChunkCount=1
    var xChunkCount = chunkSize > 0 ? (width + chunkSize - 1) / chunkSize : 1;
    var yChunkCount = chunkSize > 0 ? (height + chunkSize - 1) / chunkSize : 1;
    BinaryPrimitives.WriteUInt16BigEndian(span[14..], (ushort)xChunkCount);
    BinaryPrimitives.WriteUInt16BigEndian(span[16..], (ushort)yChunkCount);

    // Pixel data
    pixelData.AsSpan(0, Math.Min(expectedPixelBytes, pixelData.Length)).CopyTo(result.AsSpan(Gd2File.HeaderSize));

    return result;
  }
}
