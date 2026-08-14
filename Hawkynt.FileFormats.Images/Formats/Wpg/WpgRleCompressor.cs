using System;
using System.IO;

namespace FileFormat.Wpg;

/// <summary>WPG RLE compression. Bit 7 set = repeat run, otherwise literal copy.</summary>
internal static class WpgRleCompressor {

  /// <summary>Codes a bitmap one scanline at a time, which is how WPG stores one.</summary>
  /// <remarks>
  /// Every row is its own little stream. No run carries across the end of one, so a reader that has
  /// finished a row is standing on a control byte and not in the middle of a run. Coding the whole
  /// raster in a single pass would merge the runs over that boundary and leave every row after the
  /// first out of step.
  /// </remarks>
  public static byte[] CompressRows(byte[] data, int stride, int height) {
    ArgumentNullException.ThrowIfNull(data);

    using var ms = new MemoryStream();
    for (var y = 0; y < height; ++y) {
      var from = y * stride;
      if (from + stride > data.Length)
        break;

      var row = Compress(data[from..(from + stride)]);
      ms.Write(row, 0, row.Length);
    }

    return ms.ToArray();
  }

  public static byte[] Compress(byte[] data) {
    if (data.Length == 0)
      return [];

    using var ms = new MemoryStream();
    var i = 0;

    while (i < data.Length) {
      var value = data[i];

      // Count run length
      var runStart = i;
      while (i < data.Length && i - runStart < 127 && data[i] == value)
        ++i;

      var runLen = i - runStart;

      if (runLen >= 3) {
        // Encode as repeat run: (0x80 | count), value
        ms.WriteByte((byte)(0x80 | runLen));
        ms.WriteByte(value);
      } else {
        // Collect literals
        i = runStart;
        var litStart = i;
        while (i < data.Length && i - litStart < 127) {
          // Check if a run of 3+ starts here
          if (i + 2 < data.Length && data[i] == data[i + 1] && data[i] == data[i + 2])
            break;

          ++i;
        }

        var litLen = i - litStart;
        ms.WriteByte((byte)litLen);
        ms.Write(data, litStart, litLen);
      }
    }

    return ms.ToArray();
  }

  public static byte[] Decompress(byte[] data, int expectedSize) {
    var output = new byte[expectedSize];
    var inIdx = 0;
    var outIdx = 0;

    while (inIdx < data.Length && outIdx < expectedSize) {
      var control = data[inIdx++];

      if ((control & 0x80) != 0) {
        // Repeat run
        var count = control & 0x7F;
        if (inIdx >= data.Length)
          break;

        var value = data[inIdx++];
        for (var j = 0; j < count && outIdx < expectedSize; ++j)
          output[outIdx++] = value;
      } else {
        // Literal copy
        var count = control;
        for (var j = 0; j < count && inIdx < data.Length && outIdx < expectedSize; ++j)
          output[outIdx++] = data[inIdx++];
      }
    }

    return output;
  }
}
