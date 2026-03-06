using System;
using System.IO;

namespace FileFormat.Pcx;

internal static class PcxRleCompressor {

  public static byte[] Compress(ReadOnlySpan<byte> scanline) {
    if (scanline.Length == 0)
      return [];

    using var ms = new MemoryStream();
    var i = 0;

    while (i < scanline.Length) {
      var value = scanline[i];
      var runStart = i;

      // Count run of identical bytes (max 63 per PCX spec)
      while (i < scanline.Length && i - runStart < 63 && scanline[i] == value)
        ++i;

      var count = i - runStart;

      if (count > 1 || value >= 0xC0) {
        // Encoded run: 0xC0 | count, then value
        ms.WriteByte((byte)(0xC0 | count));
        ms.WriteByte(value);
      } else {
        // Literal byte (only if < 0xC0)
        ms.WriteByte(value);
      }
    }

    return ms.ToArray();
  }

  public static byte[] Decompress(ReadOnlySpan<byte> data, int expectedLength) {
    var output = new byte[expectedLength];
    var inIdx = 0;
    var outIdx = 0;

    while (inIdx < data.Length && outIdx < expectedLength) {
      var b = data[inIdx++];
      if ((b & 0xC0) == 0xC0) {
        var count = b & 0x3F;
        if (inIdx >= data.Length)
          break;
        var value = data[inIdx++];
        for (var j = 0; j < count && outIdx < expectedLength; ++j)
          output[outIdx++] = value;
      } else {
        output[outIdx++] = b;
      }
    }

    return output;
  }

  public static double EstimateCompressionRatio(ReadOnlySpan<byte> data) {
    if (data.Length == 0)
      return 1.0;

    var sampleSize = Math.Min(data.Length, 4096);
    var sample = data[..sampleSize];
    var compressed = Compress(sample);
    return (double)compressed.Length / sampleSize;
  }
}
