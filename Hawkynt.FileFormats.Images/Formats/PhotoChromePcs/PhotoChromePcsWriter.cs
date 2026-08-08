using System;
using System.Collections.Generic;

namespace FileFormat.PhotoChromePcs;

/// <summary>Assembles PhotoChrome bytes from a <see cref="PhotoChromePcsFile"/>.</summary>
public static class PhotoChromePcsWriter {

  /// <summary>The longest run a command byte can count on its own.</summary>
  private const int _MAX_COUNTED = 127;

  /// <summary>The longest run of literals a command byte can count on its own.</summary>
  private const int _MAX_LITERALS = 128;

  public static byte[] ToBytes(PhotoChromePcsFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var fields = file.Fields ?? [];
    if (fields.Length == 0)
      throw new ArgumentException("A PhotoChrome picture holds one field or two, not none.", nameof(file));

    var data = new List<byte> { 1, 64, 0, 200, (byte)(fields.Length > 1 ? 3 : 0), 0 };

    foreach (var field in fields) {
      _PackBlock(data, field, 0, PhotoChromePcsFile.BitmapSize, false);
      _PackBlock(data, field, PhotoChromePcsFile.BitmapSize, PhotoChromePcsFile.FieldSize, true);
    }

    return [.. data];
  }

  /// <summary>
  /// Codes one half of a field as a block of run-length commands, counted in bytes for the bitmap
  /// and in colour words for the palette.
  /// </summary>
  /// <remarks>
  /// The block declares how many commands it holds before the first of them, so the whole block has
  /// to be built before its own head can be written. A block declaring more than it needs is drained
  /// by the reader rather than abandoned, which would take the next block's bytes with it — so the
  /// count is exactly what follows and nothing is padded.
  /// </remarks>
  private static void _PackBlock(List<byte> data, ReadOnlySpan<byte> field, int from, int to, bool words) {
    var step = words ? 2 : 1;
    var count = (to - from) / step;
    var body = new List<byte>();
    var commands = 0;
    var literals = 0;

    for (var at = 0; at < count;) {
      var run = 1;
      while (at + run < count && _Same(field, from + (at + run) * step, from + at * step, step))
        ++run;

      // A run costs a command and one value; literals cost a command once and a value each. Two
      // equal words already pay for themselves, two equal bytes do not.
      if (run >= (words ? 2 : 3)) {
        _FlushLiterals(body, field, from, at, step, ref literals, ref commands);
        _WriteRun(body, field, from + at * step, run, step, ref commands);
        at += run;
        continue;
      }

      ++literals;
      ++at;

      if (literals == _MAX_LITERALS)
        _FlushLiterals(body, field, from, at, step, ref literals, ref commands);
    }

    _FlushLiterals(body, field, from, count, step, ref literals, ref commands);

    data.Add((byte)(commands >> 8));
    data.Add((byte)commands);
    data.AddRange(body);
  }

  private static bool _Same(ReadOnlySpan<byte> field, int left, int right, int step) {
    for (var i = 0; i < step; ++i)
      if (field[left + i] != field[right + i])
        return false;

    return true;
  }

  private static void _WriteRun(
    List<byte> body, ReadOnlySpan<byte> field, int at, int run, int step, ref int commands) {
    while (run > 0) {
      var take = Math.Min(run, ushort.MaxValue);

      if (take <= _MAX_COUNTED && take >= 2)
        body.Add((byte)take);
      else {
        // Zero says the count is too large for a byte and follows as a word.
        body.Add(0);
        body.Add((byte)(take >> 8));
        body.Add((byte)take);
      }

      for (var i = 0; i < step; ++i)
        body.Add(field[at + i]);

      ++commands;
      run -= take;
    }
  }

  private static void _FlushLiterals(
    List<byte> body, ReadOnlySpan<byte> field, int from, int end, int step, ref int literals,
    ref int commands) {
    if (literals == 0)
      return;

    // Counted downwards from 256, which is what makes 128 the most a byte can say.
    body.Add((byte)(256 - literals));
    for (var i = end - literals; i < end; ++i)
      for (var b = 0; b < step; ++b)
        body.Add(field[from + i * step + b]);

    ++commands;
    literals = 0;
  }
}
