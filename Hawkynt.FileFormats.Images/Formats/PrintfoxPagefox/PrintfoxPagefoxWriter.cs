using System;
using System.Collections.Generic;

namespace FileFormat.PrintfoxPagefox;

/// <summary>Assembles Printfox/Pagefox (.bs/.pg) file bytes from a PrintfoxPagefoxFile.</summary>
public static class PrintfoxPagefoxWriter {

  /// <summary>The byte a run is introduced by, and the one every sample opens with.</summary>
  private const byte _RUN_ESCAPE = 0x9B;

  private const byte _TYPE_BYTE = 0x42;

  /// <summary>
  /// Writes the screen packed, a character cell at a time, the way real files hold it.
  /// </summary>
  /// <remarks>
  /// This wrote the rows out as they stood with no type byte and no packing, which is not a file any
  /// Printfox reader would take — it agreed only with the reader here, which handed its input back
  /// unpacked.
  /// </remarks>
  public static byte[] ToBytes(PrintfoxPagefoxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var cells = _RowsToCells(file.RawData ?? []);
    var output = new List<byte>(cells.Length / 2) { _TYPE_BYTE };

    for (var at = 0; at < cells.Length;) {
      var run = 1;
      while (run < 0xFFFF && at + run < cells.Length && cells[at + run] == cells[at])
        ++run;

      // A run costs four bytes, so it is worth coding from five alike upwards — and the escape
      // itself can never stand for itself, however short its run.
      if (run >= 5 || cells[at] == _RUN_ESCAPE) {
        output.Add(_RUN_ESCAPE);
        output.Add((byte)run);
        output.Add((byte)(run >> 8));
        output.Add(cells[at]);
      } else
        for (var i = 0; i < run; ++i)
          output.Add(cells[at]);

      at += run;
    }

    return output.ToArray();
  }

  /// <summary>Puts a screen held in rows back into character cells, eight bytes to a cell.</summary>
  private static byte[] _RowsToCells(byte[] rows) {
    var cells = new byte[PrintfoxPagefoxFile.MinDataSize];
    var columns = PrintfoxPagefoxFile.BytesPerRow;

    for (var cellRow = 0; cellRow < PrintfoxPagefoxFile.FixedHeight / 8; ++cellRow)
      for (var cellColumn = 0; cellColumn < columns; ++cellColumn)
        for (var line = 0; line < 8; ++line) {
          var from = (cellRow * 8 + line) * columns + cellColumn;
          cells[(cellRow * columns + cellColumn) * 8 + line] = from < rows.Length ? rows[from] : (byte)0;
        }

    return cells;
  }
}
