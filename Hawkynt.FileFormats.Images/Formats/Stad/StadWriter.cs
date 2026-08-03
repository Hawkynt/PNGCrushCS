using System;
using System.Collections.Generic;

namespace FileFormat.Stad;

/// <summary>Assembles STAD compressed screen bytes from a <see cref="StadFile"/>.</summary>
public static class StadWriter {

  private static readonly byte[] _MagicPM85 = [(byte)'p', (byte)'M', (byte)'8', (byte)'5'];

  /// <summary>
  /// Writes the screen in the run-length form STAD states.
  /// </summary>
  /// <remarks>
  /// This wrote PackBits under a four-byte header, which no STAD reader understands — it agreed only
  /// with the reader here, which expected the same invented scheme. It also claimed pM86, the form
  /// that stores the screen a byte-column at a time, while writing rows.
  /// <para/>
  /// The real header is seven bytes: the magic, then an escape and the single value it repeats, then
  /// a second escape for runs of anything else. Both escapes have to be bytes the screen makes little
  /// use of, since every occurrence of one in the picture costs three bytes to spell out; they are
  /// picked here as the two rarest.
  /// </remarks>
  public static byte[] ToBytes(StadFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var screen = new byte[StadFile.ScreenDataSize];
    file.RawData.AsSpan(0, Math.Min(file.RawData.Length, StadFile.ScreenDataSize)).CopyTo(screen);

    var histogram = new int[256];
    foreach (var b in screen)
      ++histogram[b];

    var runValue = histogram[0xFF] > histogram[0x00] ? (byte)0xFF : (byte)0x00;
    var (escapeRun, escapeAny) = _RarestPair(histogram, runValue);

    var output = new List<byte>(screen.Length / 2) {
      _MagicPM85[0], _MagicPM85[1], _MagicPM85[2], _MagicPM85[3],
      escapeRun, runValue, escapeAny,
    };

    for (var at = 0; at < screen.Length;) {
      var value = screen[at];

      var run = 1;
      while (run < 256 && at + run < screen.Length && screen[at + run] == value)
        ++run;

      // A run of the one repeated value costs two bytes and any other run costs three, so what is
      // worth coding rather than writing out differs between them.
      if (value == runValue && run >= 2) {
        output.Add(escapeRun);
        output.Add((byte)(run - 1));
      } else if (run >= 3) {
        output.Add(escapeAny);
        output.Add(value);
        output.Add((byte)(run - 1));
      } else
        // A byte that happens to be one of the escapes cannot stand for itself, so it is spelled out
        // as a run of one.
        for (var i = 0; i < run; ++i)
          if (value == escapeRun || value == escapeAny) {
            output.Add(escapeAny);
            output.Add(value);
            output.Add(0);
          } else
            output.Add(value);

      at += run;
    }

    return output.ToArray();
  }

  /// <summary>Picks the two byte values the screen uses least, leaving out the one being repeated.</summary>
  private static (byte EscapeRun, byte EscapeAny) _RarestPair(int[] histogram, byte runValue) {
    int first = -1, second = -1;

    for (var value = 0; value < histogram.Length; ++value) {
      if (value == runValue)
        continue;

      if (first < 0 || histogram[value] < histogram[first]) {
        second = first;
        first = value;
      } else if (second < 0 || histogram[value] < histogram[second])
        second = value;
    }

    return ((byte)first, (byte)second);
  }
}
