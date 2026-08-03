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
  /// Run-length codes the bitmap the way a PGC states it: a byte with the top bit set repeats the
  /// byte after it, and one without says how many bytes follow that are taken as they stand.
  /// </summary>
  /// <remarks>
  /// This wrote a different encoding altogether — a zero introducing a count and a value — which is
  /// what the reader used to expect, so the two agreed with each other and with no real file. Both
  /// are corrected together, and the signature is written where it was previously left out.
  /// <para/>
  /// A run and a literal stretch each carry one byte of overhead, so a run is worth coding from two
  /// alike upwards; both counts stop at 127, which is as far as seven bits reach.
  /// </remarks>
  private static byte[] _Compress(byte[] bitmap) {
    var result = new List<byte>(bitmap.Length);
    foreach (var b in PortfolioGraphicsFile.PgcSignature)
      result.Add(b);

    for (var at = 0; at < bitmap.Length;) {
      var run = 1;
      while (run < 0x7F && at + run < bitmap.Length && bitmap[at + run] == bitmap[at])
        ++run;

      if (run > 1) {
        result.Add((byte)(0x80 | run));
        result.Add(bitmap[at]);
        at += run;
        continue;
      }

      // A stretch of bytes with no two alike in it, which is cheaper written out than coded.
      var literal = 1;
      while (literal < 0x7F && at + literal + 1 < bitmap.Length && bitmap[at + literal] != bitmap[at + literal + 1])
        ++literal;

      result.Add((byte)literal);
      for (var i = 0; i < literal; ++i)
        result.Add(bitmap[at + i]);

      at += literal;
    }

    return result.ToArray();
  }
}
