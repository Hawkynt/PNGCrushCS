using System;
using System.IO;

namespace FileFormat.IffRgb8;

/// <summary>Modified ByteRun1 compression operating on 4-byte pixel groups for IFF RGB8 BODY data.</summary>
internal static class Rgb8ByteRun1Compressor {

  private const int _GROUP_SIZE = 4;

  /// <summary>Compresses 4-byte pixel groups using modified ByteRun1 encoding.</summary>
  public static byte[] Encode(ReadOnlySpan<byte> data) {
    if (data.Length == 0)
      return [];

    var groupCount = data.Length / _GROUP_SIZE;
    using var ms = new MemoryStream();
    var i = 0;

    while (i < groupCount)
      if (i + 1 < groupCount && _GroupsEqual(data, i, i + 1)) {
        var runStart = i;
        while (i < groupCount && i - runStart < 128 && _GroupsEqual(data, runStart, i))
          ++i;

        var count = i - runStart;
        ms.WriteByte((byte)(257 - count)); // -(count-1) as unsigned byte
        ms.Write(data.Slice(runStart * _GROUP_SIZE, _GROUP_SIZE));
      } else {
        var literalStart = i;
        while (i < groupCount && i - literalStart < 128) {
          if (i + 1 < groupCount && _GroupsEqual(data, i, i + 1))
            break;
          ++i;
        }

        var count = i - literalStart;
        ms.WriteByte((byte)(count - 1));
        ms.Write(data.Slice(literalStart * _GROUP_SIZE, count * _GROUP_SIZE));
      }

    return ms.ToArray();
  }

  /// <summary>Decompresses modified ByteRun1-encoded data into 4-byte pixel groups.</summary>
  public static byte[] Decode(ReadOnlySpan<byte> data, int expectedLength) {
    var output = new byte[expectedLength];
    var outIdx = 0;
    var inIdx = 0;

    while (inIdx < data.Length && outIdx < expectedLength) {
      var header = (sbyte)data[inIdx++];

      if (header >= 0) {
        // Literal: copy (header + 1) groups of 4 bytes
        var count = (header + 1) * _GROUP_SIZE;
        for (var j = 0; j < count && inIdx < data.Length && outIdx < expectedLength; ++j)
          output[outIdx++] = data[inIdx++];
      } else if (header != -128) {
        // Run: repeat next 4-byte group (-header + 1) times
        var repeatCount = 1 - header;
        if (inIdx + _GROUP_SIZE > data.Length)
          continue;

        var groupStart = inIdx;
        inIdx += _GROUP_SIZE;
        for (var r = 0; r < repeatCount && outIdx + _GROUP_SIZE <= expectedLength; ++r)
          for (var b = 0; b < _GROUP_SIZE; ++b)
            output[outIdx++] = data[groupStart + b];
      }
      // header == -128 (0x80): no-op
    }

    return output;
  }

  private static bool _GroupsEqual(ReadOnlySpan<byte> data, int groupA, int groupB) {
    var a = data.Slice(groupA * _GROUP_SIZE, _GROUP_SIZE);
    var b = data.Slice(groupB * _GROUP_SIZE, _GROUP_SIZE);
    return a.SequenceEqual(b);
  }
}
