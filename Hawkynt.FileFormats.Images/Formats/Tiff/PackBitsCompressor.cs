using System;
using System.IO;

namespace FileFormat.Tiff;

internal static class PackBitsCompressor {
  /// <summary>
  ///   Compresses data using the PackBits RLE algorithm (TIFF compression type 32773).
  /// </summary>
  public static byte[] Compress(ReadOnlySpan<byte> data) {
    if (data.Length == 0)
      return [];

    using var ms = new MemoryStream();
    var i = 0;

    while (i < data.Length)
      if (i + 1 < data.Length && data[i] == data[i + 1]) {
        // Run of identical bytes
        var runStart = i;
        var value = data[i];
        while (i < data.Length && i - runStart < 128 && data[i] == value)
          ++i;

        var count = i - runStart;
        ms.WriteByte((byte)(257 - count)); // -(count-1) as unsigned byte
        ms.WriteByte(value);
      } else {
        // Literal run of non-repeating bytes
        var literalStart = i;
        while (i < data.Length && i - literalStart < 128) {
          if (i + 1 < data.Length && data[i] == data[i + 1])
            break;
          ++i;
        }

        var count = i - literalStart;
        ms.WriteByte((byte)(count - 1));
        ms.Write(data.Slice(literalStart, count));
      }

    return ms.ToArray();
  }

  /// <summary>
  ///   Estimates PackBits compression ratio by scanning a sample of data.
  ///   Returns output/input ratio (lower is better; > 0.95 means PackBits is ineffective).
  /// </summary>
  public static double EstimateCompressionRatio(ReadOnlySpan<byte> data) {
    if (data.Length == 0)
      return 1.0;

    // Sample up to 4096 bytes from the start
    var sampleSize = Math.Min(data.Length, 4096);
    var sample = data[..sampleSize];

    var compressedSize = 0;
    var i = 0;
    while (i < sample.Length)
      if (i + 1 < sample.Length && sample[i] == sample[i + 1]) {
        var runStart = i;
        var value = sample[i];
        while (i < sample.Length && i - runStart < 128 && sample[i] == value)
          ++i;
        compressedSize += 2; // header + value
      } else {
        var literalStart = i;
        while (i < sample.Length && i - literalStart < 128) {
          if (i + 1 < sample.Length && sample[i] == sample[i + 1])
            break;
          ++i;
        }

        compressedSize += 1 + (i - literalStart); // header + literal bytes
      }

    return (double)compressedSize / sampleSize;
  }

  /// <summary>
  ///   Decompresses PackBits RLE data.
  /// </summary>
  public static byte[] Decompress(ReadOnlySpan<byte> data, int expectedLength) {
    var output = new byte[expectedLength];
    var outIdx = 0;
    var inIdx = 0;

    while (inIdx < data.Length && outIdx < expectedLength) {
      var header = (sbyte)data[inIdx++];

      if (header >= 0) {
        // Literal: copy (header + 1) bytes
        var count = header + 1;
        for (var j = 0; j < count && inIdx < data.Length && outIdx < expectedLength; ++j)
          output[outIdx++] = data[inIdx++];
      } else if (header != -128) {
        // Run: repeat next byte (-header + 1) times
        var count = -header + 1;
        if (inIdx >= data.Length)
          continue;

        var value = data[inIdx++];
        for (var j = 0; j < count && outIdx < expectedLength; ++j)
          output[outIdx++] = value;
      }
      // header == -128 (0x80): no-op
    }

    return output;
  }
}
