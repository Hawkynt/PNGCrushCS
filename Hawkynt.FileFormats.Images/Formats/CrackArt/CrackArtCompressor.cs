using System;
using System.IO;

namespace FileFormat.CrackArt;

/// <summary>PackBits-style RLE compressor/decompressor for CrackArt image data.</summary>
internal static class CrackArtCompressor {

  /// <summary>Compresses data using the PackBits RLE algorithm.</summary>
  public static byte[] Compress(byte[] data) {
    if (data.Length == 0)
      return [];

    using var ms = new MemoryStream();
    var i = 0;

    while (i < data.Length)
      if (i + 1 < data.Length && data[i] == data[i + 1]) {
        var runStart = i;
        var value = data[i];
        while (i < data.Length && i - runStart < 128 && data[i] == value)
          ++i;

        var count = i - runStart;
        ms.WriteByte((byte)(257 - count));
        ms.WriteByte(value);
      } else {
        var literalStart = i;
        while (i < data.Length && i - literalStart < 128) {
          if (i + 1 < data.Length && data[i] == data[i + 1])
            break;
          ++i;
        }

        var count = i - literalStart;
        ms.WriteByte((byte)(count - 1));
        ms.Write(data, literalStart, count);
      }

    return ms.ToArray();
  }

  /// <summary>Decompresses PackBits RLE data.</summary>
  public static byte[] Decompress(byte[] data, int expectedSize) {
    var output = new byte[expectedSize];
    var outIdx = 0;
    var inIdx = 0;

    while (inIdx < data.Length && outIdx < expectedSize) {
      var header = (sbyte)data[inIdx++];

      if (header >= 0) {
        var count = header + 1;
        for (var j = 0; j < count && inIdx < data.Length && outIdx < expectedSize; ++j)
          output[outIdx++] = data[inIdx++];
      } else if (header != -128) {
        var count = -header + 1;
        if (inIdx >= data.Length)
          continue;

        var value = data[inIdx++];
        for (var j = 0; j < count && outIdx < expectedSize; ++j)
          output[outIdx++] = value;
      }
      // header == -128 (0x80): no-op
    }

    return output;
  }
}
