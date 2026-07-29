using System;

namespace FileFormat.MadStudio;

/// <summary>
/// The cell geometry, file layout and colour rules of each Mad Studio character mode.
/// </summary>
/// <remarks>
/// Every mode fills the same 320x192 screen from a grid of character codes and the machine's
/// character set. None of them stores a font: the glyphs come from ROM, so what a file holds is
/// the grid, and at most five colour registers.
/// </remarks>
public static class MadStudioLayout {

  /// <summary>Displayed width.</summary>
  public const int DisplayWidth = 320;

  /// <summary>Displayed height.</summary>
  public const int DisplayHeight = 192;

  /// <summary>Colour registers a mode stores: background, then PF0 to PF3.</summary>
  public const int ColorCount = 5;

  /// <summary>Character codes a mode can use.</summary>
  public const int CharacterCount = 256;

  /// <summary>Rows of pixels in one glyph.</summary>
  public const int GlyphRows = 8;

  /// <summary>Cells across.</summary>
  public static int ColumnsFor(MadStudioMode mode)
    => mode is MadStudioMode.Graphics1 or MadStudioMode.Graphics2 ? 20 : 40;

  /// <summary>Cells down.</summary>
  public static int RowsFor(MadStudioMode mode)
    => mode is MadStudioMode.Antic5 or MadStudioMode.Graphics2 ? 12 : 24;

  /// <summary>Width of one cell in screen pixels.</summary>
  public static int CellWidthFor(MadStudioMode mode) => DisplayWidth / ColumnsFor(mode);

  /// <summary>Height of one cell in screen pixels.</summary>
  public static int CellHeightFor(MadStudioMode mode) => DisplayHeight / RowsFor(mode);

  /// <summary>Whether each glyph row is drawn on two scanlines.</summary>
  public static bool IsDoubleHeight(MadStudioMode mode)
    => mode is MadStudioMode.Antic5 or MadStudioMode.Graphics2;

  /// <summary>Bytes in the character grid.</summary>
  public static int CharacterMapSizeFor(MadStudioMode mode) => ColumnsFor(mode) * RowsFor(mode);

  /// <summary>Whether the colour registers follow the grid rather than preceding it.</summary>
  public static bool ColorsFollowCharacters(MadStudioMode mode)
    => mode is MadStudioMode.Graphics1 or MadStudioMode.Graphics2;

  /// <summary>Bytes before the character grid.</summary>
  /// <remarks>
  /// The ANTIC modes lead with the grid size and, except for the two-colour one, the registers.
  /// The Graphics modes have a fixed grid size and put their registers at the end instead.
  /// </remarks>
  public static int HeaderSizeFor(MadStudioMode mode) => mode switch {
    MadStudioMode.Antic2 => 2,
    MadStudioMode.Antic4 or MadStudioMode.Antic5 => 2 + ColorCount,
    _ => 0,
  };

  /// <summary>Total file size.</summary>
  public static int FileSizeFor(MadStudioMode mode)
    => HeaderSizeFor(mode) + CharacterMapSizeFor(mode) + (ColorsFollowCharacters(mode) ? ColorCount : 0);

  /// <summary>Maps an extension to the mode it names.</summary>
  public static MadStudioMode ModeFromExtension(string extension) => extension.ToLowerInvariant() switch {
    ".an4" => MadStudioMode.Antic4,
    ".an5" => MadStudioMode.Antic5,
    ".gr1" => MadStudioMode.Graphics1,
    ".gr2" => MadStudioMode.Graphics2,
    _ => MadStudioMode.Antic2,
  };

  /// <summary>Recovers the mode from a file size, for callers with no extension to go on.</summary>
  public static MadStudioMode ModeFromLength(int length) {
    foreach (var mode in Enum.GetValues<MadStudioMode>())
      if (FileSizeFor(mode) == length)
        return mode;

    throw new ArgumentOutOfRangeException(nameof(length), length, "Not the size of any Mad Studio screen.");
  }

  /// <summary>
  /// The colour register a character draws a given pixel from, as an index into a background-first
  /// block of five.
  /// </summary>
  /// <remarks>
  /// ANTIC 4 reads two bits per pixel and takes the fourth colour from PF2 or PF3 depending on the
  /// character code's top bit — that bit is a colour switch, not part of the glyph number. The
  /// Graphics modes read one bit per pixel and let the code's top two bits pick the register
  /// outright, which is why they only have 64 glyphs to choose from.
  /// </remarks>
  public static int RegisterFor(MadStudioMode mode, int character, int pixel) {
    if (mode == MadStudioMode.Antic2)
      return pixel;

    if (mode is MadStudioMode.Graphics1 or MadStudioMode.Graphics2)
      return pixel == 0 ? 0 : 1 + (character >> 6);

    return pixel switch {
      0 => 0,
      3 when character >= 128 => 4,
      _ => pixel,
    };
  }

  /// <summary>Glyphs a mode can address; the rest of the code is colour information.</summary>
  public static int GlyphCountFor(MadStudioMode mode)
    => mode is MadStudioMode.Graphics1 or MadStudioMode.Graphics2 ? 64 : 128;

  /// <summary>Pixel values one glyph row yields, before the register lookup.</summary>
  public static int PixelValueCountFor(MadStudioMode mode)
    => mode is MadStudioMode.Antic4 or MadStudioMode.Antic5 ? 4 : 2;

  /// <summary>Reads the pixel value a character shows at a position inside its cell.</summary>
  public static int PixelAt(MadStudioMode mode, ReadOnlySpan<byte> font, int character, int cellX, int cellY) {
    var row = font[(character & (GlyphCountFor(mode) - 1)) * GlyphRows + (IsDoubleHeight(mode) ? cellY >> 1 : cellY)];

    return mode switch {
      // Two bits per pixel, each drawn two screen pixels wide.
      MadStudioMode.Antic4 or MadStudioMode.Antic5 => (row >> (~cellX & 6)) & 3,
      // One bit per pixel, each drawn two screen pixels wide.
      MadStudioMode.Graphics1 or MadStudioMode.Graphics2 => (row >> (~(cellX >> 1) & 7)) & 1,
      // One bit per pixel, and the code's top bit inverts the whole glyph.
      _ => ((row >> (~cellX & 7)) ^ (character >> 7)) & 1,
    };
  }
}
