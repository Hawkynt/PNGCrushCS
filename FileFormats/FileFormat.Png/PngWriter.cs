using System;
using System.Buffers;
using System.IO;
using System.IO.Hashing;
using System.Net;
using System.Text;

namespace FileFormat.Png;

/// <summary>Writes PNG files</summary>
public static class PngWriter {

  /// <summary>Write a PngFile to bytes using default ZLib compression</summary>
  public static byte[] ToBytes(PngFile file) {
    ArgumentNullException.ThrowIfNull(file);

    byte[] compressedIdat;
    if (file.PixelData != null) {
      using var ms = new MemoryStream();
      using (var zlib = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.SmallestSize, true)) {
        foreach (var scanline in file.PixelData) {
          zlib.WriteByte(0);
          zlib.Write(scanline);
        }
      }
      compressedIdat = ms.ToArray();
    } else {
      compressedIdat = [];
    }

    return Assemble(
      file.Width, file.Height, file.BitDepth, file.ColorType, file.InterlaceMethod,
      compressedIdat,
      file.Palette, file.PaletteCount, file.Transparency,
      file.ChunksBeforePlte, file.ChunksBetweenPlteAndIdat, file.ChunksAfterIdat
    );
  }

  /// <summary>Assemble a PNG file from pre-compressed IDAT data and metadata</summary>
  internal static byte[] Assemble(
    int width, int height,
    int bitDepth, PngColorType colorType, PngInterlaceMethod interlaceMethod,
    ReadOnlySpan<byte> compressedIdatData,
    byte[]? palette, int paletteCount,
    byte[]? tRNS,
    System.Collections.Generic.IReadOnlyList<PngChunk>? chunksBeforePlte = null,
    System.Collections.Generic.IReadOnlyList<PngChunk>? chunksBetweenPlteAndIdat = null,
    System.Collections.Generic.IReadOnlyList<PngChunk>? chunksAfterIdat = null) {
    const byte COMPRESSION_METHOD_DEFLATE = 0;
    const byte FILTER_METHOD_ADAPTIVE = 0;

    using var ms = new MemoryStream();

    Span<byte> sigBuf = stackalloc byte[PngSignatureHeader.StructSize];
    PngSignatureHeader.Expected.WriteTo(sigBuf);
    ms.Write(sigBuf);

    var ihdrRented = ArrayPool<byte>.Shared.Rent(PngIhdr.StructSize);
    try {
      var ihdr = new PngIhdr(width, height, (byte)bitDepth, (byte)colorType, COMPRESSION_METHOD_DEFLATE, FILTER_METHOD_ADAPTIVE, (byte)interlaceMethod);
      ihdr.WriteTo(ihdrRented.AsSpan(0, PngIhdr.StructSize));
      WriteChunk(ms, "IHDR", ihdrRented.AsSpan(0, PngIhdr.StructSize));
    } finally {
      ArrayPool<byte>.Shared.Return(ihdrRented);
    }

    if (chunksBeforePlte != null)
      foreach (var chunk in chunksBeforePlte)
        WriteChunk(ms, chunk.Type, chunk.Data);

    if (colorType == PngColorType.Palette && palette != null) {
      WriteChunk(ms, "PLTE", palette.AsSpan(0, paletteCount * 3));

      if (tRNS != null)
        WriteChunk(ms, "tRNS", tRNS);
    }

    if (colorType != PngColorType.Palette && tRNS != null)
      WriteChunk(ms, "tRNS", tRNS);

    if (chunksBetweenPlteAndIdat != null)
      foreach (var chunk in chunksBetweenPlteAndIdat)
        WriteChunk(ms, chunk.Type, chunk.Data);

    WriteChunk(ms, "IDAT", compressedIdatData);

    if (chunksAfterIdat != null)
      foreach (var chunk in chunksAfterIdat)
        WriteChunk(ms, chunk.Type, chunk.Data);

    WriteChunk(ms, "IEND", ReadOnlySpan<byte>.Empty);

    return ms.ToArray();
  }

  /// <summary>Write a PNG chunk with CRC32 checksum</summary>
  internal static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data) {
    using var bw = new BinaryWriter(stream, Encoding.ASCII, true);
    bw.Write(IPAddress.HostToNetworkOrder(data.Length));
    var typeBytes = Encoding.ASCII.GetBytes(type);
    stream.Write(typeBytes);
    stream.Write(data);

    var crc = new Crc32();
    crc.Append(typeBytes);
    crc.Append(data);
    bw.Write(IPAddress.HostToNetworkOrder((int)crc.GetCurrentHashAsUInt32()));
  }
}
