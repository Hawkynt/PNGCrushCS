using System;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.BoogieDownPaint;

/// <summary>Assembles Boogie Down Paint pictures.</summary>
/// <remarks>
/// Of the three forms the reader knows, this writes the one that names its own escape bytes and
/// says so in a header. The oldest form makes every byte a command and so cannot represent a
/// literal at all, and the loader form would mean emitting somebody's machine code; the named-escape
/// form is the only one that can be produced from the picture alone.
/// </remarks>
public static class BoogieDownPaintWriter {

  /// <summary>What the later form calls itself, sitting between the load address and the escapes.</summary>
  private static ReadOnlySpan<byte> _Signature => "BDP 5.00"u8;

  /// <summary>Where Boogie Down Paint's screen lands.</summary>
  private const ushort _LoadAddress = 0x4000;

  /// <summary>A run costs three bytes, so it only pays from three alike upward.</summary>
  private const int _WorthARun = 3;

  public static byte[] ToBytes(BoogieDownPaintFile file) {
    var screen = file.ScreenData ?? [];
    if (screen.Length < BoogieDownPaintFile.UnpackedSize)
      throw new ArgumentException($"A Boogie Down Paint screen is {BoogieDownPaintFile.UnpackedSize} bytes; this one is {screen.Length}.", nameof(file));

    var payload = screen.AsSpan(0, BoogieDownPaintFile.UnpackedSize);
    var (shortEscape, longEscape) = _ChooseEscapes(payload);

    var output = new List<byte> {
      (byte)(_LoadAddress & 0xFF),
      (byte)(_LoadAddress >> 8),
    };
    foreach (var b in _Signature)
      output.Add(b);

    output.Add(shortEscape);
    output.Add(longEscape);

    for (var at = 0; at < payload.Length;) {
      var value = payload[at];
      var run = 1;
      while (at + run < payload.Length && payload[at + run] == value)
        ++run;

      at += run;

      // A byte that is itself an escape can never stand for itself, however few of them there are.
      var mustEscape = value == shortEscape || value == longEscape;

      while (run > 0) {
        if (run < _WorthARun && !mustEscape) {
          for (var i = 0; i < run; ++i)
            output.Add(value);

          break;
        }

        if (run <= 256) {
          output.Add(shortEscape);
          output.Add((byte)(run & 0xFF));       // 256 is written as nought, which is what it means
          output.Add(value);
          break;
        }

        var chunk = Math.Min(run, 65535);
        output.Add(longEscape);
        output.Add((byte)(chunk & 0xFF));
        output.Add((byte)(chunk >> 8));
        output.Add(value);
        run -= chunk;
      }
    }

    return [.. output];
  }

  /// <summary>
  /// The two byte values that cost least to give up as escapes.
  /// </summary>
  /// <remarks>
  /// Every occurrence of an escape in the picture has to be written as a run of its own, so the
  /// cheapest pair is the pair that occurs least. A C64 screen almost always leaves several values
  /// entirely unused, in which case the choice is free.
  /// </remarks>
  private static (byte Short, byte Long) _ChooseEscapes(ReadOnlySpan<byte> payload) {
    Span<int> counts = stackalloc int[256];
    foreach (var b in payload)
      ++counts[b];

    int first = 0, second = 1;
    if (counts[second] < counts[first])
      (first, second) = (second, first);

    for (var i = 2; i < 256; ++i) {
      if (counts[i] < counts[first]) {
        second = first;
        first = i;
      } else if (counts[i] < counts[second])
        second = i;
    }

    return ((byte)first, (byte)second);
  }
}
