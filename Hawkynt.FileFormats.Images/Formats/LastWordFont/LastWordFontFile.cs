using System;
using FileFormat.Core;

namespace FileFormat.LastWordFont;

/// <summary>In-memory representation of a The Last Word font (.f80) file.</summary>
/// <remarks>
/// A bare 512-byte character set: 64 glyphs of eight bytes each, one bit per pixel, most
/// significant bit leftmost. Viewers lay the glyphs out sixteen to a row, giving a 128x32 sheet
/// drawn in the text mode's two colours.
/// </remarks>
public readonly record struct LastWordFontFile
  : IImageFormatReader<LastWordFontFile>, IImageToRawImage<LastWordFontFile>,
    IImageFromRawImage<LastWordFontFile>, IImageFormatWriter<LastWordFontFile> {

  /// <summary>Bytes per glyph.</summary>
  public const int GlyphHeight = 8;

  /// <summary>Glyphs laid out per row.</summary>
  public const int GlyphsPerRow = 16;

  /// <summary>Glyphs in the set.</summary>
  public const int GlyphCount = 64;

  /// <summary>Sheet width in pixels.</summary>
  public const int SheetWidth = GlyphsPerRow * GlyphHeight;

  /// <summary>Sheet height in pixels.</summary>
  public const int SheetHeight = GlyphCount / GlyphsPerRow * GlyphHeight;

  /// <summary>Total file size.</summary>
  public const int FileSize = GlyphCount * GlyphHeight;

  /// <summary>Background and foreground, matching the colours the text mode defaults to.</summary>
  private static ReadOnlySpan<byte> _Palette => [0x00, 0x00, 0x00, 0xDC, 0xDC, 0xDC];

  static string IImageFormatMetadata<LastWordFontFile>.PrimaryExtension => ".f80";
  static string[] IImageFormatMetadata<LastWordFontFile>.FileExtensions => [".f80"];
  static LastWordFontFile IImageFormatReader<LastWordFontFile>.FromSpan(ReadOnlySpan<byte> data)
    => LastWordFontReader.FromSpan(data);
  static byte[] IImageFormatWriter<LastWordFontFile>.ToBytes(LastWordFontFile file)
    => LastWordFontWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<LastWordFontFile>.VideoModes => [
    new("Character set", [(SheetWidth, SheetHeight)], [2])
  ];

  /// <summary>Raw glyph bytes.</summary>
  public byte[] GlyphData { get; init; }

  /// <summary>Byte holding the pixel row that screen row <paramref name="y"/>, column
  /// <paramref name="x"/> falls in.</summary>
  private static int _OffsetOf(int x, int y)
    => ((y / GlyphHeight) * GlyphsPerRow + x / GlyphHeight) * GlyphHeight + (y % GlyphHeight);

  public static RawImage ToRawImage(LastWordFontFile file) {
    var pixels = new byte[SheetWidth * SheetHeight];
    for (var y = 0; y < SheetHeight; ++y)
    for (var x = 0; x < SheetWidth; ++x) {
      var offset = _OffsetOf(x, y);
      if (offset < file.GlyphData.Length)
        pixels[y * SheetWidth + x] = (byte)((file.GlyphData[offset] >> (~x & 7)) & 1);
    }

    return new() {
      Width = SheetWidth,
      Height = SheetHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = _Palette.ToArray(),
      PaletteCount = 2,
    };
  }

  public static LastWordFontFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != SheetWidth || image.Height != SheetHeight)
      throw new ArgumentException($"Expected {SheetWidth}x{SheetHeight} but got {image.Width}x{image.Height}.", nameof(image));

    // One bit per pixel: anything at or above mid-grey sets the glyph bit.
    var grey = PixelConverter.Convert(image, PixelFormat.Gray8);
    var data = new byte[FileSize];

    for (var y = 0; y < SheetHeight; ++y)
    for (var x = 0; x < SheetWidth; ++x) {
      if (grey.PixelData[y * SheetWidth + x] < 128)
        continue;

      data[_OffsetOf(x, y)] |= (byte)(1 << (~x & 7));
    }

    return new() { GlyphData = data };
  }
}
