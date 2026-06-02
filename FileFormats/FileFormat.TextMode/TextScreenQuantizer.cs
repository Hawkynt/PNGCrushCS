using System;

namespace FileFormat.TextMode;

/// <summary>
/// Quantizes an RGB24 pixel grid into a <see cref="TextScreen"/> by picking the best CP437 glyph
/// + 16-colour fg/bg pair per font cell. Operates on a raw byte buffer to stay decoupled from
/// FileFormat.Core — the format wrappers (NFO/ANSI/XBIN) convert their RawImage input first.
/// </summary>
public static class TextScreenQuantizer {

  public static TextScreen FromRgb24(byte[] rgb, int width, int height, int columns, int rows, BitmapFont? font = null, byte[]? palette = null) {
    if (rgb is null) throw new ArgumentNullException(nameof(rgb));
    font ??= BitmapFont.DefaultVga8x16;
    palette ??= TextPalette.DefaultEga;
    if (palette.Length < 48) throw new ArgumentException("Palette must be ≥ 48 bytes (16 RGB triples).", nameof(palette));

    var cellW = font.CellWidth;
    var cellH = font.CellHeight;
    if (columns * cellW != width || rows * cellH != height)
      throw new ArgumentException($"Image dimensions {width}×{height} don't match {columns}×{rows} cells of {cellW}×{cellH}.", nameof(rgb));
    if (rgb.Length < width * height * 3)
      throw new ArgumentException($"RGB buffer too small ({rgb.Length} bytes) for {width}×{height}.", nameof(rgb));

    var cells = new TextCell[columns * rows];
    for (var row = 0; row < rows; ++row)
      for (var col = 0; col < columns; ++col)
        cells[row * columns + col] = _QuantizeCell(rgb, width, col, row, cellW, cellH, font, palette);

    return new TextScreen {
      ColumnCount = columns,
      RowCount = rows,
      Cells = cells,
      Palette = palette,
      Font = font,
    };
  }

  private static TextCell _QuantizeCell(byte[] rgb, int width, int col, int row, int cellW, int cellH, BitmapFont font, byte[] palette) {
    Span<int> counts = stackalloc int[16];
    var snapped = new byte[cellW * cellH];
    for (var py = 0; py < cellH; ++py)
      for (var px = 0; px < cellW; ++px) {
        var srcOff = ((row * cellH + py) * width + (col * cellW + px)) * 3;
        var idx = _ClosestPaletteIndex(rgb[srcOff], rgb[srcOff + 1], rgb[srcOff + 2], palette);
        snapped[py * cellW + px] = idx;
        ++counts[idx];
      }

    var (fg, bg) = _TopTwo(counts);
    if (counts[fg] == 0) (fg, bg) = (15, 0);

    Span<byte> mask = stackalloc byte[cellH];
    for (var py = 0; py < cellH; ++py) {
      byte rowBits = 0;
      for (var px = 0; px < cellW; ++px) {
        var idx = snapped[py * cellW + px];
        var distFg = _Sq(idx, fg);
        var distBg = _Sq(idx, bg);
        if (distFg <= distBg) rowBits |= (byte)(1 << (7 - px));
      }
      mask[py] = rowBits;
    }

    var bestGlyph = (byte)0;
    var bestDist = int.MaxValue;
    for (var g = 0; g < 256; ++g) {
      var dist = 0;
      for (var r = 0; r < cellH; ++r)
        dist += _PopCount((byte)(font.GlyphData[g * cellH + r] ^ mask[r]));
      if (dist < bestDist) {
        bestDist = dist;
        bestGlyph = (byte)g;
        if (dist == 0) break;
      }
    }
    return new TextCell(bestGlyph, (byte)fg, (byte)bg);
  }

  private static byte _ClosestPaletteIndex(byte r, byte g, byte b, byte[] palette) {
    var bestIx = 0;
    var bestDist = int.MaxValue;
    for (var i = 0; i < 16; ++i) {
      var dr = palette[i * 3]     - r;
      var dg = palette[i * 3 + 1] - g;
      var db = palette[i * 3 + 2] - b;
      var d = dr * dr + dg * dg + db * db;
      if (d < bestDist) { bestDist = d; bestIx = i; }
    }
    return (byte)bestIx;
  }

  private static (int top, int second) _TopTwo(Span<int> counts) {
    int top = 0, second = 0, topC = -1, secondC = -1;
    for (var i = 0; i < counts.Length; ++i) {
      if (counts[i] > topC) { secondC = topC; second = top; topC = counts[i]; top = i; }
      else if (counts[i] > secondC) { secondC = counts[i]; second = i; }
    }
    return (top, second);
  }

  private static int _Sq(int a, int b) { var d = a - b; return d * d; }

  private static int _PopCount(byte b) {
    var n = 0;
    while (b != 0) { n += b & 1; b >>= 1; }
    return n;
  }
}
