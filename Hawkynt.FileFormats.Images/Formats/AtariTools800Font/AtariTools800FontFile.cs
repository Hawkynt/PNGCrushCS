using System;
using FileFormat.Core;

namespace FileFormat.AtariTools800Font;

/// <summary>In-memory representation of an AtariTools-800 character set (.acs).</summary>
/// <remarks>
/// Four colour bytes and then a 128-glyph ANTIC 4 character set, shown as a sheet of 16 glyphs
/// across and 8 down. Each glyph is eight bytes, two bits per pixel over four registers, so a cell
/// is eight screen pixels wide and eight tall.
/// <para>
/// The sheet has exactly as many cells as the set has glyphs, so no two cells share one and an
/// arbitrary picture of this size encodes exactly rather than being fitted to a character set.
/// </para>
/// </remarks>
public readonly record struct AtariTools800FontFile
  : IImageFormatReader<AtariTools800FontFile>, IImageToRawImage<AtariTools800FontFile>,
    IImageFromRawImage<AtariTools800FontFile>, IImageFormatWriter<AtariTools800FontFile> {

  /// <summary>Colour bytes stored, in the order background, PF0, PF1, PF2.</summary>
  public const int ColorCount = 4;

  /// <summary>Glyphs in the set.</summary>
  public const int GlyphCount = 128;

  /// <summary>Bytes in one glyph, one per scanline.</summary>
  public const int GlyphSize = 8;

  /// <summary>Size of the character set.</summary>
  public const int FontDataSize = GlyphCount * GlyphSize;

  /// <summary>Total file size.</summary>
  public const int FileSize = ColorCount + FontDataSize;

  /// <summary>Glyphs across the sheet.</summary>
  public const int Columns = 16;

  /// <summary>Glyph rows down the sheet.</summary>
  public const int Rows = GlyphCount / Columns;

  /// <summary>Displayed width.</summary>
  public const int DisplayWidth = Columns * 8;

  /// <summary>Displayed height.</summary>
  public const int DisplayHeight = Rows * 8;

  static string IImageFormatMetadata<AtariTools800FontFile>.PrimaryExtension => ".acs";
  static string[] IImageFormatMetadata<AtariTools800FontFile>.FileExtensions => [".acs"];
  static AtariTools800FontFile IImageFormatReader<AtariTools800FontFile>.FromSpan(ReadOnlySpan<byte> data) => AtariTools800FontReader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariTools800FontFile>.ToBytes(AtariTools800FontFile file) => AtariTools800FontWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AtariTools800FontFile>.VideoModes => [
    new("Character set", [(DisplayWidth, DisplayHeight)], [ColorCount])
  ];

  /// <summary>Colour bytes indexed by pixel value: background first, then PF0, PF1 and PF2.</summary>
  public byte[] Colors { get; init; }

  /// <summary>The character set, eight bytes per glyph.</summary>
  public byte[] FontData { get; init; }

  public static RawImage ToRawImage(AtariTools800FontFile file) {
    var colors = file.Colors ?? [];
    var font = file.FontData ?? [];
    var gtia = Atari8BitGraphics.CreatePalette();

    var palette = new byte[ColorCount * 3];
    for (var i = 0; i < ColorCount; ++i)
      Array.Copy(gtia, (i < colors.Length ? colors[i] & 254 : 0) * 3, palette, i * 3, 3);

    var pixels = new byte[DisplayWidth * DisplayHeight];
    for (var y = 0; y < DisplayHeight; ++y)
    for (var x = 0; x < DisplayWidth; ++x) {
      var glyph = (y >> 3) * Columns + (x >> 3);
      var index = glyph * GlyphSize + (y & 7);
      var row = index < font.Length ? font[index] : 0;
      // Two bits per pixel, most significant pair first.
      pixels[y * DisplayWidth + x] = (byte)((row >> (~x & 6)) & 3);
    }

    return new() {
      Width = DisplayWidth,
      Height = DisplayHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = ColorCount,
    };
  }

  public static AtariTools800FontFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != DisplayWidth || image.Height != DisplayHeight)
      throw new ArgumentException($"Expected {DisplayWidth}x{DisplayHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var gtia = Atari8BitGraphics.CreatePalette();
    var quantized = ColorQuantizer.Quantize(bgra.PixelData, DisplayWidth * DisplayHeight, ColorCount);

    var colors = new byte[ColorCount];
    for (var i = 0; i < ColorCount && i < quantized.Count; ++i)
      colors[i] = Atari8BitGraphics.FindNearestColorByte(
        gtia, quantized.Palette[i * 3], quantized.Palette[i * 3 + 1], quantized.Palette[i * 3 + 2]);

    var font = new byte[FontDataSize];
    for (var y = 0; y < DisplayHeight; ++y)
    for (var x = 0; x < DisplayWidth; x += 2) {
      var glyph = (y >> 3) * Columns + (x >> 3);
      var value = quantized.Indices[y * DisplayWidth + x];
      font[glyph * GlyphSize + (y & 7)] |= (byte)((value & 3) << (~x & 6));
    }

    return new() { Colors = colors, FontData = font };
  }
}
