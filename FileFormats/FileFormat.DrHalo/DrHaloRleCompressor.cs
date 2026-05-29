using System;
using System.IO;

namespace FileFormat.DrHalo;

/// <summary>Dr. Halo CUT per-scanline RLE compressor/decompressor using (count, value) pairs.</summary>
internal static class DrHaloRleCompressor {

  public static byte[] CompressScanline(ReadOnlySpan<byte> row) {
    if (row.Length == 0)
      return [];

    using var ms = new MemoryStream();
    var i = 0;

    while (i < row.Length) {
      var value = row[i];
      var runStart = i;
      while (i < row.Length && i - runStart < 255 && row[i] == value)
        ++i;

      var count = i - runStart;
      ms.WriteByte((byte)count);
      ms.WriteByte(value);
    }

    return ms.ToArray();
  }

  public static byte[] DecompressScanline(ReadOnlySpan<byte> data, int width) {
    var output = new byte[width];
    var inIdx = 0;
    var outIdx = 0;

    while (inIdx + 1 < data.Length && outIdx < width) {
      var count = data[inIdx++];
      if (count == 0)
        break;

      var value = data[inIdx++];
      for (var j = 0; j < count && outIdx < width; ++j)
        output[outIdx++] = value;
    }

    return output;
  }
}
