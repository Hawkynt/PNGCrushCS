using FileFormat.Core;

namespace FileFormat.TextMode;

/// <summary>
/// The sizes a text-mode picture can be, shared by the formats that store one.
/// </summary>
/// <remarks>
/// ANSI, NFO and XBIN all store a grid of characters rather than a grid of pixels, so the only
/// widths and heights they can express are whole cells of the font. All three declared no sizes at
/// all, which reads as "any size will do" — and all three then threw when handed one that was not a
/// multiple of the cell. Saying it once, here, keeps the three answers from drifting apart.
/// <para/>
/// The step follows <see cref="BitmapFont.Default"/> rather than naming 8 and 16, because the same
/// formats accept a different grid when the font is swapped and a hard-coded step would then be
/// wrong in the one case the caller changed something.
/// </remarks>
internal static class TextModeGrid {

  /// <summary>The most cells across or down any of these formats stores.</summary>
  /// <remarks>XBIN holds each count in sixteen bits; the others are bounded by good sense.</remarks>
  private const int MaxCells = 4096;

  /// <summary>Whole cells of the current font, in both directions.</summary>
  public static (IntegerRange Width, IntegerRange Height) Dimensions {
    get {
      var font = BitmapFont.Default;
      return (
        new IntegerRange(font.CellWidth, font.CellWidth * MaxCells, font.CellWidth),
        new IntegerRange(font.CellHeight, font.CellHeight * MaxCells, font.CellHeight)
      );
    }
  }
}
