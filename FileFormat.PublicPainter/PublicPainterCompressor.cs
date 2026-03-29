using System;
using System.IO;

namespace FileFormat.PublicPainter;

/// <summary>Compressor/decompressor for Public Painter's byte-level RLE scheme.</summary>
/// <remarks>
/// If a byte has the high bit set, it's a run: count = byte &amp; 0x7F, value = next byte.
/// Otherwise it's a literal: count = byte, followed by that many literal bytes.
/// </remarks>
internal static class PublicPainterCompressor {

  /// <summary>Decompresses Public Painter RLE data to the expected output size.</summary>
  public static byte[] Decompress(ReadOnlySpan<byte> compressed, int expectedSize) {
    var output = new byte[expectedSize];
    var srcPos = 0;
    var dstPos = 0;

    while (dstPos < expectedSize && srcPos < compressed.Length) {
      var control = compressed[srcPos++];
      if ((control & 0x80) != 0) {
        // Run-length: count = control & 0x7F, value = next byte
        var count = control & 0x7F;
        if (srcPos >= compressed.Length)
          break;

        var value = compressed[srcPos++];
        var end = Math.Min(dstPos + count, expectedSize);
        for (var i = dstPos; i < end; ++i)
          output[i] = value;

        dstPos = end;
      } else {
        // Literal: count = control, followed by that many bytes
        var count = control;
        var end = Math.Min(dstPos + count, expectedSize);
        var available = Math.Min(count, compressed.Length - srcPos);
        for (var i = 0; i < available && dstPos + i < end; ++i)
          output[dstPos + i] = compressed[srcPos + i];

        srcPos += available;
        dstPos = end;
      }
    }

    return output;
  }

  /// <summary>Compresses data using Public Painter's byte-level RLE scheme.</summary>
  public static byte[] Compress(ReadOnlySpan<byte> data) {
    using var ms = new MemoryStream();
    var pos = 0;

    while (pos < data.Length) {
      // Check for a run of identical bytes
      var runStart = pos;
      while (pos + 1 < data.Length && data[pos] == data[pos + 1] && pos - runStart < 126)
        ++pos;

      var runLength = pos - runStart + 1;
      ++pos;

      if (runLength >= 3) {
        // Emit as run
        ms.WriteByte((byte)(0x80 | runLength));
        ms.WriteByte(data[runStart]);
      } else {
        // Collect literals
        var litStart = runStart;
        pos = runStart;

        while (pos < data.Length) {
          if (pos + 2 < data.Length && data[pos] == data[pos + 1] && data[pos] == data[pos + 2])
            break;

          ++pos;
          if (pos - litStart >= 127)
            break;
        }

        var litCount = pos - litStart;
        ms.WriteByte((byte)litCount);
        for (var i = 0; i < litCount; ++i)
          ms.WriteByte(data[litStart + i]);
      }
    }

    return ms.ToArray();
  }
}
