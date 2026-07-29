using System;

namespace FileFormat.Core;

/// <summary>Primitives shared by the Commodore 16 and Plus/4 picture formats.</summary>
/// <remarks>
/// The TED chip does not have a palette in the sense the VIC-II does. It has fifteen hues and eight
/// luminances, and a colour is the product of the two — 121 distinct colours, addressed as a
/// luminance in the high nibble and a hue in the low one, with every luminance of hue 0 being the
/// same black. Formats store the two halves in separate memory areas, which is why they are so
/// often mistaken for a palette plus an index.
/// </remarks>
public static class Commodore16Graphics {

  /// <summary>Entries in the luminance-by-hue table.</summary>
  public const int ColorCount = 128;

  /// <summary>Hues the TED offers, black included.</summary>
  public const int HueCount = 16;

  /// <summary>The colour a luminance and hue combine to, as an index into <see cref="HexColors"/>.</summary>
  public static int ColorIndex(int luminance, int hue) => ((luminance & 7) << 4) | (hue & 15);

  /// <summary>Every luminance-and-hue combination as a 0xRRGGBB value.</summary>
  /// <remarks>
  /// Measured from hardware rather than computed: the TED's luminance steps are not linear and its
  /// hues drift with brightness, so a generated table does not match a real machine. These are the
  /// values RECOIL uses, which come from the same measurements.
  /// </remarks>
  public static ReadOnlySpan<int> HexColors => [
    0x030303, 0x2F2F2F, 0x681010, 0x004242,
    0x58006D, 0x004E00, 0x191C94, 0x383800,
    0x562000, 0x4B2800, 0x164800, 0x69072F,
    0x004626, 0x062A80, 0x2A149B, 0x0B4900,
    0x030303, 0x3D3D3D, 0x751E20, 0x00504F,
    0x6A1078, 0x045C00, 0x2A2AA3, 0x4C4700,
    0x692F00, 0x593800, 0x265600, 0x751541,
    0x00583D, 0x153D8F, 0x3922AE, 0x195900,
    0x030303, 0x424242, 0x7B2820, 0x025659,
    0x6F1A82, 0x0A6509, 0x3034A7, 0x505100,
    0x6E3600, 0x654000, 0x2C5C00, 0x7D1E45,
    0x016145, 0x1C4599, 0x422DAD, 0x1D6200,
    0x030303, 0x56555A, 0x903C3B, 0x176D72,
    0x872D99, 0x1F7B15, 0x4649C1, 0x666300,
    0x844C0D, 0x735500, 0x407200, 0x91335E,
    0x19745C, 0x3259AE, 0x593FC3, 0x327600,
    0x030303, 0x847E85, 0xBB6768, 0x459696,
    0xAF58C3, 0x4AA73E, 0x7373EC, 0x928D11,
    0xAF7832, 0xA18020, 0x6C9E12, 0xBA5F89,
    0x469F83, 0x6185DD, 0x846CEF, 0x5DA329,
    0x030303, 0xB2ACB3, 0xE99292, 0x6CC3C1,
    0xD986F0, 0x79D176, 0x9DA1FF, 0xBDBE40,
    0xDCA261, 0xD1A94C, 0x93C83D, 0xE98AB1,
    0x6FCDAB, 0x8AB4FF, 0xB29AFF, 0x88CB59,
    0x030303, 0xCACACA, 0xFFACAC, 0x85D8E0,
    0xF39CFF, 0x92EA8A, 0xB7BAFF, 0xD6D35B,
    0xF3BE79, 0xE6C565, 0xB0E057, 0xFFA4CF,
    0x89E5C8, 0xA4CAFF, 0xC8B8FF, 0xA2E57A,
    0x030303, 0xF9F9F9, 0xFFF6F2, 0xD1FFFF,
    0xFFE9FF, 0xDBFFD3, 0xF0FFFF, 0xFFFFA3,
    0xFFFFC1, 0xFFFFB2, 0xFCFFA2, 0xFFEEFF,
    0xD1FFFF, 0xEBFFFF, 0xFFF8FF, 0xEDFFBC,
  ];

  /// <summary>The whole table as RGB triplets, ready for <see cref="RawImage.Palette"/>.</summary>
  public static byte[] CreatePalette() {
    var palette = new byte[ColorCount * 3];
    for (var i = 0; i < ColorCount; ++i) {
      var color = HexColors[i];
      palette[i * 3] = (byte)(color >> 16);
      palette[i * 3 + 1] = (byte)(color >> 8);
      palette[i * 3 + 2] = (byte)color;
    }

    return palette;
  }
}
