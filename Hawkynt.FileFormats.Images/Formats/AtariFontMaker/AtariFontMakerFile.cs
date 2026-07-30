using System;
using FileFormat.Core;

namespace FileFormat.AtariFontMaker;

/// <summary>In-memory representation of an Atari FontMaker double character set (.fn2).</summary>
/// <remarks>
/// Two 128-glyph character sets in one file, laid out as a sheet of thirty-two glyphs across and
/// eight bands down. The bands alternate between the two sets, so the sheet reads as the first
/// set's row, then the second's, then the first again — which is how the editor showed a matched
/// pair side by side rather than one after the other.
/// <para/>
/// Drawn in Graphics 0's two colours, which take the hue from the background register and only the
/// luminance from the foreground.
/// </remarks>
public readonly record struct AtariFontMakerFile
  : IImageFormatReader<AtariFontMakerFile>, IImageToRawImage<AtariFontMakerFile> {

  /// <summary>Total file size: two character sets.</summary>
  public const int FileSize = 2048;

  /// <summary>Bytes one character set occupies.</summary>
  public const int SetSize = FileSize / 2;

  /// <summary>Glyphs shown side by side.</summary>
  public const int Columns = 32;

  /// <summary>Scanlines one glyph spans.</summary>
  public const int GlyphHeight = 8;

  /// <summary>Displayed width.</summary>
  public const int Width = Columns * 8;

  /// <summary>Displayed height.</summary>
  public const int Height = 64;

  /// <summary>GTIA colour of the background.</summary>
  public const byte BackgroundColor = 0;

  /// <summary>GTIA colour of the foreground; Graphics 0 takes only its luminance.</summary>
  public const byte ForegroundColor = 14;

  static string IImageFormatMetadata<AtariFontMakerFile>.PrimaryExtension => ".fn2";
  static string[] IImageFormatMetadata<AtariFontMakerFile>.FileExtensions => [".fn2"];
  static AtariFontMakerFile IImageFormatReader<AtariFontMakerFile>.FromSpan(ReadOnlySpan<byte> data)
    => AtariFontMakerReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AtariFontMakerFile>.VideoModes => [
    new("Double character set", [(Width, Height)], [2])
  ];

  /// <summary>The glyph data, both sets one after the other.</summary>
  public byte[] GlyphData { get; init; }

  /// <summary>Which character a band's column shows, and from which of the two sets.</summary>
  /// <remarks>
  /// A band's parity picks the set and its position picks the block of thirty-two, so the two sets
  /// interleave down the sheet rather than following one another.
  /// </remarks>
  public static (int Set, int Character) GlyphAt(int band, int column)
    => (band & 1, (band >> 1) * Columns + column);

  public static RawImage ToRawImage(AtariFontMakerFile file) {
    var font = file.GlyphData ?? [];
    var gtia = Atari8BitGraphics.Palette;

    // Graphics 0 takes the hue from the background and only the luminance from the foreground.
    var foreground = (byte)((BackgroundColor & 240) | (ForegroundColor & 14));
    var palette = new byte[6];
    gtia.Slice(BackgroundColor * 3, 3).CopyTo(palette);
    gtia.Slice(foreground * 3, 3).CopyTo(palette.AsSpan(3));

    var pixels = new byte[Width * Height];
    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var (set, character) = GlyphAt(y / GlyphHeight, x >> 3);
      var index = set * SetSize + ((character & 127) * GlyphHeight) + (y % GlyphHeight);
      var row = index < font.Length ? font[index] : 0;
      pixels[y * Width + x] = (byte)((row >> (~x & 7)) & 1);
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
