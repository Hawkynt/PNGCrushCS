using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.SpookySpritesFalcon;

/// <summary>RLE compressor/decompressor for Spooky Sprites Falcon format.
/// Scheme: signed byte count. Positive = literal run (count*2 bytes). Negative = repeat run (|count| times, 2 bytes). Zero = end.</summary>
internal static class SpookySpritesFalconRleCompressor {

  /// <summary>Decompresses RLE-encoded pixel data into raw RGB565 pixels.</summary>
  public static byte[] Decompress(ReadOnlySpan<byte> compressed, int expectedPixelCount) {
    var result = new byte[expectedPixelCount * 2];
    var srcPos = 0;
    var dstPos = 0;

    while (srcPos < compressed.Length && dstPos < result.Length) {
      var count = (sbyte)compressed[srcPos];
      ++srcPos;

      if (count == 0)
        break;

      if (count > 0) {
        var literalBytes = count * 2;
        var available = Math.Min(literalBytes, compressed.Length - srcPos);
        var writable = Math.Min(available, result.Length - dstPos);
        compressed.Slice(srcPos, writable).CopyTo(result.AsSpan(dstPos));
        srcPos += available;
        dstPos += writable;
      } else {
        var repeatCount = -count;
        if (srcPos + 1 >= compressed.Length)
          break;

        var hi = compressed[srcPos];
        var lo = compressed[srcPos + 1];
        srcPos += 2;

        for (var i = 0; i < repeatCount && dstPos + 1 < result.Length; ++i) {
          result[dstPos] = hi;
          result[dstPos + 1] = lo;
          dstPos += 2;
        }
      }
    }

    return result;
  }

  /// <summary>Compresses raw RGB565 pixel data using RLE encoding.</summary>
  public static byte[] Compress(ReadOnlySpan<byte> pixelData) {
    using var output = new MemoryStream();
    var pixelCount = pixelData.Length / 2;
    var pos = 0;

    while (pos < pixelCount) {
      var runStart = pos;

      // Check for repeat run
      if (pos + 1 < pixelCount) {
        var hi = pixelData[pos * 2];
        var lo = pixelData[pos * 2 + 1];
        var runLen = 1;

        while (runStart + runLen < pixelCount && runLen < 127) {
          var nextHi = pixelData[(runStart + runLen) * 2];
          var nextLo = pixelData[(runStart + runLen) * 2 + 1];
          if (nextHi != hi || nextLo != lo)
            break;
          ++runLen;
        }

        if (runLen >= 2) {
          output.WriteByte((byte)(-(sbyte)runLen));
          output.WriteByte(hi);
          output.WriteByte(lo);
          pos += runLen;
          continue;
        }
      }

      // Literal run
      var literalStart = pos;
      var literalLen = 0;
      while (pos < pixelCount && literalLen < 127) {
        if (pos + 1 < pixelCount) {
          var hi = pixelData[pos * 2];
          var lo = pixelData[pos * 2 + 1];
          var nextHi = pixelData[(pos + 1) * 2];
          var nextLo = pixelData[(pos + 1) * 2 + 1];
          if (hi == nextHi && lo == nextLo)
            break;
        }
        ++pos;
        ++literalLen;
      }

      if (literalLen == 0) {
        // Single pixel that starts a repeat - emit as literal
        ++pos;
        literalLen = 1;
      }

      output.WriteByte((byte)literalLen);
      output.Write(pixelData.Slice(literalStart * 2, literalLen * 2));
    }

    // End marker
    output.WriteByte(0);
    return output.ToArray();
  }
}
