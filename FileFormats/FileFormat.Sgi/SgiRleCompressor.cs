using System;
using System.IO;

namespace FileFormat.Sgi;

/// <summary>SGI per-scanline RLE encoder and decoder.</summary>
internal static class SgiRleCompressor {

  public static byte[] Compress(byte[] scanline) {
    if (scanline.Length == 0)
      return [0];

    using var ms = new MemoryStream();
    var i = 0;

    while (i < scanline.Length) {
      // Check for a run of identical bytes
      var runStart = i;
      while (i + 1 < scanline.Length && i - runStart < 126 && scanline[i] == scanline[i + 1])
        ++i;

      if (i > runStart) {
        // Run packet: high bit clear, count in low 7 bits, repeat next byte
        ++i;
        var count = i - runStart;
        ms.WriteByte((byte)count);
        ms.WriteByte(scanline[runStart]);
      } else {
        // Raw packet: high bit set, count in low 7 bits, followed by raw bytes
        var rawStart = i;
        while (i < scanline.Length && i - rawStart < 127) {
          if (i + 1 < scanline.Length && scanline[i] == scanline[i + 1])
            break;
          ++i;
        }

        var count = i - rawStart;
        if (count == 0) {
          ms.WriteByte(0x81);
          ms.WriteByte(scanline[i]);
          ++i;
        } else {
          ms.WriteByte((byte)(0x80 | count));
          ms.Write(scanline, rawStart, count);
        }
      }
    }

    // Terminator
    ms.WriteByte(0);
    return ms.ToArray();
  }

  public static byte[] Decompress(byte[] data, int offset, int length, int expectedSize) {
    var output = new byte[expectedSize];
    var inIdx = offset;
    var end = offset + length;
    var outIdx = 0;

    while (inIdx < end && outIdx < expectedSize) {
      var control = data[inIdx++];
      if (control == 0)
        break;

      var count = control & 0x7F;
      if ((control & 0x80) != 0) {
        // Raw bytes
        for (var j = 0; j < count && inIdx < end && outIdx < expectedSize; ++j)
          output[outIdx++] = data[inIdx++];
      } else {
        // Run
        if (inIdx >= end)
          break;

        var value = data[inIdx++];
        for (var j = 0; j < count && outIdx < expectedSize; ++j)
          output[outIdx++] = value;
      }
    }

    return output;
  }
}
