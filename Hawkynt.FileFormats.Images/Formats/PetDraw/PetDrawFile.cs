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
  : IImageFormatReader<PetDrawFile>, IImageToRawImage<PetDrawFile> {

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
}
