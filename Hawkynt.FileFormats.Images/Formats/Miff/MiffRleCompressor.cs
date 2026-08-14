using System;
using System.IO;

namespace FileFormat.Miff;

/// <summary>
/// MIFF run-length compression: every pixel packet is followed by a one-byte count, and the count
/// states one fewer than the number of pixels the packet stands for.
/// </summary>
/// <remarks>
/// The count byte is unconditional — a lone pixel is written as the pixel and a zero — and it is a
/// plain number rather than a flagged one. ImageMagick sizes its packet as the samples plus one for
/// exactly this reason, so the layout is fixed-width and needs no lookahead to parse.
/// <para/>
/// Reading the count only when its top bit is set is what this replaces, and it is the kind of wrong
/// that produces a picture rather than an error: a file whose first row is sixty-one blue pixels
/// states them as <c>00 00 00 00 ff ff 3c</c>, and 0x3C read as the next pixel's leading sample puts
/// every pixel after the first in the wrong place. Against ImageMagick's own reading of a 61x37
/// sample that measured 828 of 2257 pixels different.
/// </remarks>
internal static class MiffRleCompressor {

  /// <summary>The largest count a byte states: 255, which stands for 256 pixels.</summary>
  private const int _MAX_EXTRA = 255;

  public static byte[] Decompress(byte[] data, int bytesPerPixel, int pixelCount) {
    var outputSize = pixelCount * bytesPerPixel;
    var output = new byte[outputSize];
    var inIdx = 0;
    var outIdx = 0;

    while (outIdx < outputSize) {
      // The packet is the samples and the count together; a short tail states neither.
      if (inIdx + bytesPerPixel + 1 > data.Length)
        break;

      var packetStart = inIdx;
      inIdx += bytesPerPixel;
      var pixels = data[inIdx++] + 1;

      for (var r = 0; r < pixels && outIdx < outputSize; ++r)
        for (var b = 0; b < bytesPerPixel && outIdx < outputSize; ++b)
          output[outIdx++] = data[packetStart + b];
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

      // A run states one packet, and a count byte cannot say more than 256 pixels.
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

      for (var b = 0; b < bytesPerPixel; ++b)
        ms.WriteByte(data[runStart * bytesPerPixel + b]);

      ms.WriteByte((byte)(i - runStart - 1));
    }

    return ms.ToArray();
  }
}
