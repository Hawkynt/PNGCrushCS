using System;

namespace FileFormat.TextMode;

/// <summary>
/// A fixed-width bitmap font: 1 byte per row, MSB-leftmost pixel; <see cref="CellWidth"/> ≤ 8.
/// The default <see cref="DefaultVga8x16"/> is procedurally generated to cover ASCII + CP437 box drawing,
/// shade blocks, and half blocks. Production users can replace it via <see cref="FromBytes"/> with an
/// authentic VGA ROM font (4096 bytes = 256 glyphs × 16 rows) loaded from a .F16 file.
/// </summary>
public sealed record BitmapFont {

  public int CellWidth { get; }
  public int CellHeight { get; }
  public byte[] GlyphData { get; }

  private BitmapFont(int width, int height, byte[] glyphData) {
    if (width is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(width), "1 ≤ CellWidth ≤ 8.");
    if (height < 1) throw new ArgumentOutOfRangeException(nameof(height), "CellHeight ≥ 1.");
    if (glyphData.Length != 256 * height)
      throw new ArgumentException($"Glyph data must be exactly 256×{height} = {256 * height} bytes (was {glyphData.Length}).", nameof(glyphData));
    this.CellWidth = width;
    this.CellHeight = height;
    this.GlyphData = glyphData;
  }

  /// <summary>Construct a font from a raw glyph byte array (e.g. contents of a .F16 file = 4096 bytes for 8×16).</summary>
  public static BitmapFont FromBytes(int cellWidth, int cellHeight, byte[] glyphData)
    => new(cellWidth, cellHeight, (byte[])glyphData.Clone());

  /// <summary>Return one row of pixels for the given CP437 byte: byte where bit 7 = leftmost pixel.</summary>
  public byte GetGlyphRow(byte codePoint, int row) {
    if ((uint)row >= (uint)CellHeight) throw new ArgumentOutOfRangeException(nameof(row));
    return GlyphData[codePoint * CellHeight + row];
  }

  private static BitmapFont? _defaultVga;
  /// <summary>Shared procedurally-generated 8×16 VGA-style font (covers ASCII + box drawing + shades + blocks).</summary>
  public static BitmapFont DefaultVga8x16 => _defaultVga ??= new BitmapFont(8, 16, ProceduralVgaFont.Build());

  /// <summary>
  /// Active font used by format writers/renderers when no explicit font is passed. The
  /// <see cref="FontCodepageWindow"/> UI picker writes here before save so the user's choice
  /// flows through to NFO/ANSI/XBIN quantizers without changing the IImageFormatWriter contract.
  /// </summary>
  public static BitmapFont Default { get; set; } = DefaultVga8x16;
}
