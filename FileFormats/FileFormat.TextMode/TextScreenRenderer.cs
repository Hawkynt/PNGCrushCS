using System;

namespace FileFormat.TextMode;

/// <summary>Paints a <see cref="TextScreen"/> onto an RGB24 byte buffer using a <see cref="BitmapFont"/>.</summary>
public static class TextScreenRenderer {

  public readonly record struct Rgb24Image(int Width, int Height, byte[] PixelData);

  public static Rgb24Image Render(TextScreen screen, BitmapFont? font = null) {
    if (screen is null) throw new ArgumentNullException(nameof(screen));
    font ??= screen.Font ?? BitmapFont.DefaultVga8x16;
    var palette = screen.Palette ?? TextPalette.DefaultEga;
    if (palette.Length < 48)
      throw new ArgumentException("Text palette must be ≥ 48 bytes (16 RGB triples).", nameof(screen));

    var cellW = font.CellWidth;
    var cellH = font.CellHeight;
    var width = screen.ColumnCount * cellW;
    var height = screen.RowCount * cellH;
    var rgb = new byte[width * height * 3];

    for (var row = 0; row < screen.RowCount; ++row)
      for (var col = 0; col < screen.ColumnCount; ++col) {
        var cell = screen.Cells[row * screen.ColumnCount + col];
        var fgOff = (cell.Foreground & 0x0F) * 3;
        var bgOff = (cell.Background & 0x0F) * 3;
        for (var gy = 0; gy < cellH; ++gy) {
          var glyphRow = font.GetGlyphRow(cell.CodePoint, gy);
          for (var gx = 0; gx < cellW; ++gx) {
            var lit = (glyphRow & (1 << (7 - gx))) != 0;
            var srcOff = lit ? fgOff : bgOff;
            var dstOff = ((row * cellH + gy) * width + (col * cellW + gx)) * 3;
            rgb[dstOff]     = palette[srcOff];
            rgb[dstOff + 1] = palette[srcOff + 1];
            rgb[dstOff + 2] = palette[srcOff + 2];
          }
        }
      }

    return new Rgb24Image(width, height, rgb);
  }
}
