using System;
using System.IO;

namespace FileFormat.Tga;

internal static class TgaRleCompressor {

  public static byte[] Compress(ReadOnlySpan<byte> scanline, int bytesPerPixel) {
    if (scanline.Length == 0)
      return [];

    using var ms = new MemoryStream();
    var pixelCount = scanline.Length / bytesPerPixel;
    var i = 0;

    while (i < pixelCount) {
      // Check for a run of identical pixels
      var runStart = i;
      while (i + 1 < pixelCount && i - runStart < 127 && _PixelsEqual(scanline, i, i + 1, bytesPerPixel))
        ++i;

      if (i > runStart) {
        // RLE packet: bit 7 set, lower 7 = count - 1
        ++i; // Include the pixel we stopped at
        var count = i - runStart;
        ms.WriteByte((byte)(0x80 | (count - 1)));
        ms.Write(scanline.Slice(runStart * bytesPerPixel, bytesPerPixel));
      } else {
        // Raw packet: collect non-repeating pixels
        var rawStart = i;
        while (i < pixelCount && i - rawStart < 128) {
          if (i + 1 < pixelCount && _PixelsEqual(scanline, i, i + 1, bytesPerPixel))
            break;
          ++i;
        }

        var count = i - rawStart;
        if (count == 0) {
          // Single pixel at end
          ms.WriteByte(0x00);
          ms.Write(scanline.Slice(i * bytesPerPixel, bytesPerPixel));
          ++i;
        } else {
          ms.WriteByte((byte)(count - 1));
          ms.Write(scanline.Slice(rawStart * bytesPerPixel, count * bytesPerPixel));
        }
      }
    }

    return ms.ToArray();
  }

  public static byte[] Decompress(ReadOnlySpan<byte> data, int expectedPixels, int bytesPerPixel) {
    var output = new byte[expectedPixels * bytesPerPixel];
    var inIdx = 0;
    var outPixel = 0;

    while (inIdx < data.Length && outPixel < expectedPixels) {
      var header = data[inIdx++];
      if ((header & 0x80) != 0) {
        // RLE packet
        var count = (header & 0x7F) + 1;
        if (inIdx + bytesPerPixel > data.Length)
          break;

        var pixel = data.Slice(inIdx, bytesPerPixel);
        inIdx += bytesPerPixel;
        for (var j = 0; j < count && outPixel < expectedPixels; ++j) {
          pixel.CopyTo(output.AsSpan(outPixel * bytesPerPixel));
          ++outPixel;
        }
      } else {
        // Raw packet
        var count = header + 1;
        for (var j = 0; j < count && outPixel < expectedPixels && inIdx + bytesPerPixel <= data.Length; ++j) {
          data.Slice(inIdx, bytesPerPixel).CopyTo(output.AsSpan(outPixel * bytesPerPixel));
          inIdx += bytesPerPixel;
          ++outPixel;
        }
      }
    }

    return output;
  }

  public static double EstimateCompressionRatio(ReadOnlySpan<byte> data, int bytesPerPixel) {
    if (data.Length == 0)
      return 1.0;

    var samplePixels = Math.Min(data.Length / bytesPerPixel, 1024);
    var sampleBytes = samplePixels * bytesPerPixel;
    var sample = data[..sampleBytes];

    var compressed = Compress(sample, bytesPerPixel);
    return (double)compressed.Length / sampleBytes;
  }

  private static bool _PixelsEqual(ReadOnlySpan<byte> data, int pixelA, int pixelB, int bytesPerPixel) {
    var a = data.Slice(pixelA * bytesPerPixel, bytesPerPixel);
    var b = data.Slice(pixelB * bytesPerPixel, bytesPerPixel);
    return a.SequenceEqual(b);
  }
}
