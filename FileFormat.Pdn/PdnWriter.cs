using System;
using System.IO;
using System.IO.Compression;

namespace FileFormat.Pdn;

/// <summary>Assembles PDN file bytes from pixel data.</summary>
public static class PdnWriter {

  public static byte[] ToBytes(PdnFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height, file.Version);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height, ushort version = 3) {
    using var output = new MemoryStream();

    // Write header (16 bytes)
    Span<byte> headerBytes = stackalloc byte[PdnHeader.StructSize];
    var header = new PdnHeader(
      (byte)'P', (byte)'D', (byte)'N', (byte)'3',
      version, 0,
      (uint)width, (uint)height
    );
    header.WriteTo(headerBytes);
    output.Write(headerBytes);

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
