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
  : IImageFormatReader<PetsciiBotFile>, IImageToRawImage<PetsciiBotFile>,
    IImageFromRawImage<PetsciiBotFile>, IImageFormatWriter<PetsciiBotFile> {

  static byte[] IImageFormatWriter<PetsciiBotFile>.ToBytes(PetsciiBotFile file) => PetsciiBotWriter.ToBytes(file);

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

  /// <summary>Builds a picture at whichever of the two shapes the source is nearer.</summary>
  /// <remarks>
  /// Only two sizes exist and nothing in the file states one, so a picture of any other shape is
  /// sampled to the nearer rather than written to a length nothing can read.
  /// <para/>
  /// Everything a character does not draw is black — there is no background colour to choose — so a
  /// cell's own colour is simply the best match for the part of it that is not.
  /// </remarks>
  public static PetsciiBotFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var large = Math.Abs(image.Width - LargeColumns * CellSize) + Math.Abs(image.Height - LargeRows * CellSize)
                <= Math.Abs(image.Width - SmallColumns * CellSize) + Math.Abs(image.Height - SmallRows * CellSize);

    var columns = large ? LargeColumns : SmallColumns;
    var rows = large ? LargeRows : SmallRows;
    int width = columns * CellSize, height = rows * CellSize;

    var rgb = image.SampleTo(width, height).PixelData;
    var c64 = Commodore64Graphics.CreatePalette();
    var cells = columns * rows;

    var wanted = new byte[width * height];
    var data = new byte[cells * 2];

    for (var row = 0; row < rows; ++row)
    for (var column = 0; column < columns; ++column) {
      var ink = _ChooseInk(rgb, c64, width, column * CellSize, row * CellSize);
      data[row * columns + column] = ink;

      for (var y = 0; y < CellSize; ++y)
      for (var x = 0; x < CellSize; ++x) {
        var pixel = (row * CellSize + y) * width + column * CellSize + x;
        var at = pixel * 3;
        wanted[pixel] = (byte)(_Distance(rgb, at, c64, ink) < _Distance(rgb, at, c64, 0) ? 1 : 0);
      }
    }

    CharacterRoms.MatchGlyphs(wanted, columns, rows, Font, GlyphCount).AsSpan(0, cells).CopyTo(data.AsSpan(cells));

    return new() { Columns = columns, Rows = rows, Data = data };
  }

  /// <summary>The colour a cell draws in, black being what it is drawn over.</summary>
  private static byte _ChooseInk(ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> c64, int width, int x0, int y0) {
    byte best = 0;
    var bestCost = long.MaxValue;

    for (byte candidate = 0; candidate < Commodore64Graphics.ColorCount; ++candidate) {
      long cost = 0;

      for (var y = y0; y < y0 + CellSize; ++y)
      for (var x = x0; x < x0 + CellSize; ++x) {
        var at = (y * width + x) * 3;
        cost += Math.Min(_Distance(rgb, at, c64, candidate), _Distance(rgb, at, c64, 0));
      }

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = candidate;
    }

    return best;
  }

  private static long _Distance(ReadOnlySpan<byte> rgb, int at, ReadOnlySpan<byte> c64, int color) {
    var entry = color * 3;
    long dr = rgb[at] - c64[entry], dg = rgb[at + 1] - c64[entry + 1], db = rgb[at + 2] - c64[entry + 2];

    return dr * dr + dg * dg + db * db;
  }
}
