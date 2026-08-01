using System;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Brus;

/// <summary>Assembles BRUS picture bytes.</summary>
public static class BrusWriter {

  public static byte[] ToBytes(BrusFile file) {
    var columns = file.Columns;
    var height = file.Height;
    var result = new List<byte>(BrusFile.StreamOffset + columns * height);

    // Where the picture is loaded, then the four letters that name it. The bytes at six, ten and
    // eleven are what a reader checks after them, and they are what a BRUS file has there.
    result.Add(0x00);
    result.Add(0x1C);
    result.AddRange(Encoding.ASCII.GetBytes(BrusFile.Signature));
    result.Add(4);
    result.Add(0);
    result.Add(0);
    result.Add(0);
    result.Add(1);
    result.Add(2);
    result.Add((byte)columns);
    result.Add((byte)height);
    result.Add((byte)(height >> 8));
    result.Add(0);
    result.Add(0);
    result.Add(0);

    _Pack(result, file.Bitmap ?? []);

    if (file.Colors is not { } colors)
      return result.ToArray();

    // The colour chunk is packed a band at a time rather than as one stream, because the decoder
    // unpacks one band per eight rows and starts each where the last one stopped.
    result.AddRange(Encoding.ASCII.GetBytes("COLR"));
    var bandSize = columns << 1;
    for (var at = 0; at < colors.Length; at += bandSize)
      _Pack(result, colors.AsSpan(at, Math.Min(bandSize, colors.Length - at)));

    return result.ToArray();
  }

  /// <summary>
  /// Packs a block: a byte under 128 introduces that many literals, one above it repeats the next
  /// byte that many times less 128.
  /// </summary>
  /// <remarks>
  /// A run of two costs the same either way and a run of one costs more as a run, so only three or
  /// more are worth coding; below that the bytes go out as literals. The literal count tops out at
  /// 127 and the repeat count at 127 as well, both being what the byte can hold.
  /// </remarks>
  private static void _Pack(List<byte> target, ReadOnlySpan<byte> source) {
    var at = 0;

    while (at < source.Length) {
      var run = 1;
      while (at + run < source.Length && run < 127 && source[at + run] == source[at])
        ++run;

      if (run >= 3) {
        target.Add((byte)(128 + run));
        target.Add(source[at]);
        at += run;
        continue;
      }

      // Gather literals until a run worth coding starts.
      var start = at;
      while (at < source.Length && at - start < 127) {
        var ahead = 1;
        while (at + ahead < source.Length && ahead < 3 && source[at + ahead] == source[at])
          ++ahead;

        if (ahead >= 3)
          break;

        ++at;
      }

      target.Add((byte)(at - start));
      for (var i = start; i < at; ++i)
        target.Add(source[i]);
    }
  }
}
