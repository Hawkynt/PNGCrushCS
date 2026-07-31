using System;
using FileFormat.Core;

namespace FileFormat.OdFontEditor;

/// <summary>In-memory representation of an OD Font Editor character set (.odf) for the Atari 8-bit.</summary>
/// <remarks>
/// A 128-glyph set whose glyphs are ten scanlines tall rather than the usual eight. ANTIC can be
/// told to give a character row more scanlines than the character set has rows, and a font drawn
/// for that has to be ten deep to fill them — which is why the file is 1280 bytes rather than 1024
/// and why nothing that assumes eight can read it.
/// <para/>
/// Shown as thirty-two glyphs across and four bands down, in the two colours the text mode uses.
/// </remarks>
public readonly record struct OdFontEditorFile
  : IImageFormatReader<OdFontEditorFile>, IImageToRawImage<OdFontEditorFile>,
    IImageFromRawImage<OdFontEditorFile>, IImageFormatWriter<OdFontEditorFile> {

  /// <summary>Scanlines one glyph spans.</summary>
  public const int GlyphHeight = 10;

  /// <summary>Glyphs shown side by side.</summary>
  public const int Columns = 32;

  /// <summary>Bands of glyphs down the sheet.</summary>
  public const int Bands = 4;

  /// <summary>Bytes one band of glyphs occupies.</summary>
  public const int BandSize = Columns * GlyphHeight;

  /// <summary>Displayed width.</summary>
  public const int Width = Columns * 8;

  /// <summary>Displayed height.</summary>
  public const int Height = Bands * GlyphHeight;

  /// <summary>Total file size.</summary>
  public const int FileSize = Bands * BandSize;

  /// <summary>GTIA colour of the background.</summary>
  public const byte BackgroundColor = 0;

  /// <summary>GTIA colour of the foreground.</summary>
  public const byte ForegroundColor = 14;

  static string IImageFormatMetadata<OdFontEditorFile>.PrimaryExtension => ".odf";
  static string[] IImageFormatMetadata<OdFontEditorFile>.FileExtensions => [".odf"];
  static OdFontEditorFile IImageFormatReader<OdFontEditorFile>.FromSpan(ReadOnlySpan<byte> data)
    => OdFontEditorReader.FromSpan(data);
  static byte[] IImageFormatWriter<OdFontEditorFile>.ToBytes(OdFontEditorFile file)
    => OdFontEditorWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<OdFontEditorFile>.VideoModes => [
    new("Character set", [(Width, Height)], [2])
  ];

  /// <summary>The glyph data, ten bytes a glyph.</summary>
  public byte[] GlyphData { get; init; }

  public static RawImage ToRawImage(OdFontEditorFile file) {
    var font = file.GlyphData ?? [];
    var gtia = Atari8BitGraphics.Palette;

    var palette = new byte[6];
    gtia.Slice(BackgroundColor * 3, 3).CopyTo(palette);
    gtia.Slice(ForegroundColor * 3, 3).CopyTo(palette.AsSpan(3));

    var pixels = new byte[Width * Height];
    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      // A glyph's ten rows are consecutive, so the band and the column pick a block of ten.
      var index = (y / GlyphHeight) * BandSize + (x >> 3) * GlyphHeight + (y % GlyphHeight);
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

  /// <summary>Reads the sheet back into a character set ten scanlines deep.</summary>
  public static OdFontEditorFile FromRawImage(RawImage image) {
    var set = GlyphSheet.Sample(image, Width, Height);
    var font = new byte[FileSize];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      if (!set[y * Width + x])
        continue;

      font[y / GlyphHeight * BandSize + (x >> 3) * GlyphHeight + y % GlyphHeight] |= (byte)(1 << (~x & 7));
    }

    return new() { GlyphData = font };
  }
}
