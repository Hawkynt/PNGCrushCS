using System;
using System.IO;

namespace FileFormat.Palm;

/// <summary>Palm OS Bitmap RLE compression (type 2): count/value byte pairs per scanline.</summary>
internal static class PalmRleCompressor {

  public static byte[] Decompress(ReadOnlySpan<byte> data, int bytesPerRow, int height) {
    var totalBytes = bytesPerRow * height;
    var output = new byte[totalBytes];
    var inIdx = 0;
    var outIdx = 0;

    for (var row = 0; row < height && inIdx < data.Length; ++row) {
      var rowStart = outIdx;
      while (outIdx - rowStart < bytesPerRow && inIdx + 1 < data.Length) {
        var count = data[inIdx++];
        var value = data[inIdx++];
        for (var j = 0; j < count && outIdx - rowStart < bytesPerRow; ++j)
          output[outIdx++] = value;
      }

      // Advance to next row boundary if not already there
      outIdx = rowStart + bytesPerRow;
    }

    return output;
  }

  public static byte[] Compress(ReadOnlySpan<byte> data, int bytesPerRow, int height) {
    using var ms = new MemoryStream();

    for (var row = 0; row < height; ++row) {
      var rowOffset = row * bytesPerRow;
      var i = 0;

      while (i < bytesPerRow) {
        var value = data[rowOffset + i];
        var count = 1;

        while (i + count < bytesPerRow && count < 255 && data[rowOffset + i + count] == value)
          ++count;

        ms.WriteByte((byte)count);
        ms.WriteByte(value);
        i += count;
      }
    }

    return ms.ToArray();
  }
}
