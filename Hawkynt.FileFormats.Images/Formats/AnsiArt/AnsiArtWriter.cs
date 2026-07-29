using System;
using System.IO;
using System.Text;

namespace FileFormat.AnsiArt;

public static class AnsiArtWriter {

  /// <summary>Serialise the cell grid as ANSI: emit minimal SGR transitions between adjacent cells.</summary>
  public static byte[] ToBytes(AnsiArtFile file) {
    ArgumentNullException.ThrowIfNull(file.Cells);
    if (file.ColumnCount == 0 || file.RowCount == 0) return [];

    // CGA → ANSI param map (inverse of reader's ansiToCga).
    var cgaToAnsi = new int[] { 0, 4, 2, 6, 1, 5, 3, 7 };
    byte curFg = 7, curBg = 0;
    var curBright = false; var curBlink = false;

    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
    // Reset to defaults first.
    w.Write("\x1B[0m"u8.ToArray());

    for (var row = 0; row < file.RowCount; ++row) {
      for (var col = 0; col < file.ColumnCount; ++col) {
        var cell = file.Cells[row * file.ColumnCount + col];
        var fgBase = (byte)(cell.Foreground & 0x07);
        var bright = (cell.Foreground & 0x08) != 0;
        var bg = (byte)(cell.Background & 0x07);
        var blink = cell.Blink;

        var sgr = new StringBuilder();
        if (bright != curBright || blink != curBlink || (curFg & 0x08) != (cell.Foreground & 0x08)) {
          sgr.Append('0').Append(';');
          if (bright) sgr.Append("1;");
          if (blink) sgr.Append("5;");
          // Reset baseline forces re-emit of colours below.
          curBright = bright; curBlink = blink; curFg = 7; curBg = 0;
        }
        if (fgBase != (curFg & 0x07) || (curFg & 0x08) != (cell.Foreground & 0x08)) {
          sgr.Append(30 + cgaToAnsi[fgBase]).Append(';');
          curFg = cell.Foreground;
        }
        if (bg != curBg) {
          sgr.Append(40 + cgaToAnsi[bg]).Append(';');
          curBg = bg;
        }
        if (sgr.Length > 0) {
          if (sgr[^1] == ';') sgr.Length--;
          w.Write((byte)0x1B);
          w.Write((byte)'[');
          w.Write(Encoding.ASCII.GetBytes(sgr.ToString()));
          w.Write((byte)'m');
        }
        w.Write(cell.CodePoint);
      }
      // CRLF between rows, except after the last row (lets the renderer place exactly RowCount lines).
      if (row + 1 < file.RowCount) { w.Write((byte)'\r'); w.Write((byte)'\n'); }
    }

    if (file.SauceRecord is { Length: 128 }) {
      w.Write((byte)0x1A); // SAUCE preamble
      w.Write(file.SauceRecord);
    }
    return ms.ToArray();
  }
}
