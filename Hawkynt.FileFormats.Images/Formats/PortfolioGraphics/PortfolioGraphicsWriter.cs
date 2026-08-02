using System;
using System.Collections.Generic;

namespace FileFormat.PortfolioGraphics;

/// <summary>Assembles Atari Portfolio PGF format bytes from a <see cref="PortfolioGraphicsFile"/>.</summary>
public static class PortfolioGraphicsWriter {

  public static byte[] ToBytes(PortfolioGraphicsFile file) {
    var bitmap = file.PixelData ?? [];
    if (file.Compressed)
      return _Compress(bitmap);

    var result = new byte[PortfolioGraphicsFile.PgfFileSize];
    bitmap.AsSpan(0, Math.Min(bitmap.Length, result.Length)).CopyTo(result);
    return result;
  }

  /// <summary>
  /// Run-length codes the bitmap the way a PGC states it: a zero introduces a count and a value,
  /// anything else stands for itself.
  /// </summary>
  /// <remarks>
  /// A zero byte cannot stand for itself, being the introducer, so it is always spelled as a run —
  /// which is why the shortest run worth coding is two rather than three.
  /// </remarks>
  private static byte[] _Compress(byte[] bitmap) {
    var result = new List<byte>(bitmap.Length);

    for (var at = 0; at < bitmap.Length;) {
      var value = bitmap[at];

      var run = 1;
      while (run < 255 && at + run < bitmap.Length && bitmap[at + run] == value)
        ++run;

      if (run > 2 || value == 0) {
        result.Add(0);
        result.Add((byte)run);
        result.Add(value);
      } else
        for (var i = 0; i < run; ++i)
          result.Add(value);

      at += run;
    }

    return result.ToArray();
  }
}
