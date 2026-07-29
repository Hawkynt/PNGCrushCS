using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.TextMode;

namespace FileFormat.AnsiArt;

/// <summary>Parses ANSI art streams: CP437 bytes with CSI escape sequences (ESC '[' params 'cmd').</summary>
public static class AnsiArtReader {

  private const int _DefaultColumns = 80;

  public static AnsiArtFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ANSI art file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AnsiArtFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static AnsiArtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static AnsiArtFile FromSpan(ReadOnlySpan<byte> data) {
    // 1. Detach a trailing SAUCE record if present. SAUCE marker = "SAUCE00" at offset (length-128).
    byte[]? sauce = null;
    var artLen = data.Length;
    if (artLen >= 128 && data.Slice(artLen - 128, 5).SequenceEqual("SAUCE"u8)) {
      sauce = data.Slice(artLen - 128, 128).ToArray();
      artLen -= 128;
      // SAUCE precedes optionally with a 0x1A (^Z) — strip it if present.
      if (artLen > 0 && data[artLen - 1] == 0x1A) --artLen;
      // Plus optional COMNT block: 5-byte "COMNT" header + N*64 bytes; the SAUCE record's byte at offset 104 gives N.
      var commentLines = sauce[104];
      if (commentLines > 0) {
        var commentTotal = 5 + commentLines * 64;
        if (artLen >= commentTotal && data.Slice(artLen - commentTotal, 5).SequenceEqual("COMNT"u8))
          artLen -= commentTotal;
      }
    } else if (artLen > 0 && data[artLen - 1] == 0x1A) {
      --artLen;
    }

    // 2. Stream-parse the art region, painting into a sparse cell grid.
    var cells = new List<TextCell>();
    var col = 0;
    var row = 0;
    var maxCols = _DefaultColumns;
    byte fg = 7, bg = 0;
    var blink = false;
    var bright = false;
    var savedCol = 0; var savedRow = 0;

    void Ensure(int targetRow, int targetCol) {
      while (cells.Count < (targetRow + 1) * maxCols)
        cells.Add(new TextCell(0x20, 7, 0));
      // No-op for column — handled by Put.
    }

    void Put(byte cp) {
      if (col >= maxCols) { col = 0; ++row; }
      Ensure(row, col);
      var effectiveFg = bright ? (byte)(fg | 0x08) : fg;
      cells[row * maxCols + col] = new TextCell(cp, effectiveFg, bg, blink);
      ++col;
    }

    var i = 0;
    while (i < artLen) {
      var c = data[i++];
      if (c == 0x1B && i < artLen && data[i] == (byte)'[') {
        ++i;
        var paramStart = i;
        while (i < artLen && (data[i] is (>= (byte)'0' and <= (byte)'9') or (byte)';' or (byte)'?')) ++i;
        if (i >= artLen) break;
        var paramText = System.Text.Encoding.ASCII.GetString(data.Slice(paramStart, i - paramStart));
        var cmd = data[i++];
        var p = _ParseParams(paramText);

        switch (cmd) {
          case (byte)'m': // SGR
            _ApplySgr(p, ref fg, ref bg, ref bright, ref blink);
            break;
          case (byte)'A': row = Math.Max(0, row - _OrDefault(p, 0, 1)); break;
          case (byte)'B': row += _OrDefault(p, 0, 1); break;
          case (byte)'C': col = Math.Min(maxCols - 1, col + _OrDefault(p, 0, 1)); break;
          case (byte)'D': col = Math.Max(0, col - _OrDefault(p, 0, 1)); break;
          case (byte)'H':
          case (byte)'f':
            row = Math.Max(0, _OrDefault(p, 0, 1) - 1);
            col = Math.Max(0, _OrDefault(p, 1, 1) - 1);
            break;
          case (byte)'s': savedCol = col; savedRow = row; break;
          case (byte)'u': col = savedCol; row = savedRow; break;
          case (byte)'J':
          case (byte)'K':
            // Clear-screen / clear-line: we model these as space-fills against the current background.
            break;
        }
        continue;
      }
      if (c == 0x0D) { col = 0; continue; }
      if (c == 0x0A) { col = 0; ++row; continue; }
      Put(c);
    }

    var totalRows = (cells.Count + maxCols - 1) / maxCols;
    if (totalRows == 0) totalRows = 1;
    while (cells.Count < totalRows * maxCols) cells.Add(new TextCell(0x20, fg, bg, false));

    return new AnsiArtFile {
      ColumnCount = maxCols,
      RowCount = totalRows,
      Cells = cells.ToArray(),
      SauceRecord = sauce,
    };
  }

  private static int[] _ParseParams(string text) {
    if (string.IsNullOrEmpty(text)) return [];
    var parts = text.Split(';');
    var result = new int[parts.Length];
    for (var i = 0; i < parts.Length; ++i) {
      if (int.TryParse(parts[i], out var v)) result[i] = v;
      else result[i] = 0;
    }
    return result;
  }

  private static int _OrDefault(int[] p, int ix, int def) => ix < p.Length && p[ix] > 0 ? p[ix] : def;

  private static void _ApplySgr(int[] p, ref byte fg, ref byte bg, ref bool bright, ref bool blink) {
    // ANSI → CGA palette index map (bit-reordered).
    var ansiToCga = new byte[] { 0, 4, 2, 6, 1, 5, 3, 7 };
    if (p.Length == 0) p = [0];
    foreach (var v in p) {
      switch (v) {
        case 0:  fg = 7; bg = 0; bright = false; blink = false; break;
        case 1:  bright = true; break;
        case 2:  bright = false; break;
        case 5:  blink = true; break;
        case 25: blink = false; break;
        case 7:  (fg, bg) = (bg, fg); break;
        case >= 30 and <= 37: fg = ansiToCga[v - 30]; break;
        case >= 40 and <= 47: bg = ansiToCga[v - 40]; break;
        case 39: fg = 7; break;
        case 49: bg = 0; break;
      }
    }
  }
}
