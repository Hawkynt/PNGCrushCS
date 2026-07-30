using System;
using FileFormat.Core;
using FileFormat.TextMode;

namespace FileFormat.PetsciiBot;

/// <summary>In-memory representation of a PETSCII BOT picture (.pbot) for the Commodore 64.</summary>
/// <remarks>
/// A small character-cell picture: a colour for every cell, then a character code for every cell,
/// and nothing else — no header, no background, no dimensions. The two sizes it comes in are the
/// two shapes the tool drew, and the length is what tells them apart.
/// <para/>
/// PETSCII art works by choosing characters for their shape rather than their meaning, which is why
/// a picture this small can be recognisable: a quarter-block or a diagonal is a pixel that happens
/// to have a letter's name.
/// </remarks>
public readonly record struct PetsciiBotFile
  : IImageFormatReader<PetsciiBotFile>, IImageToRawImage<PetsciiBotFile> {

  /// <summary>Pixels a character cell spans in each direction.</summary>
  public const int CellSize = 8;

  /// <summary>Glyphs the character ROM holds; the code's top bit inverts rather than selects.</summary>
  public const int GlyphCount = 128;

  /// <summary>The small shape, in cells.</summary>
  public const int SmallColumns = 5;

  /// <summary>Rows of the small shape.</summary>
  public const int SmallRows = 7;

  /// <summary>The large shape, in cells.</summary>
  public const int LargeColumns = 12;

  /// <summary>Rows of the large shape.</summary>
  public const int LargeRows = 16;

  static string IImageFormatMetadata<PetsciiBotFile>.PrimaryExtension => ".pbot";
  static string[] IImageFormatMetadata<PetsciiBotFile>.FileExtensions => [".pbot"];
  static PetsciiBotFile IImageFormatReader<PetsciiBotFile>.FromSpan(ReadOnlySpan<byte> data)
    => PetsciiBotReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<PetsciiBotFile>.VideoModes => [
    new("Small", [(SmallColumns * CellSize, SmallRows * CellSize)], [Commodore64Graphics.ColorCount]),
    new("Large", [(LargeColumns * CellSize, LargeRows * CellSize)], [Commodore64Graphics.ColorCount]),
  ];

  /// <summary>Cells across.</summary>
  public int Columns { get; init; }

  /// <summary>Cells down.</summary>
  public int Rows { get; init; }

  /// <summary>The file's bytes: the colours then the character codes.</summary>
  public byte[] Data { get; init; }

  /// <summary>The machine's character ROM, one byte per glyph row.</summary>
  internal static ReadOnlySpan<byte> Font => BitmapFontEmbedded.C64PetsciiGraphics8x8.GlyphData;

  public static RawImage ToRawImage(PetsciiBotFile file) {
    var data = file.Data ?? [];
    var font = Font;
    var width = file.Columns * CellSize;
    var height = file.Rows * CellSize;
    var cells = file.Columns * file.Rows;
    var pixels = new byte[width * height];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cell = (y / CellSize) * file.Columns + (x / CellSize);
      // Colours come first, characters after them.
      var character = _At(data, cells + cell);
      var row = font[(character & (GlyphCount - 1)) * CellSize + (y % CellSize)];
      var set = (((row >> (~x & 7)) ^ (character >> 7)) & 1) != 0;

      // A lit pixel takes the cell's colour; the rest of the cell is black.
      pixels[y * width + x] = set ? (byte)(_At(data, cell) & 15) : (byte)0;
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = Commodore64Graphics.CreatePalette(),
      PaletteCount = Commodore64Graphics.ColorCount,
    };
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;
}
