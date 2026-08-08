using System;
using FileFormat.Core;

namespace FileFormat.Graph2Font;

/// <summary>Builds a Graph2Font project around a picture.</summary>
/// <remarks>
/// The format stores no picture, only every input the chip needs to draw one, so writing it is a
/// matter of choosing which of the editor's many freedoms to spend. Almost all of them are declined:
/// no sprites, no raster program, no video upgrade, no second inverse table, and the same display
/// mode down the whole screen. What is spent is the two that carry a picture — a colour table with
/// one entry per scanline, and a character set per row of cells.
/// <para/>
/// A row gets its own character set and forty cells to fill, and a set holds 128 characters, so
/// every cell can be given a character nothing else uses. The character screen is then the same
/// forty numbers on every row and the picture lives entirely in the sets: 160 pixels across, two
/// bits each, and four colours a scanline chosen freely from the 128 the chip has.
/// </remarks>
public static class Graph2FontEncoder {

  /// <summary>Characters ANTIC fetches per scanline.</summary>
  public const int Columns = 40;

  /// <summary>Rows of character cells.</summary>
  public const int CellRows = Graph2FontFile.Height / 8;

  /// <summary>Character sets carried: one per row of cells, so no cell shares with another.</summary>
  public const int FontCount = CellRows;

  /// <summary>Pixels of border either side of the playfield, which only the background reaches.</summary>
  public const int BorderPixels = (Graph2FontFile.Width - Columns * 8) / 2;

  /// <summary>Two-bit pixels one scanline of the playfield holds.</summary>
  public const int PlayfieldPixels = Columns * 4;

  /// <summary>Colour registers a scanline draws from: the background and three playfield ones.</summary>
  private const int _REGISTERS = 4;

  /// <summary>Distance between one scanline's entry in a colour table and the next register's.</summary>
  private const int _COLOR_STRIDE = 256;

  /// <summary>Where the character set numbers sit, and everything after them is measured from.</summary>
  public static int FontNumberOffset => 3 + 30 * Columns + FontCount * Graph2FontFile.FontSize;

  /// <summary>How long a project of this shape is.</summary>
  public static int Length => FontNumberOffset + 153724;

  /// <summary>Offset of the byte naming how a cell's characters are arranged.</summary>
  private const int _ARRANGEMENT = 147679;

  /// <summary>Offset of the per-row display modes.</summary>
  private const int _MODES = 153694;

  /// <summary>
  /// The arrangement that draws five colours from a cell and carries neither a raster program nor a
  /// second inverse table.
  /// </summary>
  /// <remarks>
  /// Five rather than four costs nothing here and is not used either: the fifth colour arrives by a
  /// bit of the character number, which is one byte per cell and so shared by the cell's eight
  /// scanlines, while every other colour choice is per scanline. Declining it keeps the scanlines
  /// independent, which is what makes each of them settleable on its own.
  /// </remarks>
  private const byte _FIVE_COLOR_ARRANGEMENT = 2;

  /// <summary>The display mode that reads a cell as four two-bit pixels.</summary>
  private const byte _FOUR_COLOR_MODE = 2;

  public static byte[] Encode(ReadOnlySpan<byte> rgb) {
    var data = new byte[Length];
    var fontsOffset = 3 + 30 * Columns;
    var numbers = FontNumberOffset;

    data[0] = Columns;
    data[2] = FontCount - 1;
    data[numbers + _ARRANGEMENT] = _FIVE_COLOR_ARRANGEMENT;

    for (var row = 0; row < CellRows; ++row) {
      data[numbers + row] = (byte)row;
      data[numbers + _MODES + row] = _FOUR_COLOR_MODE;

      // The same forty character numbers on every row; what differs is the set the row reads them
      // from, which is what gives each row its own forty cells.
      for (var column = 0; column < Columns; ++column)
        data[3 + row * Columns + column] = (byte)column;
    }

    Span<byte> registers = stackalloc byte[_REGISTERS];
    Span<byte> pixels = stackalloc byte[PlayfieldPixels];

    for (var y = 0; y < Graph2FontFile.Height; ++y) {
      _SolveRow(rgb, y, registers, pixels);

      for (var register = 0; register < _REGISTERS; ++register)
        data[numbers + 30 + y + register * _COLOR_STRIDE] = registers[register];

      for (var column = 0; column < Columns; ++column) {
        var packed = 0;
        for (var pixel = 0; pixel < 4; ++pixel)
          packed |= pixels[column * 4 + pixel] << (6 - pixel * 2);

        data[fontsOffset + (y >> 3) * Graph2FontFile.FontSize + column * 8 + (y & 7)] = (byte)packed;
      }
    }

    return data;
  }

  /// <summary>
  /// Settles one scanline: which four colours its registers hold, and which of them each pixel takes.
  /// </summary>
  /// <remarks>
  /// Reduced to what the registers can hold before the four are chosen rather than after. The two
  /// reductions do not commute — choosing four colours of a picture first and rounding them to the
  /// chip's afterwards can land two of them on the same shade and leave a scanline with three
  /// colours where it could have had four — and doing it this way round means a scanline that
  /// already fits comes back exactly, which is what a picture that was one of these needs.
  /// <para/>
  /// The border either side of the playfield is drawn by the background register and by nothing
  /// else, so it is counted among the colours to be chosen and then decides which of them the
  /// background is. A scanline whose border disagrees with its edges would otherwise spend a
  /// playfield register on the border and lose it from the picture.
  /// </remarks>
  private static void _SolveRow(ReadOnlySpan<byte> rgb, int y, Span<byte> registers, Span<byte> pixels) {
    var gtia = Atari8BitGraphics.Palette;
    var row = y * Graph2FontFile.Width * 3;
    var samples = PlayfieldPixels + BorderPixels * 2;
    var bgra = new byte[samples * 4];

    for (var sample = 0; sample < samples; ++sample) {
      // A playfield pixel covers two screen pixels; the border is counted a screen pixel at a time.
      var border = sample - PlayfieldPixels;
      var x = sample < PlayfieldPixels
        ? BorderPixels + sample * 2
        : border < BorderPixels
          ? border
          : Graph2FontFile.Width - BorderPixels * 2 + border;

      var at = row + x * 3;
      var entry = Atari8BitGraphics.FindNearestColorByte(gtia, rgb[at], rgb[at + 1], rgb[at + 2]) * 3;
      bgra[sample * 4] = gtia[entry + 2];
      bgra[sample * 4 + 1] = gtia[entry + 1];
      bgra[sample * 4 + 2] = gtia[entry];
      bgra[sample * 4 + 3] = 255;
    }

    var quantized = ColorQuantizer.Quantize(bgra, samples, _REGISTERS);

    Span<int> borderCounts = stackalloc int[_REGISTERS];
    for (var sample = PlayfieldPixels; sample < samples; ++sample)
      ++borderCounts[quantized.Indices[sample]];

    var background = 0;
    for (var entry = 1; entry < quantized.Count; ++entry)
      if (borderCounts[entry] > borderCounts[background])
        background = entry;

    // The background must be the first register, so the rest close up behind it.
    Span<int> places = stackalloc int[_REGISTERS];
    var next = 1;
    for (var entry = 0; entry < quantized.Count; ++entry)
      places[entry] = entry == background ? 0 : next++;

    for (var entry = 0; entry < _REGISTERS; ++entry)
      registers[entry] = 0;

    for (var entry = 0; entry < quantized.Count; ++entry)
      registers[places[entry]] = Atari8BitGraphics.FindNearestColorByte(
        gtia, quantized.Palette[entry * 3], quantized.Palette[entry * 3 + 1], quantized.Palette[entry * 3 + 2]);

    for (var pixel = 0; pixel < PlayfieldPixels; ++pixel)
      pixels[pixel] = (byte)places[quantized.Indices[pixel]];
  }
}
