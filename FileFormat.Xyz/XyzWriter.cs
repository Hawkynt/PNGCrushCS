using System;
using System.IO;
using System.IO.Compression;

namespace FileFormat.Xyz;

/// <summary>Assembles RPG Maker XYZ file bytes from image data.</summary>
public static class XyzWriter {

  private const int _PALETTE_SIZE = 768;

  public static byte[] ToBytes(XyzFile file) => Assemble(file.Palette, file.PixelData, file.Width, file.Height);

  internal static byte[] Assemble(byte[] palette, byte[] pixelData, int width, int height) {
    using var ms = new MemoryStream();

    // Magic "XYZ1"
    ms.WriteByte((byte)'X');
    ms.WriteByte((byte)'Y');
    ms.WriteByte((byte)'Z');
    ms.WriteByte((byte)'1');

    // Width (uint16 LE)
    ms.WriteByte((byte)(width & 0xFF));
    ms.WriteByte((byte)((width >> 8) & 0xFF));

    // Height (uint16 LE)
    ms.WriteByte((byte)(height & 0xFF));
    ms.WriteByte((byte)((height >> 8) & 0xFF));

    // Compress palette + pixel data using zlib
    var uncompressed = new byte[_PALETTE_SIZE + width * height];
    var paletteLen = Math.Min(_PALETTE_SIZE, palette.Length);
    palette.AsSpan(0, paletteLen).CopyTo(uncompressed.AsSpan(0));

    var pixelLen = Math.Min(width * height, pixelData.Length);
    pixelData.AsSpan(0, pixelLen).CopyTo(uncompressed.AsSpan(_PALETTE_SIZE));

    // Zlib header (0x78 0x9C = default compression)
    ms.WriteByte(0x78);
    ms.WriteByte(0x9C);

    // DEFLATE compressed data
    using (var deflateStream = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
      deflateStream.Write(uncompressed, 0, uncompressed.Length);

    // Adler-32 checksum (big-endian)
    var adler = _ComputeAdler32(uncompressed);
    ms.WriteByte((byte)((adler >> 24) & 0xFF));
    ms.WriteByte((byte)((adler >> 16) & 0xFF));
    ms.WriteByte((byte)((adler >> 8) & 0xFF));
    ms.WriteByte((byte)(adler & 0xFF));

    return ms.ToArray();
  }

  private static uint _ComputeAdler32(byte[] data) {
    uint a = 1, b = 0;
    const uint mod = 65521;
    for (var i = 0; i < data.Length; ++i) {
      a = (a + data[i]) % mod;
      b = (b + a) % mod;
    }

    return (b << 16) | a;
  }
}
