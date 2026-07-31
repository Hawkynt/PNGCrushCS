using System;

namespace FileFormat.Core;

/// <summary>Primitives shared by the ZX Spectrum screen formats.</summary>
public static class ZxSpectrumGraphics {

  /// <summary>Screen width in pixels.</summary>
  public const int ScreenWidth = 256;

  /// <summary>Screen height in pixels.</summary>
  public const int ScreenHeight = 192;

  /// <summary>Bytes per scanline.</summary>
  public const int BytesPerRow = ScreenWidth / 8;

  /// <summary>Size of the bitmap area.</summary>
  public const int BitmapSize = BytesPerRow * ScreenHeight;

  /// <summary>
  /// Byte offset of the start of scanline <paramref name="y"/> within the bitmap.
  /// </summary>
  /// <remarks>
  /// The Spectrum's display file is famously not in scanline order. It is addressed as third,
  /// character row within the third, and scanline within the character — so consecutive stored
  /// rows are eight pixel rows apart, and reading the file linearly produces a sheared image.
  /// </remarks>
  public static int LineOffset(int y) => ((y & 192) << 5) + ((y & 7) << 8) + ((y & 56) << 2);

  /// <summary>The eight base colours, at normal and bright intensity.</summary>
  public static ReadOnlySpan<byte> Palette => [
    0x00, 0x00, 0x00, 0x00, 0x00, 0xCD, 0xCD, 0x00, 0x00, 0xCD, 0x00, 0xCD,
    0x00, 0xCD, 0x00, 0x00, 0xCD, 0xCD, 0xCD, 0xCD, 0x00, 0xCD, 0xCD, 0xCD,
    0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0xFF, 0x00, 0xFF,
    0x00, 0xFF, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0xFF, 0xFF, 0xFF,
  ];

  /// <summary>Palette entries: eight colours at two intensities.</summary>
  public const int PaletteEntryCount = 16;

  /// <summary>Builds the palette index an attribute byte selects for a set or clear bitmap bit.</summary>
  public static int ColorIndex(byte attribute, bool inkSet) {
    var bright = (attribute >> 6) & 1;
    var color = inkSet ? attribute & 7 : (attribute >> 3) & 7;
    return bright * 8 + color;
  }

  /// <summary>The colour an attribute byte names, as 0xRRGGBB.</summary>
  public static int HexColor(byte attribute, bool inkSet) {
    var entry = ColorIndex(attribute, inkSet) * 3;
    return (Palette[entry] << 16) | (Palette[entry + 1] << 8) | Palette[entry + 2];
  }

  /// <summary>Writes the colour an attribute byte names as an RGB triplet.</summary>
  public static void WriteRgb(Span<byte> target, int offset, byte attribute, bool inkSet) {
    var entry = ColorIndex(attribute, inkSet) * 3;
    target[offset] = Palette[entry];
    target[offset + 1] = Palette[entry + 1];
    target[offset + 2] = Palette[entry + 2];
  }

  /// <summary>Builds an attribute byte from an ink and paper index.</summary>
  public static byte Attribute(int ink, int paper) {
    var bright = ink >= 8 || paper >= 8 ? 1 : 0;
    return (byte)((bright << 6) | (((paper & 7)) << 3) | (ink & 7));
  }
}
