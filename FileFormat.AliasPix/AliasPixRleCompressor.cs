using System;
using System.IO;

namespace FileFormat.AliasPix;

/// <summary>Alias/Wavefront PIX per-scanline RLE compression. Packets are (count, B, G, R[, A]).</summary>
internal static class AliasPixRleCompressor {

  public static byte[] Decompress(ReadOnlySpan<byte> data, int width, int height, int bytesPerPixel) {
    var pixelData = new byte[width * height * bytesPerPixel];
    var inIdx = 0;
    var outIdx = 0;
    var packetSize = 1 + bytesPerPixel;

    for (var y = 0; y < height; ++y) {
      var pixelsWritten = 0;
      while (pixelsWritten < width) {
        if (inIdx + packetSize > data.Length)
          throw new InvalidDataException("Unexpected end of RLE data.");

        var count = data[inIdx];
        if (count == 0)
          count = 1;

        var remaining = width - pixelsWritten;
        var actualCount = Math.Min(count, remaining);

        for (var i = 0; i < actualCount; ++i) {
          for (var c = 0; c < bytesPerPixel; ++c)
            pixelData[outIdx + c] = data[inIdx + 1 + c];

          outIdx += bytesPerPixel;
        }

        pixelsWritten += actualCount;
        inIdx += packetSize;
      }
    }

    return pixelData;
  }

  public static byte[] Compress(ReadOnlySpan<byte> pixelData, int width, int height, int bytesPerPixel) {
    if (pixelData.Length == 0)
      return [];

    using var ms = new MemoryStream();

    for (var y = 0; y < height; ++y) {
      var rowStart = y * width * bytesPerPixel;
      var x = 0;

      while (x < width) {
        var pixelOffset = rowStart + x * bytesPerPixel;
        var runLength = 1;

        while (x + runLength < width && runLength < 255) {
          var nextOffset = rowStart + (x + runLength) * bytesPerPixel;
          var same = true;
          for (var c = 0; c < bytesPerPixel; ++c) {
            if (pixelData[nextOffset + c] != pixelData[pixelOffset + c]) {
              same = false;
              break;
            }
          }

          if (!same)
            break;

          ++runLength;
        }

        ms.WriteByte((byte)runLength);
        for (var c = 0; c < bytesPerPixel; ++c)
          ms.WriteByte(pixelData[pixelOffset + c]);

        x += runLength;
      }
    }

    return ms.ToArray();
  }
}
