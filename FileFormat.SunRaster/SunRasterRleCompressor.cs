using System;
using System.IO;

namespace FileFormat.SunRaster;

/// <summary>Sun Raster RLE compression. Escape byte is 0x80.</summary>
internal static class SunRasterRleCompressor {

  private const byte _ESCAPE = 0x80;

  public static byte[] Compress(byte[] data) {
    if (data.Length == 0)
      return [];

    using var ms = new MemoryStream();
    var i = 0;

    while (i < data.Length) {
      var value = data[i];

      // Check for a run
      var runStart = i;
      while (i < data.Length && i - runStart < 256 && data[i] == value)
        ++i;

      var count = i - runStart;

      if (count > 2) {
        // Encode as run: 0x80, (count-1), value
        // count can be up to 256, but (count-1) fits in byte for count 1..256
        // Split into chunks of 256 if needed
        var remaining = count;
        while (remaining > 0) {
          var chunk = Math.Min(remaining, 256);
          ms.WriteByte(_ESCAPE);
          ms.WriteByte((byte)(chunk - 1));
          ms.WriteByte(value);
          remaining -= chunk;
        }
      } else {
        // Emit literals
        for (var j = runStart; j < runStart + count; ++j) {
          var b = data[j];
          if (b == _ESCAPE) {
            ms.WriteByte(_ESCAPE);
            ms.WriteByte(0x00); // literal 0x80
          } else
            ms.WriteByte(b);
        }
      }
    }

    return ms.ToArray();
  }

  public static byte[] Decompress(byte[] data, int expectedSize) {
    var output = new byte[expectedSize];
    var inIdx = 0;
    var outIdx = 0;

    while (inIdx < data.Length && outIdx < expectedSize) {
      var b = data[inIdx++];
      if (b == _ESCAPE) {
        if (inIdx >= data.Length)
          break;

        var countByte = data[inIdx++];
        if (countByte == 0x00) {
          // Literal 0x80
          output[outIdx++] = _ESCAPE;
        } else {
          // Run: repeat value (countByte + 1) times
          if (inIdx >= data.Length)
            break;

          var value = data[inIdx++];
          var count = countByte + 1;
          for (var j = 0; j < count && outIdx < expectedSize; ++j)
            output[outIdx++] = value;
        }
      } else
        output[outIdx++] = b;
    }

    return output;
  }
}
