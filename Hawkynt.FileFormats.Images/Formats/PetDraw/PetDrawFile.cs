using System;
using FileFormat.Core;
using FileFormat.TextMode;

namespace FileFormat.PetDraw;

/// <summary>In-memory representation of a PetDraw64 screen (.pdr) for the Commodore 64.</summary>
/// <remarks>
/// A text screen with per-cell colour: a small header, then 40x25 character codes, then a colour
/// for each of them. The glyphs come from the machine's character ROM, so the file holds a page of
/// text rather than a bitmap — but unlike the Atari's Graphics 0, every cell picks its own colour
/// freely, and only the background is shared.
/// <para/>
/// The character code's top bit inverts the glyph rather than selecting one, which is how the
/// screen draws solid blocks of colour from a character set that has no solid block.
/// </remarks>
public readonly record struct PetDrawFile
  : IImageFormatReader<PetDrawFile>, IImageToRawImage<PetDrawFile>,
    IImageFromRawImage<PetDrawFile>, IImageFormatWriter<PetDrawFile> {

  static byte[] IImageFormatWriter<PetDrawFile>.ToBytes(PetDrawFile file) => PetDrawWriter.ToBytes(file);

  /// <summary>Characters across.</summary>
  public const int Columns = 40;

  /// <summary>Character rows.</summary>
  public const int Rows = 25;

  /// <summary>Scanlines one glyph spans.</summary>
  public const int GlyphHeight = 8;

  /// <summary>Displayed width.</summary>
  public const int Width = Columns * 8;

  /// <summary>Displayed height.</summary>
  public const int Height = Rows * GlyphHeight;

  /// <summary>Cells on the screen.</summary>
  public const int CellCount = Columns * Rows;

  /// <summary>Offset of the byte holding the shared background colour.</summary>
  public const int BackgroundOffset = 3;

  /// <summary>Offset of the character grid.</summary>
  public const int ScreenOffset = 5;

  /// <summary>Offset of the per-cell colours.</summary>
  public const int ColorsOffset = 1029;

  /// <summary>Total file size.</summary>
  public const int FileSize = 2029;

  /// <summary>Glyphs the character ROM holds; the code's top bit inverts rather than selects.</summary>
  public const int GlyphCount = 128;

  static string IImageFormatMetadata<PetDrawFile>.PrimaryExtension => ".pdr";
  static string[] IImageFormatMetadata<PetDrawFile>.FileExtensions => [".pdr"];
  static PetDrawFile IImageFormatReader<PetDrawFile>.FromSpan(ReadOnlySpan<byte> data) => PetDrawReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<PetDrawFile>.VideoModes => [
    new("PetDraw64", [(Width, Height)], [Commodore64Graphics.ColorCount])
  ];

  /// <summary>The file's bytes, kept whole because the areas are addressed by absolute offset.</summary>
  public byte[] Data { get; init; }

  /// <summary>The machine's character ROM, one byte per glyph row.</summary>
  internal static ReadOnlySpan<byte> Font => BitmapFontEmbedded.C64PetsciiGraphics8x8.GlyphData;

  public static RawImage ToRawImage(PetDrawFile file) {
    var data = file.Data ?? [];
    var font = Font;
    var background = (byte)(_At(data, BackgroundOffset) & 15);
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var cell = (y / GlyphHeight) * Columns + (x >> 3);
      var character = _At(data, ScreenOffset + cell);
      var row = font[(character & (GlyphCount - 1)) * GlyphHeight + (y % GlyphHeight)];
      var set = (((row >> (~x & 7)) ^ (character >> 7)) & 1) != 0;

      // A lit pixel takes the cell's own colour; everything else is the one shared background.
      pixels[y * Width + x] = set ? (byte)(_At(data, ColorsOffset + cell) & 15) : background;
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = Commodore64Graphics.CreatePalette(),
      PaletteCount = Commodore64Graphics.ColorCount,
    };
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset) => offset < data.Length ? data[offset] : (byte)0;

  /// <summary>Builds a screen out of the machine's own character shapes.</summary>
  /// <remarks>
  /// There is no bitmap: a picture here is a grid of characters, each drawn in one colour over a
  /// background the whole screen shares. So each cell gets its own colour first — chosen by trying
  /// all sixteen and keeping whichever splits the cell's pixels best against the background — and
  /// the resulting two-tone cell is then matched against every shape the character set has.
  /// <para/>
  /// The reverse bit, which draws a shape inside out, is left clear: the set already contains the
  /// inverse of the shapes that matter, so using it would double the search for very little.
  /// </remarks>
  public static PetDrawFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(Width, Height).PixelData;
    var c64 = Commodore64Graphics.CreatePalette();
    var background = _ChooseBackground(rgb, c64);

    var wanted = new byte[Width * Height];
    var colors = new byte[CellCount];

    for (var row = 0; row < Rows; ++row)
    for (var column = 0; column < Columns; ++column) {
      var ink = _ChooseInk(rgb, c64, background, column * 8, row * GlyphHeight);
      colors[row * Columns + column] = ink;

      for (var y = 0; y < GlyphHeight; ++y)
      for (var x = 0; x < 8; ++x) {
        var at = ((row * GlyphHeight + y) * Width + column * 8 + x) * 3;
        var isInk = _Distance(rgb, at, c64, ink) < _Distance(rgb, at, c64, background);
        wanted[(row * GlyphHeight + y) * Width + column * 8 + x] = (byte)(isInk ? 1 : 0);
      }
    }

    var characters = CharacterRoms.MatchGlyphs(wanted, Columns, Rows, Font, GlyphCount);

    var data = new byte[FileSize];
    data[BackgroundOffset] = background;
    characters.AsSpan(0, CellCount).CopyTo(data.AsSpan(ScreenOffset));
    colors.CopyTo(data.AsSpan(ColorsOffset));

    return new() { Data = data };
  }

  /// <summary>The one colour the whole screen shows wherever no character is drawn.</summary>
  private static byte _ChooseBackground(ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> c64) {
    byte best = 0;
    var bestCost = long.MaxValue;

    for (byte candidate = 0; candidate < Commodore64Graphics.ColorCount; ++candidate) {
      long cost = 0;
      for (var at = 0; at + 2 < rgb.Length; at += 3)
        cost += _Distance(rgb, at, c64, candidate);

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = candidate;
    }

    return best;
  }

  /// <summary>The colour a cell should draw in, given the background it sits on.</summary>
  private static byte _ChooseInk(ReadOnlySpan<byte> rgb, ReadOnlySpan<byte> c64, byte background, int x0, int y0) {
    byte best = background;
    var bestCost = long.MaxValue;

    for (byte candidate = 0; candidate < Commodore64Graphics.ColorCount; ++candidate) {
      long cost = 0;

      for (var y = y0; y < y0 + GlyphHeight; ++y)
      for (var x = x0; x < x0 + 8; ++x) {
        var at = (y * Width + x) * 3;
        cost += Math.Min(_Distance(rgb, at, c64, candidate), _Distance(rgb, at, c64, background));
      }

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = candidate;
    }

    return best;
  }

  /// <summary>How far one pixel sits from one of the machine's colours.</summary>
  private static long _Distance(ReadOnlySpan<byte> rgb, int at, ReadOnlySpan<byte> c64, int color) {
    var entry = color * 3;
    long dr = rgb[at] - c64[entry], dg = rgb[at + 1] - c64[entry + 1], db = rgb[at + 2] - c64[entry + 2];

    return dr * dr + dg * dg + db * db;
  }
}
