using System;
using FileFormat.Core;

namespace FileFormat.ZxFont;

/// <summary>In-memory representation of a ZX Spectrum character set (.ch4/.ch6/.ch8).</summary>
/// <remarks>
/// A bare glyph bitmap with no header: eight bytes per character, one bit per pixel, most
/// significant bit leftmost. Viewers lay the glyphs out 32 to a row, so a file of N bytes renders
/// as a 256-pixel-wide sheet of <c>ceil(N / 256) * 8</c> rows. The three extensions describe how
/// wide the glyphs are meant to be drawn (4, 6 or 8 pixels) but the storage is identical, so they
/// share one implementation.
/// </remarks>
public readonly record struct ZxFontFile
  : IImageFormatReader<ZxFontFile>, IImageToRawImage<ZxFontFile>,
    IImageFromRawImage<ZxFontFile>, IImageFormatWriter<ZxFontFile> {

  /// <summary>Bytes per glyph.</summary>
  public const int GlyphHeight = 8;

  /// <summary>Glyphs laid out per row.</summary>
  public const int GlyphsPerRow = 32;

  /// <summary>Sheet width in pixels.</summary>
  public const int SheetWidth = GlyphsPerRow * GlyphHeight;

  /// <summary>Bytes in one row of glyphs.</summary>
  public const int BytesPerGlyphRow = GlyphsPerRow * GlyphHeight;

  /// <summary>Size written for a full 256-glyph set.</summary>
  public const int FullSetSize = 256 * GlyphHeight;

  /// <summary>Sheet height for a full 256-glyph set.</summary>
  public const int FullSetHeight = FullSetSize / BytesPerGlyphRow * GlyphHeight;

  static string IImageFormatMetadata<ZxFontFile>.PrimaryExtension => ".ch8";
  static string[] IImageFormatMetadata<ZxFontFile>.FileExtensions => [".ch8", ".ch4", ".ch6"];
  static ZxFontFile IImageFormatReader<ZxFontFile>.FromSpan(ReadOnlySpan<byte> data) => ZxFontReader.FromSpan(data);
  static byte[] IImageFormatWriter<ZxFontFile>.ToBytes(ZxFontFile file) => ZxFontWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ZxFontFile>.VideoModes => [
    new("Character set", [(SheetWidth, FullSetHeight)], [2])
  ];

  /// <summary>Raw glyph bytes.</summary>
  public byte[] GlyphData { get; init; }

  /// <summary>Sheet height for the stored glyph count.</summary>
  public static int HeightFor(int byteCount) => (byteCount + BytesPerGlyphRow - GlyphHeight) / BytesPerGlyphRow * GlyphHeight;

  /// <summary>Byte offset of the glyph row containing screen row <paramref name="y"/>, for column <paramref name="x"/>.</summary>
  private static int _OffsetOf(int x, int y) {
    var rowInGlyph = y % GlyphHeight;
    return (y - rowInGlyph) * GlyphsPerRow + (x / GlyphHeight) * GlyphHeight + rowInGlyph;
  }

  public static RawImage ToRawImage(ZxFontFile file) {
    var data = file.GlyphData ?? [];
    var height = HeightFor(data.Length);
    var pixels = new byte[SheetWidth * height];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < SheetWidth; ++x) {
      var offset = _OffsetOf(x, y);
      if (offset >= data.Length)
        continue;

      pixels[y * SheetWidth + x] = (byte)((data[offset] >> (~x & 7)) & 1);
    }

    return new() {
      Width = SheetWidth,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = [0, 0, 0, 255, 255, 255],
      PaletteCount = 2,
    };
  }

  public static ZxFontFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != SheetWidth)
      throw new ArgumentException($"Expected a {SheetWidth}-pixel-wide sheet but got {image.Width}.", nameof(image));
    if (image.Height % GlyphHeight != 0)
      throw new ArgumentException($"Sheet height must be a multiple of {GlyphHeight} but got {image.Height}.", nameof(image));

    // One bit per pixel: anything at or above mid-grey is set.
    var grey = PixelConverter.Convert(image, PixelFormat.Gray8);
    var data = new byte[image.Height / GlyphHeight * BytesPerGlyphRow];

    for (var y = 0; y < image.Height; ++y)
    for (var x = 0; x < SheetWidth; ++x) {
      if (grey.PixelData[y * SheetWidth + x] < 128)
        continue;

      var offset = _OffsetOf(x, y);
      if (offset < data.Length)
        data[offset] |= (byte)(1 << (~x & 7));
    }

    return new() { GlyphData = data };
  }
}
