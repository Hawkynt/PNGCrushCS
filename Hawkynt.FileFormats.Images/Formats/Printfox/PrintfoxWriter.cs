using System;
using System.Collections.Generic;

namespace FileFormat.Printfox;

/// <summary>Assembles Printfox picture bytes from a <see cref="PrintfoxFile"/>.</summary>
public static class PrintfoxWriter {

  /// <summary>The byte that introduces a run.</summary>
  private const int _ESCAPE = 155;

  /// <summary>
  /// Writes a named block, which is the only one of the three kinds that can hold any size.
  /// </summary>
  /// <remarks>
  /// A block counts its runs in one byte, so the longest is 256 and a length of zero means that
  /// rather than nothing. A run of one or two is written out literally: the escape form costs
  /// three bytes, so it only pays from three upwards — and a literal that happens to equal the
  /// escape has to be written as a run of one, there being no other way to say it.
  /// </remarks>
  public static byte[] ToBytes(PrintfoxFile file) {
    var cells = file.Cells ?? [];
    var body = new List<byte> { (byte)'P', (byte)file.Rows, (byte)file.Columns };
    body.AddRange("PICTURE"u8.ToArray());
    body.Add(0);

    for (var i = 0; i < cells.Length;) {
      var run = 1;
      while (run < 256 && i + run < cells.Length && cells[i + run] == cells[i])
        ++run;

      if (run < 3 && cells[i] != _ESCAPE) {
        for (var j = 0; j < run; ++j)
          body.Add(cells[i]);

        i += run;
        continue;
      }

      body.Add(_ESCAPE);
      body.Add((byte)run);
      body.Add(cells[i]);
      i += run;
    }

    return body.ToArray();
  }
}
