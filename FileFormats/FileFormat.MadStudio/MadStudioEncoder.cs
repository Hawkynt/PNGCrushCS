using System;
using FileFormat.Core;

namespace FileFormat.MadStudio;

/// <summary>Fits a picture to the characters and colour registers a Mad Studio mode offers.</summary>
internal static class MadStudioEncoder {

  /// <summary>Picks the colour registers, background first.</summary>
  public static byte[] ChooseColors(MadStudioMode mode, byte[] bgra, byte[] gtia, byte[] antic2Colors) {
    if (mode == MadStudioMode.Antic2)
      return antic2Colors;

    var quantized = ColorQuantizer.Quantize(
      bgra, MadStudioLayout.DisplayWidth * MadStudioLayout.DisplayHeight, MadStudioLayout.ColorCount);

    var colors = new byte[MadStudioLayout.ColorCount];
    for (var i = 0; i < colors.Length && i < quantized.Count; ++i)
      colors[i] = Atari8BitGraphics.FindNearestColorByte(
        gtia, quantized.Palette[i * 3], quantized.Palette[i * 3 + 1], quantized.Palette[i * 3 + 2]);

    return colors;
  }

  /// <summary>
  /// Chooses the character code for every cell by trying all of them and keeping the closest.
  /// </summary>
  /// <remarks>
  /// There is nothing cleverer available: the glyphs are fixed, the registers are chosen for the
  /// whole screen, and cells do not interact, so each cell's best code is found independently and
  /// exhaustively. A code carries colour information as well as a glyph number in every mode but
  /// ANTIC 2, which is why all 256 are worth trying rather than just the glyphs.
  /// </remarks>
  public static byte[] ChooseCharacters(MadStudioMode mode, byte[] bgra, byte[] gtia, byte[] colors, byte[] font) {
    var columns = MadStudioLayout.ColumnsFor(mode);
    var rows = MadStudioLayout.RowsFor(mode);
    var cellWidth = MadStudioLayout.CellWidthFor(mode);
    var cellHeight = MadStudioLayout.CellHeightFor(mode);

    // The registers as RGB, so a candidate can be scored without going through the palette twice.
    var registers = new byte[colors.Length * 3];
    for (var i = 0; i < colors.Length; ++i)
      Array.Copy(gtia, colors[i] * 3, registers, i * 3, 3);

    var characters = new byte[columns * rows];
    for (var row = 0; row < rows; ++row)
    for (var col = 0; col < columns; ++col) {
      var bestCost = long.MaxValue;
      var best = 0;

      for (var candidate = 0; candidate < MadStudioLayout.CharacterCount; ++candidate) {
        long cost = 0;
        for (var cellY = 0; cellY < cellHeight && cost < bestCost; ++cellY)
        for (var cellX = 0; cellX < cellWidth; ++cellX) {
          var value = MadStudioLayout.PixelAt(mode, font, candidate, cellX, cellY);
          var register = MadStudioLayout.RegisterFor(mode, candidate, value) * 3;
          var pixel = ((row * cellHeight + cellY) * MadStudioLayout.DisplayWidth + col * cellWidth + cellX) * 4;

          int dr = registers[register] - bgra[pixel + 2];
          int dg = registers[register + 1] - bgra[pixel + 1];
          int db = registers[register + 2] - bgra[pixel];
          cost += dr * dr + dg * dg + db * db;
        }

        if (cost >= bestCost)
          continue;

        bestCost = cost;
        best = candidate;
      }

      characters[row * columns + col] = (byte)best;
    }

    return characters;
  }
}
