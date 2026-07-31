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

  /// <summary>
  /// Chooses the attribute and the eight bitmap bytes that come closest to one character cell.
  /// </summary>
  /// <param name="rgb">The picture, three bytes a pixel.</param>
  /// <param name="width">Pixels across the picture.</param>
  /// <param name="left">The cell's leftmost column.</param>
  /// <param name="top">The cell's topmost row.</param>
  /// <param name="bits">Receives the cell's eight rows, a bit a pixel, ink where set.</param>
  /// <remarks>
  /// This is the whole of what makes a Spectrum picture a Spectrum picture. A cell may show two
  /// colours out of fifteen and no more, and the two must agree about brightness — the bright bit
  /// belongs to the cell, not to either colour — so the choice is over 128 pairs rather than over
  /// all 240. Trying every pair is cheaper than any cleverness: it is 128 comparisons against 64
  /// pixels, and it is exact.
  /// <para/>
  /// No error is diffused across a cell boundary. A dither would spread a colour into a cell that
  /// cannot show it, and the attribute clash that results is worse than the banding it fixes —
  /// which is why Spectrum artists dithered within a cell and never across one.
  /// </remarks>
  public static byte ChooseCell(ReadOnlySpan<byte> rgb, int width, int left, int top, Span<byte> bits) {
    var best = (byte)0;
    var bestCost = long.MaxValue;
    Span<byte> bestBits = stackalloc byte[8];

    for (var bright = 0; bright < 2; ++bright)
    for (var ink = 0; ink < 8; ++ink)
    for (var paper = 0; paper < 8; ++paper) {
      var inkEntry = (bright * 8 + ink) * 3;
      var paperEntry = (bright * 8 + paper) * 3;
      var cost = 0L;
      Span<byte> pattern = stackalloc byte[8];

      for (var y = 0; y < 8; ++y) {
        var value = 0;
        for (var x = 0; x < 8; ++x) {
          var at = ((top + y) * width + left + x) * 3;
          if (at + 2 >= rgb.Length)
            continue;

          var inkCost = _Distance(rgb, at, inkEntry);
          var paperCost = _Distance(rgb, at, paperEntry);

          if (inkCost <= paperCost) {
            value |= 1 << (7 - x);
            cost += inkCost;
          } else
            cost += paperCost;
        }

        pattern[y] = (byte)value;
      }

      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = Attribute(bright * 8 + ink, bright * 8 + paper);
      pattern.CopyTo(bestBits);

      if (cost == 0)
        goto done;
    }

    done:
    bestBits.CopyTo(bits);

    return best;
  }

  /// <summary>How far a pixel is from a palette entry, weighted the way the eye weights it.</summary>
  private static long _Distance(ReadOnlySpan<byte> rgb, int pixel, int entry) {
    long dr = rgb[pixel] - Palette[entry];
    long dg = rgb[pixel + 1] - Palette[entry + 1];
    long db = rgb[pixel + 2] - Palette[entry + 2];

    return dr * dr * 77 + dg * dg * 150 + db * db * 29;
  }
}
