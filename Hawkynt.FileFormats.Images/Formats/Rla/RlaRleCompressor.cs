using System;
using System.IO;

namespace FileFormat.Rla;

/// <summary>RLA per-channel RLE encoder and decoder.</summary>
/// <remarks>
/// RLE scheme: read a control byte.
/// If control > 128: run of (257 - control) copies of the next byte.
/// If control &lt;= 128: (control + 1) literal bytes follow.
/// </remarks>
internal static class RlaRleCompressor {

  public static byte[] Decompress(ReadOnlySpan<byte> data, int expectedLength) {
    var output = new byte[expectedLength];
    var inIdx = 0;
    var outIdx = 0;

    while (inIdx < data.Length && outIdx < expectedLength) {
      var control = data[inIdx++];
      if (control > 128) {
        var count = 257 - control;
        if (inIdx >= data.Length)
          break;

        var value = data[inIdx++];
        for (var j = 0; j < count && outIdx < expectedLength; ++j)
          output[outIdx++] = value;
      } else {
        var count = control + 1;
        for (var j = 0; j < count && inIdx < data.Length && outIdx < expectedLength; ++j)
          output[outIdx++] = data[inIdx++];
      }
    }

    return output;
  }

  public static byte[] Compress(ReadOnlySpan<byte> data) {
    if (data.Length == 0)
      return [];

    using var ms = new MemoryStream();
    var i = 0;

    while (i < data.Length) {
      var runStart = i;

      // Check for a run of identical bytes (max 127)
      while (i + 1 < data.Length && i - runStart < 126 && data[i] == data[i + 1])
        ++i;

      if (i > runStart) {
        ++i;
        var count = i - runStart;
        ms.WriteByte((byte)(257 - count));
        ms.WriteByte(data[runStart]);
      } else {
        // Literal run (max 128 bytes, control value 0..127 means 1..128 literals)
        var litStart = i;
        while (i < data.Length && i - litStart < 127) {
          if (i + 1 < data.Length && data[i] == data[i + 1])
            break;
          ++i;
        }

        var count = i - litStart;
        if (count == 0) {
          // Single byte that looks like start of a run but has no continuation
          ms.WriteByte(0);
          ms.WriteByte(data[i]);
          ++i;
        } else {
          ms.WriteByte((byte)(count - 1));
          for (var j = litStart; j < litStart + count; ++j)
            ms.WriteByte(data[j]);
        }
      }
    }

    return ms.ToArray();
  }
}
