using System;
using System.Collections.Generic;

namespace FileFormat.AtariCompressed;

/// <summary>Assembles Atari Compressed Screen bytes from an <see cref="AtariCompressedFile"/>.</summary>
public static class AtariCompressedWriter {

  public static byte[] ToBytes(AtariCompressedFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _CompressRle(file.PixelData);
  }

  /// <summary>
  /// Compresses data using Atari RLE encoding.
  /// Runs of 2+ identical bytes: 0x80 | count, value (count up to 127).
  /// Non-repeated bytes: literal (must be &lt; 0x80; values &gt;= 0x80 are encoded as runs of 1).
  /// </summary>
  private static byte[] _CompressRle(byte[] data) {
    var result = new List<byte>();
    var i = 0;

    while (i < data.Length) {
      var b = data[i];

      // Count consecutive identical bytes
      var runLength = 1;
      while (i + runLength < data.Length && data[i + runLength] == b && runLength < 127)
        ++runLength;

      if (runLength >= 2 || b >= 0x80) {
        // Encode as RLE run
        result.Add((byte)(0x80 | runLength));
        result.Add(b);
        i += runLength;
      } else {
        // Literal byte (value < 0x80)
        result.Add(b);
        ++i;
      }
    }

    return result.ToArray();
  }
}
