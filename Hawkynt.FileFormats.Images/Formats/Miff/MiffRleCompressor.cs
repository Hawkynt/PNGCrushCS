using System;
using System.IO;

namespace FileFormat.Miff;

/// <summary>
/// MIFF RLE compression. Each pixel packet may be followed by a run-count byte (bit 7 set).
/// The run-count encodes (value &amp; 0x7F) additional copies of the preceding pixel packet.
/// A run-count byte of 0x80 means zero additional copies (used to disambiguate when the next
/// pixel's first byte has bit 7 set). Without a run-count byte, the next pixel follows immediately.
/// </summary>
internal static class MiffRleCompressor {

  /// <summary>Maximum additional copies in a single run byte: 0x7F = 127, so 128 total pixels.</summary>
  private const int _MAX_EXTRA = 127;

  public static byte[] Decompress(byte[] data, int bytesPerPixel, int pixelCount) {
    var outputSize = pixelCount * bytesPerPixel;
    var output = new byte[outputSize];
    var inIdx = 0;
    var outIdx = 0;

    while (inIdx < data.Length && outIdx < outputSize) {
      if (inIdx + bytesPerPixel > data.Length)
        break;

      // Read one pixel packet
      var packetStart = outIdx;
      for (var b = 0; b < bytesPerPixel && outIdx < outputSize; ++b)
        output[outIdx++] = data[inIdx++];

      // Check if next byte is a run count (bit 7 set)
      if (inIdx < data.Length && (data[inIdx] & 0x80) != 0) {
        var extraCopies = data[inIdx] & 0x7F;
        ++inIdx;

        for (var r = 0; r < extraCopies && outIdx < outputSize; ++r)
          for (var b = 0; b < bytesPerPixel && outIdx < outputSize; ++b)
            output[outIdx++] = output[packetStart + b];
      }
    }

    return output;
  }

  public static byte[] Compress(byte[] data, int bytesPerPixel) {
    if (data.Length == 0)
      return [];

    using var ms = new MemoryStream();
    var pixelCount = data.Length / bytesPerPixel;
    var i = 0;

    while (i < pixelCount) {
      var runStart = i;
      ++i;

      // Count consecutive identical pixel packets (cap at 1 + _MAX_EXTRA = 128 total)
      while (i < pixelCount && i - runStart < 1 + _MAX_EXTRA) {
        var match = true;
        for (var b = 0; b < bytesPerPixel; ++b) {
          if (data[i * bytesPerPixel + b] != data[runStart * bytesPerPixel + b]) {
            match = false;
            break;
          }
        }

        if (!match)
          break;

        ++i;
      }

      var runLength = i - runStart;

      // Write the pixel packet once
      for (var b = 0; b < bytesPerPixel; ++b)
        ms.WriteByte(data[runStart * bytesPerPixel + b]);

      if (runLength > 1) {
        // Emit run count: (runLength - 1) extra copies with bit 7 set
        ms.WriteByte((byte)(0x80 | (runLength - 1)));
      } else if (i < pixelCount && (data[i * bytesPerPixel] & 0x80) != 0) {
        // Single pixel followed by a pixel whose first byte has bit 7 set:
        // emit a no-op run count (0x80 = 0 extra copies) to prevent misinterpretation
        ms.WriteByte(0x80);
      }
    }

    return ms.ToArray();
  }
}
