using System;
using System.IO;

namespace FileFormat.IffDeep;

/// <summary>ByteRun1 (PackBits) compression for IFF DEEP BODY data.</summary>
internal static class ByteRun1Compressor {

  /// <summary>Compresses data using ByteRun1 encoding.</summary>
  public static byte[] Encode(ReadOnlySpan<byte> data) {
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
        ms.WriteByte((byte)(257 - count)); // -(count-1) as unsigned byte
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
        ms.Write(data.Slice(literalStart, count));
      }

    return ms.ToArray();
  }

  /// <summary>Decompresses ByteRun1-encoded data.</summary>
  public static byte[] Decode(ReadOnlySpan<byte> data, int expectedLength) {
    var output = new byte[expectedLength];
    var outIdx = 0;
    var inIdx = 0;

    while (inIdx < data.Length && outIdx < expectedLength) {
      var header = (sbyte)data[inIdx++];

      if (header >= 0) {
        var count = header + 1;
        for (var j = 0; j < count && inIdx < data.Length && outIdx < expectedLength; ++j)
          output[outIdx++] = data[inIdx++];
      } else if (header != -128) {
        var count = 1 - header;
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
