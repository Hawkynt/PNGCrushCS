using System;
using FileFormat.Core;
using FileFormat.TextMode;

namespace FileFormat.AsciiMaker;

/// <summary>In-memory representation of an ASCII maker screen (.asc) for the Atari 8-bit.</summary>
/// <remarks>
/// A Graphics 0 text screen saved verbatim: 40x24 character codes and nothing else. The glyphs are
/// not in the file — they come from the machine's character ROM — so what is stored is a page of
/// text and what is shown is 320x192 pixels of it.
/// <para/>
/// The two colours are the ones Graphics 0 always uses, and they are not a normal pair: the
/// background supplies the hue and the foreground only its luminance, so text can never be a
/// different colour from its background, only a different brightness. Here that is black paper and
/// luminance 14.
/// </remarks>
public readonly record struct AsciiMakerFile
  : IImageFormatReader<AsciiMakerFile>, IImageToRawImage<AsciiMakerFile> {

  /// <summary>Characters across.</summary>
  public const int Columns = 40;

  /// <summary>Character rows.</summary>
  public const int Rows = 24;

  /// <summary>Scanlines one glyph spans.</summary>
  public const int GlyphHeight = 8;

  /// <summary>Displayed width.</summary>
  public const int Width = Columns * 8;

  /// <summary>Displayed height.</summary>
  public const int Height = Rows * GlyphHeight;

  /// <summary>Size of the character grid.</summary>
  public const int ScreenSize = Columns * Rows;

  /// <summary>The larger size a file may have: the grid padded to a whole page.</summary>
  public const int PaddedSize = 1024;

  /// <summary>Glyphs the character ROM holds; the code's top bit inverts rather than selects.</summary>
  public const int GlyphCount = 128;

  /// <summary>The background colour, which supplies the hue.</summary>
  public const byte BackgroundColor = 0;

  /// <summary>The foreground colour, of which only the luminance is used.</summary>
  public const byte ForegroundColor = 14;

  static string IImageFormatMetadata<AsciiMakerFile>.PrimaryExtension => ".asc";
  static string[] IImageFormatMetadata<AsciiMakerFile>.FileExtensions => [".asc", ".gr0"];
  static AsciiMakerFile IImageFormatReader<AsciiMakerFile>.FromSpan(ReadOnlySpan<byte> data) => AsciiMakerReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AsciiMakerFile>.VideoModes => [
    new("Graphics 0", [(Width, Height)], [2])
  ];

  /// <summary>The character grid.</summary>
  public byte[] Characters { get; init; }

  /// <summary>The machine's character ROM, one byte per glyph row.</summary>
  internal static ReadOnlySpan<byte> Font => BitmapFontEmbedded.AtariAtascii8x8.GlyphData;

  /// <summary>The two colours as GTIA colour bytes.</summary>
  /// <remarks>
  /// Graphics 0 takes the hue from the background register and only the luminance from the
  /// foreground one, which is why the foreground is masked rather than used whole.
  /// </remarks>
  internal static (byte Background, byte Foreground) Colors
    => (BackgroundColor, (byte)((BackgroundColor & 240) | (ForegroundColor & 14)));

  public static RawImage ToRawImage(AsciiMakerFile file) {
    var characters = file.Characters ?? [];
    var font = Font;
    var (background, foreground) = Colors;
    var gtia = Atari8BitGraphics.Palette;

    var palette = new byte[6];
    gtia.Slice(background * 3, 3).CopyTo(palette);
    gtia.Slice(foreground * 3, 3).CopyTo(palette.AsSpan(3));

    var pixels = new byte[Width * Height];
    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var cell = (y / GlyphHeight) * Columns + (x >> 3);
      var character = cell < characters.Length ? characters[cell] : 0;
      // The code's top bit is not part of the glyph number: it inverts the glyph.
      var row = font[(character & (GlyphCount - 1)) * GlyphHeight + (y % GlyphHeight)];
      pixels[y * Width + x] = (byte)(((row >> (~x & 7)) ^ (character >> 7)) & 1);
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = 2,
    };
  }
}
