using System;

namespace FileFormat.Core;

/// <summary>Primitives shared by the MSX picture formats: the V9938 palette, the BSAVE container
/// and the V9958's YJK colour model.</summary>
public static class MsxGraphics {

  /// <summary>First byte of a BSAVE file.</summary>
  public const byte BsaveMagic = 0xFE;

  /// <summary>Size of a BSAVE header: the magic byte, then start, end and execution addresses.</summary>
  public const int BsaveHeaderSize = 7;

  /// <summary>Bytes one palette entry occupies: <c>0RRR0BBB</c> then <c>00000GGG</c>.</summary>
  public const int PaletteEntrySize = 2;

  /// <summary>The end address a BSAVE header declares, or -1 when the header is malformed.</summary>
  /// <remarks>
  /// Readers derive the picture height from this rather than from the file length, so the address
  /// has to describe the bitmap. The four bytes that must be zero are the high halves of the start
  /// and execution addresses; a file with anything else there is not a picture.
  /// </remarks>
  public static int ReadBsaveEndAddress(ReadOnlySpan<byte> data)
    => data.Length < BsaveHeaderSize || data[1] != 0 || data[2] != 0 || data[5] != 0 || data[6] != 0
      ? -1
      : data[3] | (data[4] << 8);

  /// <summary>Writes a BSAVE header describing a bitmap ending at a given address.</summary>
  public static void WriteBsaveHeader(Span<byte> data, int endAddress) {
    data[0] = BsaveMagic;
    data[3] = (byte)endAddress;
    data[4] = (byte)(endAddress >> 8);
  }

  /// <summary>Expands one of the palette's three-bit channels to eight bits.</summary>
  private static byte _Expand3(int value) => (byte)((value << 5) | (value << 2) | (value >> 1));

  /// <summary>Expands one of YJK's five-bit channels to eight bits.</summary>
  private static byte _Expand5(int value) => (byte)((value << 3) | (value >> 2));

  /// <summary>Converts a stored V9938 palette to RGB triplets.</summary>
  /// <summary>Writes one four-bit pixel, high nibble first, which is the order they are read in.</summary>
  public static void SetNibble(Span<byte> data, int offset, int index, int value) {
    var position = offset + (index >> 1);
    if (position >= data.Length)
      return;

    data[position] = (index & 1) == 0
      ? (byte)((data[position] & 0x0F) | ((value & 15) << 4))
      : (byte)((data[position] & 0xF0) | (value & 15));
  }

  /// <summary>The sixteen colours a V9938 powers up with, as stored palette entries.</summary>
  /// <remarks>
  /// A picture whose palette file is missing draws in these. They are not a grey ramp or an even
  /// spread: entry one is transparent-black, and the rest are the TMS9918's colours carried over so
  /// that older software kept looking the way it always had.
  /// </remarks>
  public static ReadOnlySpan<byte> Msx2DefaultPalette => [
    0, 0, 0, 0, 17, 6, 51, 7, 23, 1, 39, 3, 81, 1, 39, 6,
    113, 1, 115, 3, 97, 6, 100, 6, 17, 4, 101, 2, 85, 5, 119, 7,
  ];

  public static byte[] PaletteToRgb(ReadOnlySpan<byte> palette, int colors) {
    var rgb = new byte[colors * 3];
    for (var i = 0; i < colors && i * PaletteEntrySize + 1 < palette.Length; ++i) {
      // Red and blue share the first byte, three bits each; green has the second to itself.
      var rb = palette[i * PaletteEntrySize];
      rgb[i * 3] = _Expand3((rb >> 4) & 7);
      rgb[i * 3 + 1] = _Expand3(palette[i * PaletteEntrySize + 1] & 7);
      rgb[i * 3 + 2] = _Expand3(rb & 7);
    }

    return rgb;
  }

  /// <summary>Converts RGB triplets to a stored V9938 palette.</summary>
  public static byte[] PaletteFromRgb(ReadOnlySpan<byte> rgb, int count, int colors) {
    var palette = new byte[colors * PaletteEntrySize];
    for (var i = 0; i < colors && i < count; ++i) {
      palette[i * PaletteEntrySize] = (byte)((_Reduce3(rgb[i * 3]) << 4) | _Reduce3(rgb[i * 3 + 2]));
      palette[i * PaletteEntrySize + 1] = (byte)_Reduce3(rgb[i * 3 + 1]);
    }

    return palette;
  }

  private static int _Reduce3(byte value) => (value * 7 + 127) / 255;

  /// <summary>
  /// The sixteen colours an MSX2 starts up with, in the stored two-byte form.
  /// </summary>
  /// <remarks>
  /// Several formats keep their palette in a companion file rather than in the picture. When that
  /// file is absent — and for a picture handed over as bytes it always is — this is what the machine
  /// would have been showing, so it is what the picture means.
  /// </remarks>
  public static ReadOnlySpan<byte> DefaultPalette => [
    0, 0, 0, 0, 17, 6, 51, 7, 23, 1, 39, 3, 81, 1, 39, 6,
    113, 1, 115, 3, 97, 6, 100, 6, 17, 4, 101, 2, 85, 5, 119, 7,
  ];

  /// <summary>
  /// The four colours a Screen 6 picture shows when no companion palette sits beside it.
  /// </summary>
  /// <remarks>
  /// Black and three greens — what the machine starts up with. Formats that keep their palette in a
  /// separate file all fall back to this, so it is not a per-format default but a property of the
  /// screen mode.
  /// </remarks>
  public static ReadOnlySpan<byte> Screen6DefaultPaletteRgb => [
    0, 0, 0, 0x24, 0x92, 0x24, 0x24, 0xDB, 0x24, 0x6D, 0xFF, 0x6D,
  ];

  /// <summary>
  /// The sixteen colours a TMS9918 produces, as measured from hardware rather than idealised.
  /// </summary>
  /// <remarks>
  /// The widely-copied table of round numbers — 0x21C842 for medium green, 0xFFFFFF for white — is
  /// a reconstruction, and no television ever showed it. The chip generates its colours as
  /// composite video directly rather than through a palette, so what comes out is neither
  /// saturated nor neutral: black is not quite black, white is not quite white, and the greens sit
  /// nowhere an even scheme would put them. These are the measured values, so our output and the
  /// reference decoder's agree exactly rather than approximately.
  /// </remarks>
  public static ReadOnlySpan<byte> Tms9918Palette => [
    0x00, 0x08, 0x00, 0x00, 0x04, 0x00, 0x3A, 0xBB, 0x43, 0x70, 0xD3, 0x77,
    0x54, 0x59, 0xD7, 0x7B, 0x7B, 0xE8, 0xB3, 0x63, 0x4B, 0x61, 0xDF, 0xE7,
    0xD4, 0x6A, 0x53, 0xF8, 0x8E, 0x77, 0xC7, 0xC7, 0x59, 0xD9, 0xD4, 0x81,
    0x36, 0xA5, 0x3B, 0xB0, 0x6B, 0xAE, 0xC7, 0xD0, 0xC5, 0xFA, 0xFF, 0xF8,
  ];

  /// <summary>
  /// Whether a stored V9938 palette sits at an offset, rather than whatever else happens to.
  /// </summary>
  /// <remarks>
  /// Screen 2 files sometimes carry sixteen palette entries in otherwise unused video memory, which
  /// upgrades a TMS9918 picture to an MSX2 one. Nothing marks them as present, so the only test is
  /// that the bytes could be a palette at all: each entry uses three bits per channel in fixed
  /// positions, so the bits outside them must be clear — and at least one entry must be non-zero,
  /// since sixteen blacks are what unused memory looks like.
  /// </remarks>
  public static bool HasPaletteAt(ReadOnlySpan<byte> data, int offset) {
    if (offset < 0 || offset + 32 > data.Length)
      return false;

    var ored = 0;
    for (var i = 0; i < 16; ++i) {
      int rb = data[offset + i * 2], g = data[offset + i * 2 + 1];
      if ((rb & 136) != 0 || (g & 248) != 0)
        return false;

      ored |= rb | g;
    }

    return ored != 0;
  }

  /// <summary>
  /// Draws the sprite plane over an already-rendered indexed screen.
  /// </summary>
  /// <param name="mode">The screen mode, which decides how sprites behave rather than how they look.</param>
  /// <param name="attributesOffset">Offset of the 32 four-byte sprite attributes.</param>
  /// <param name="patternsOffset">Offset of the sprite patterns.</param>
  /// <remarks>
  /// Two generations of sprite share these tables. On a TMS9918 a sprite has one colour and at most
  /// four may share a scanline; the V9938 gives each of a sprite's sixteen rows its own colour byte,
  /// held in a table 512 bytes below the attributes, allows eight per line, and adds a bit that lets
  /// overlapping sprites combine their colours instead of the nearer one simply winning. The limits
  /// are not decoration — a picture drawn on the hardware was composed against them, so a decoder
  /// that ignores them shows sprites the machine would have dropped.
  /// <para/>
  /// The attribute list ends early at a sentinel vertical position, which differs between the two
  /// generations because the V9938 screen is taller.
  /// </remarks>
  public static void OverlaySprites(
    ReadOnlySpan<byte> data, int attributesOffset, int patternsOffset, int mode,
    Span<byte> pixels, int width, int height) {
    var advanced = mode >= 4;
    var terminator = advanced ? 216 : 208;

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var color = 0;
      var combining = false;
      var remaining = advanced ? 8 : 4;

      for (var sprite = 0; sprite < 32; ++sprite) {
        var attribute = attributesOffset + (sprite << 2);
        if (attribute + 3 >= data.Length)
          break;

        var spriteY = data[attribute];
        if (spriteY == terminator)
          break;

        // A sprite's own top row is one below the stored position.
        var row = (y - spriteY - 1) & 255;
        if (row >= 16)
          continue;

        // The line's sprite budget is spent by every sprite that crosses it, drawn or not.
        if (--remaining < 0)
          break;

        var flags = advanced
          ? _At(data, attributesOffset - 512 + (sprite << 4) + row)
          : data[attribute + 3];

        if (!advanced || (flags & 64) == 0) {
          if (color != 0)
            break;

          combining = true;
        } else if (!combining)
          continue;

        var column = x - data[attribute + 1];
        // The early-clock bit shifts a sprite left so it can enter from off screen.
        if (flags >= 128)
          column += 32;
        if (column < 0 || column >= 16)
          continue;

        // A sixteen-wide sprite is four eight-by-eight patterns, the right half sixteen bytes on.
        var pattern = patternsOffset + ((data[attribute + 2] & 252) << 3) + row + ((column & 8) << 1);
        if (((_At(data, pattern) >> (~column & 7)) & 1) == 0)
          continue;

        color |= flags;
        // Marks the pixel as drawn even where the sprite's colour is zero.
        if (advanced)
          color |= 16;
      }

      if (color != 0)
        pixels[y * width + x] = (byte)(color & 15);
    }
  }

  private static byte _At(ReadOnlySpan<byte> data, int offset)
    => offset >= 0 && offset < data.Length ? data[offset] : (byte)0;

  /// <summary>Reads the nibble at an index, high half of each byte first.</summary>
  public static int GetNibble(ReadOnlySpan<byte> data, int offset, int index) {
    var position = offset + (index >> 1);
    if (position >= data.Length)
      return 0;

    return (index & 1) == 0 ? data[position] >> 4 : data[position] & 15;
  }

  /// <summary>
  /// The fixed 256 colours of Screen 8, as RGB triplets.
  /// </summary>
  /// <remarks>
  /// Screen 8 spends its byte per pixel on the colour directly rather than on a palette index, so
  /// there is nothing to store and nothing to choose: three bits of green, three of red and two of
  /// blue. Blue getting only two is why the four blue levels are 0, 2, 4 and 7 rather than an even
  /// ramp — the hardware picks values that keep greys grey.
  /// </remarks>
  public static byte[] Screen8Palette() {
    ReadOnlySpan<byte> blues = [0, 2, 4, 7];
    var palette = new byte[256 * 3];

    for (var c = 0; c < 256; ++c) {
      palette[c * 3] = _Expand3((c >> 2) & 7);
      palette[c * 3 + 1] = _Expand3((c >> 5) & 7);
      palette[c * 3 + 2] = _Expand3(blues[c & 3]);
    }

    return palette;
  }

  /// <summary>Pixels one YJK group spans; the group shares a single pair of chroma components.</summary>
  public const int YjkGroupSize = 4;

  /// <summary>
  /// Decodes one row of the V9958's YJK encoding into RGB triplets.
  /// </summary>
  /// <param name="row">The row's bytes, one per pixel.</param>
  /// <param name="width">Pixels in the row.</param>
  /// <param name="usePalette">
  /// Whether an odd luma marks a palette index rather than a colour — true in Screen 10, false in
  /// Screen 12. It costs a bit of luma resolution and buys sixteen exact colours.
  /// </param>
  /// <param name="paletteRgb">The sixteen palette colours as RGB triplets; unused when
  /// <paramref name="usePalette"/> is false.</param>
  /// <param name="rgb">Receives three bytes per pixel.</param>
  /// <remarks>
  /// Each byte carries five bits of luma and three bits of one chroma component. A group of four
  /// pixels pools its twelve chroma bits into two signed six-bit values shared by all four, which
  /// is why the format holds far more luma detail than colour detail. A group cut short by the end
  /// of the row has no chroma at all and decodes as grey.
  /// </remarks>
  public static void DecodeYjkRow(
    ReadOnlySpan<byte> row, int width, bool usePalette, ReadOnlySpan<byte> paletteRgb, Span<byte> rgb) {
    for (var x = 0; x < width; ++x) {
      var luma = row[x] >> 3;
      var target = x * 3;

      if (usePalette && (luma & 1) != 0) {
        var entry = (luma >> 1) * 3;
        rgb[target] = paletteRgb[entry];
        rgb[target + 1] = paletteRgb[entry + 1];
        rgb[target + 2] = paletteRgb[entry + 2];
        continue;
      }

      if ((x | (YjkGroupSize - 1)) >= width) {
        rgb[target] = rgb[target + 1] = rgb[target + 2] = _Expand5(luma);
        continue;
      }

      var group = x & ~(YjkGroupSize - 1);
      var k = _SignExtend6((row[group] & 7) | ((row[group + 1] & 7) << 3));
      var j = _SignExtend6((row[group + 2] & 7) | ((row[group + 3] & 7) << 3));

      rgb[target] = _Expand5(_Clamp5(luma + j));
      rgb[target + 1] = _Expand5(_Clamp5(luma + k));
      rgb[target + 2] = _Expand5(_Clamp5((5 * luma - 2 * j - k + 2) >> 2));
    }
  }

  /// <summary>Encodes one row of RGB triplets into the V9958's YJK encoding.</summary>
  /// <remarks>
  /// Luma is exact per pixel; the two chroma components are the group's average, because the
  /// hardware gives a group only one pair of them. Inverting the decoder's blue term gives
  /// <c>Y = (4B + 2R + G) / 8</c>, and the chroma are then simply how far red and green sit from
  /// that. In Screen 10 the luma's low bit is cleared, since an odd one would be read back as a
  /// palette index.
  /// </remarks>
  public static void EncodeYjkRow(ReadOnlySpan<byte> rgb, int width, bool usePalette, Span<byte> row) {
    Span<int> lumas = stackalloc int[YjkGroupSize];

    for (var start = 0; start < width; start += YjkGroupSize) {
      var count = Math.Min(YjkGroupSize, width - start);
      int sumJ = 0, sumK = 0;

      for (var i = 0; i < count; ++i) {
        var source = (start + i) * 3;
        int red = rgb[source] >> 3, green = rgb[source + 1] >> 3, blue = rgb[source + 2] >> 3;
        var luma = _Clamp5((4 * blue + 2 * red + green + 4) >> 3);
        lumas[i] = usePalette ? luma & ~1 : luma;
        sumJ += red - lumas[i];
        sumK += green - lumas[i];
      }

      var j = _Clamp6(_RoundedAverage(sumJ, count));
      var k = _Clamp6(_RoundedAverage(sumK, count));

      // A group cut short by the end of the row decodes as grey, so its chroma bits are moot.
      var chroma = count == YjkGroupSize ? (ReadOnlySpan<int>)[k & 7, (k >> 3) & 7, j & 7, (j >> 3) & 7] : default;
      for (var i = 0; i < count; ++i)
        row[start + i] = (byte)((lumas[i] << 3) | (chroma.IsEmpty ? 0 : chroma[i]));
    }
  }

  private static int _RoundedAverage(int sum, int count)
    => sum >= 0 ? (2 * sum + count) / (2 * count) : -((-2 * sum + count) / (2 * count));

  private static int _SignExtend6(int value) => value - ((value & 32) << 1);

  private static int _Clamp5(int value) => value < 0 ? 0 : value > 31 ? 31 : value;

  private static int _Clamp6(int value) => value < -32 ? -32 : value > 31 ? 31 : value;
}
