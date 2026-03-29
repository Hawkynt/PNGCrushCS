using System;
using System.IO;

namespace FileFormat.Msp;

/// <summary>MSP v2 RLE compression. RunType==0 means run encoding, RunType!=0 means literal bytes.</summary>
internal static class MspRleCompressor {

  public static byte[] Compress(byte[] scanline) {
    if (scanline.Length == 0)
      return [];

    using var ms = new MemoryStream();
    var i = 0;

    while (i < scanline.Length) {
      var value = scanline[i];

      // Check for a run of identical bytes
      var runStart = i;
      while (i < scanline.Length && scanline[i] == value && i - runStart < 255)
        ++i;

      var runLength = i - runStart;

      if (runLength >= 3) {
        // Encode as run: RunType=0, RunCount, RunValue
        ms.WriteByte(0);
        ms.WriteByte((byte)runLength);
        ms.WriteByte(value);
      } else {
        // Collect literals: find how many non-run bytes to emit
        // Rewind - we consumed the initial bytes already, emit them as literals
        i = runStart;
        var literalStart = i;

        while (i < scanline.Length) {
          // Check if a run of 3+ identical bytes starts here
          if (i + 2 < scanline.Length && scanline[i] == scanline[i + 1] && scanline[i] == scanline[i + 2])
            break;

          ++i;
          if (i - literalStart >= 255)
            break;
        }

        var literalCount = i - literalStart;
        ms.WriteByte((byte)literalCount);
        ms.Write(scanline, literalStart, literalCount);
      }
    }

    return ms.ToArray();
  }

  public static byte[] Decompress(byte[] data, int bytesPerRow) {
    var output = new byte[bytesPerRow];
    var inIdx = 0;
    var outIdx = 0;

    while (inIdx < data.Length && outIdx < bytesPerRow) {
      var runType = data[inIdx++];

      if (runType == 0) {
        // Run encoding: next byte is count, byte after is the value to repeat
        if (inIdx + 1 >= data.Length)
          break;

        var runCount = data[inIdx++];
        var runValue = data[inIdx++];
        for (var j = 0; j < runCount && outIdx < bytesPerRow; ++j)
          output[outIdx++] = runValue;
      } else {
        // Literal bytes: runType is the count of literal bytes to copy
        var literalCount = runType;
        for (var j = 0; j < literalCount && inIdx < data.Length && outIdx < bytesPerRow; ++j)
          output[outIdx++] = data[inIdx++];
      }
    }

    return output;
  }
}
