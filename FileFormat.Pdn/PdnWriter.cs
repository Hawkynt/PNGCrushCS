using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace FileFormat.Pdn;

/// <summary>Assembles PDN file bytes from pixel data.</summary>
public static class PdnWriter {

  private static readonly byte[] _MAGIC = "PDN3"u8.ToArray();

  public static byte[] ToBytes(PdnFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height, file.Version);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height, ushort version = 3) {
    using var output = new MemoryStream();

    // Write header (16 bytes)
    Span<byte> header = stackalloc byte[PdnReader.HEADER_SIZE];
    _MAGIC.CopyTo(header);
    BinaryPrimitives.WriteUInt16LittleEndian(header[4..], version);
    BinaryPrimitives.WriteUInt16LittleEndian(header[6..], 0); // reserved
    BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)width);
    BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)height);
    output.Write(header);

    // Write gzip-compressed BGRA32 pixel data
    var expectedPixelBytes = width * height * 4;
    var dataToCompress = pixelData.Length >= expectedPixelBytes
      ? pixelData.AsSpan(0, expectedPixelBytes)
      : pixelData.AsSpan();

    using (var gzipStream = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
      gzipStream.Write(dataToCompress);

    return output.ToArray();
  }
}
