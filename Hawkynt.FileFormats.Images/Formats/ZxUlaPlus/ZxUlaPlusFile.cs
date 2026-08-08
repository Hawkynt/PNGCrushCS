using System;
using FileFormat.Core;

namespace FileFormat.ZxUlaPlus;

/// <summary>In-memory representation of a ZX Spectrum ULAplus file (6976 bytes: 6144 bitmap + 768 attributes + 64 palette entries).</summary>
public readonly record struct ZxUlaPlusFile
  : IImageFormatReader<ZxUlaPlusFile>, IImageToRawImage<ZxUlaPlusFile>,
    IImageFromRawImage<ZxUlaPlusFile>, IImageFormatWriter<ZxUlaPlusFile> {

  static string IImageFormatMetadata<ZxUlaPlusFile>.PrimaryExtension => ".ulp";
  /// <summary>
  /// Also .scr, which is what both ULAplus pictures in the corpus are named.
  /// </summary>
  /// <remarks>
  /// Only .ulp was claimed and neither sample carries it, so neither was read though RECOIL draws
  /// both. The extension is shared with the ordinary ZX screen at 6912 bytes and the Timex ones at
  /// 12288 and 12289; 6976 belongs to no other, so the lengths tell them apart.
  /// </remarks>
  static string[] IImageFormatMetadata<ZxUlaPlusFile>.FileExtensions => [".ulp", ".scr"];
  static ZxUlaPlusFile IImageFormatReader<ZxUlaPlusFile>.FromSpan(ReadOnlySpan<byte> data) => ZxUlaPlusReader.FromSpan(data);
  static byte[] IImageFormatWriter<ZxUlaPlusFile>.ToBytes(ZxUlaPlusFile file) => ZxUlaPlusWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<ZxUlaPlusFile>.VideoModes => [
    new("Default", [(256, 192)], [16])
  ];

  /// <summary>Always 256.</summary>
  public int Width => 256;

  /// <summary>Always 192.</summary>
  public int Height => 192;

  /// <summary>6144 bytes of 1bpp bitmap data in linear row order.</summary>
  public byte[] BitmapData { get; init; }

  /// <summary>768 bytes of attribute data, one per 8x8 cell.</summary>
  public byte[] AttributeData { get; init; }

  /// <summary>64 bytes of ULAplus palette entries (3-bit GRB encoding per byte).</summary>
  public byte[] PaletteData { get; init; }

  /// <summary>
  /// Widens a palette entry, which holds three bits of green, three of red and two of blue.
  /// </summary>
  /// <remarks>
  /// Each field is spread over the whole byte by repeating its own bits rather than by scaling it,
  /// which is what the hardware does. Scaled, six eighths came out 218 where the machine gives 219 —
  /// a level or two off on most colours the palette can name.
  /// <para/>
  /// Checked against RECOIL: one sample matches it on all 49152 pixels this way and is nowhere near
  /// on the scaled reading; the other is within seven levels a channel, RECOIL having rounded that
  /// one to four bits. ImageMagick draws neither picture, whatever it does with the file.
  /// </remarks>
  internal static int DecodePaletteEntry(byte entry) {
    var g3 = (entry >> 5) & 0x07;
    var r3 = (entry >> 2) & 0x07;
    var b2 = entry & 0x03;

    var r = (r3 << 5) | (r3 << 2) | (r3 >> 1);
    var g = (g3 << 5) | (g3 << 2) | (g3 >> 1);
    var b = (b2 << 6) | (b2 << 4) | (b2 << 2) | b2;

    return (r << 16) | (g << 8) | b;
  }

  /// <summary>Converts this ULAplus screen to Rgb24 using the custom 64-color palette.</summary>
  public static RawImage ToRawImage(ZxUlaPlusFile file) {

    const int width = 256;
    const int height = 192;
    var rgb = new byte[width * height * 3];

    // ULAplus palette: 64 entries organized as 8 sub-palettes of 8 colors each.
    // Attribute byte selects sub-palette (bits 7-6 select palette group, bits 5-3=paper index, bits 2-0=ink index).
    // Sub-palette index = (attribute >> 6) * 2 for paper, (attribute >> 6) * 2 + 1 for ink (simplified).
    // Actually: palette index = (attribute & 0xC0) >> 2 | (color & 0x07) for each of ink/paper.

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var byteIndex = y * 32 + x / 8;
        var bitPosition = 7 - (x % 8);
        var bitValue = (file.BitmapData[byteIndex] >> bitPosition) & 1;

        var cellX = x / 8;
        var cellY = y / 8;
        var attribute = file.AttributeData[cellY * 32 + cellX];

        var paletteGroup = (attribute >> 6) & 0x03;
        var paper = (attribute >> 3) & 0x07;
        var ink = attribute & 0x07;

        // ULAplus palette layout: group * 16 + color for ink (0-7), group * 16 + 8 + color for paper
        int paletteIndex;
        if (bitValue == 1)
          paletteIndex = paletteGroup * 16 + ink;
        else
          paletteIndex = paletteGroup * 16 + 8 + paper;

        // Clamp to 64 entries
        if (paletteIndex >= 64)
          paletteIndex = 0;

        var color = DecodePaletteEntry(file.PaletteData[paletteIndex]);

        var offset = (y * width + x) * 3;
        rgb[offset] = (byte)((color >> 16) & 0xFF);
        rgb[offset + 1] = (byte)((color >> 8) & 0xFF);
        rgb[offset + 2] = (byte)(color & 0xFF);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

  /// <summary>Encodes an 8-bit RGB triplet into a ULAplus palette byte (bits 7-5=G, bits 4-2=R, bits 1-0=B).</summary>
  private static byte _EncodePaletteEntry(byte r, byte g, byte b) {
    var g3 = (g * 7 + 127) / 255;
    var r3 = (r * 7 + 127) / 255;
    var b2 = (b * 3 + 127) / 255;
    return (byte)((g3 << 5) | (r3 << 2) | b2);
  }

  /// <summary>Builds a ULAplus screen from a <see cref="RawImage"/>. The picture's colours are reduced to
  /// a 16-entry palette built from the image itself (not the fixed Spectrum colours), stored in palette
  /// group 0 and mirrored into the other three groups so the group bits never matter. Within each 8x8
  /// cell only the two most common colours survive, since the hardware allows just one ink and one paper
  /// colour per cell.</summary>
  public static ZxUlaPlusFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width != 256 || image.Height != 192)
      throw new ArgumentException($"ZX Spectrum ULAplus screens are always 256x192, but got {image.Width}x{image.Height}.", nameof(image));

    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
    var quant = ColorQuantizer.Quantize(bgra.PixelData, image.Width * image.Height, 16);
    var count = quant.Count;

    var paletteData = new byte[64];
    for (var i = 0; i < 16; ++i) {
      var entry = i < count
        ? _EncodePaletteEntry(quant.Palette[i * 3], quant.Palette[i * 3 + 1], quant.Palette[i * 3 + 2])
        : (byte)0;
      paletteData[i] = entry;
      paletteData[i + 16] = entry;
      paletteData[i + 32] = entry;
      paletteData[i + 48] = entry;
    }

    var bitmap = new byte[6144];
    var attributes = new byte[768];
    const int cellsAcross = 32, cellsDown = 24;

    // The attribute byte can only ever reach palette slots 0-7 as ink and slots 8-15 as paper, so the
    // sixteen quantized colours split into two disjoint eight-colour pools by construction — a cell picks
    // its best candidate from each pool independently.
    Span<int> inkCounts = stackalloc int[8];
    Span<int> paperCounts = stackalloc int[8];
    for (var cellY = 0; cellY < cellsDown; ++cellY)
    for (var cellX = 0; cellX < cellsAcross; ++cellX) {
      inkCounts.Clear();
      paperCounts.Clear();
      for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x) {
        var idx = quant.Indices[(cellY * 8 + y) * 256 + cellX * 8 + x];
        if (idx < 8)
          ++inkCounts[idx];
        else
          ++paperCounts[idx - 8];
      }

      var ink = 0;
      for (var c = 1; c < 8; ++c)
        if (inkCounts[c] > inkCounts[ink])
          ink = c;

      var paper = 0;
      for (var c = 1; c < 8; ++c)
        if (paperCounts[c] > paperCounts[paper])
          paper = c;

      attributes[cellY * cellsAcross + cellX] = (byte)((paper << 3) | ink);

      for (var y = 0; y < 8; ++y) {
        byte rowByte = 0;
        for (var x = 0; x < 8; ++x) {
          var idx = quant.Indices[(cellY * 8 + y) * 256 + cellX * 8 + x];
          if (idx == ink)
            rowByte |= (byte)(0x80 >> x);
        }

        bitmap[(cellY * 8 + y) * 32 + cellX] = rowByte;
      }
    }

    return new() { BitmapData = bitmap, AttributeData = attributes, PaletteData = paletteData };
  }

}
