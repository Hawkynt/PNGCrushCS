using System;
using System.Collections.Generic;

namespace FileFormat.XlPaint;

/// <summary>Assembles XL-Paint (.xlp) picture bytes.</summary>
/// <remarks>
/// The unmarked form: four colour registers, then the packed stream. A marked file says the same
/// things and adds four bytes of signature to say so, which buys nothing a reader of either form
/// does not already have.
/// </remarks>
public static class XlPaintWriter {

  public static byte[] ToBytes(XlPaintFile file) {
    var registers = file.Registers ?? new byte[4];
    var result = new List<byte>(8000);

    // Stored as PF0, PF1, PF2 and then the background, which is the reverse of the order the
    // decoding helpers take them in.
    result.Add(registers.Length > 1 ? registers[1] : (byte)0);
    result.Add(registers.Length > 2 ? registers[2] : (byte)0);
    result.Add(registers.Length > 3 ? registers[3] : (byte)0);
    result.Add(registers.Length > 0 ? registers[0] : (byte)0);

    _Pack(result, file.ScreenData ?? [], file.Height * 2 * XlPaintFile.Stride);
    return result.ToArray();
  }

  /// <summary>Packs both screens down their columns.</summary>
  /// <remarks>
  /// Column by column rather than row by row, because that is the order the decoder walks: the two
  /// interlaced screens are far more alike down a column of one than across a row of both.
  /// </remarks>
  private static void _Pack(List<byte> target, ReadOnlySpan<byte> screens, int end) {
    // The bytes in the order the stream states them, which is not the order they are stored in.
    var ordered = new byte[end];
    var at = 0;
    for (var column = 0; column < XlPaintFile.Stride; ++column)
    for (var position = column; position < end; position += XlPaintFile.Stride)
      ordered[at++] = position < screens.Length ? screens[position] : (byte)0;

    var i = 0;
    while (i < ordered.Length) {
      var run = 1;
      while (i + run < ordered.Length && run < _MaxCount && ordered[i + run] == ordered[i])
        ++run;

      if (run >= 3) {
        _WriteCount(target, run, repeated: true);
        target.Add(ordered[i]);
        i += run;
        continue;
      }

      var start = i;
      while (i < ordered.Length && i - start < _MaxCount) {
        var ahead = 1;
        while (i + ahead < ordered.Length && ahead < 3 && ordered[i + ahead] == ordered[i])
          ++ahead;

        if (ahead >= 3)
          break;

        ++i;
      }

      _WriteCount(target, i - start, repeated: false);
      for (var j = start; j < i; ++j)
        target.Add(ordered[j]);
    }
  }

  /// <summary>The longest run the two-byte count can state.</summary>
  private const int _MaxCount = (63 << 8) | 255;

  /// <summary>
  /// Writes a count, in one byte where it fits in six bits and two where it does not.
  /// </summary>
  private static void _WriteCount(List<byte> target, int count, bool repeated) {
    var flag = repeated ? 128 : 0;
    if (count < 64) {
      target.Add((byte)(flag | count));
      return;
    }

    // Sixty-four and up is not a count but a marker: its low bits are the high byte of a longer one.
    target.Add((byte)(flag | (64 + (count >> 8))));
    target.Add((byte)count);
  }
}
