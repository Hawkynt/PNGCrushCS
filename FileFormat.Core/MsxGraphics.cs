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
