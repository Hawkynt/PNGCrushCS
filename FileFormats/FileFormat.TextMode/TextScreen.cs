using System;

namespace FileFormat.TextMode;

/// <summary>
/// Platform-independent text-mode screen: a column×row grid of <see cref="TextCell"/>s,
/// plus an optional 16-colour RGB palette and an optional embedded bitmap font.
/// </summary>
public sealed record TextScreen {

  public int ColumnCount { get; init; }
  public int RowCount { get; init; }
  public TextCell[] Cells { get; init; } = Array.Empty<TextCell>();

  /// <summary>16-colour palette as 48 bytes (R,G,B per entry). Null = use <see cref="TextPalette.DefaultEga"/>.</summary>
  public byte[]? Palette { get; init; }

  /// <summary>Embedded font (null = use <see cref="BitmapFont.DefaultVga8x16"/>).</summary>
  public BitmapFont? Font { get; init; }

  public TextCell GetCell(int column, int row) {
    if ((uint)column >= (uint)ColumnCount) throw new ArgumentOutOfRangeException(nameof(column));
    if ((uint)row >= (uint)RowCount) throw new ArgumentOutOfRangeException(nameof(row));
    return Cells[row * ColumnCount + column];
  }
}
