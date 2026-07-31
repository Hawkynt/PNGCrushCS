using System;

namespace FileFormat.Core;

/// <summary>Primitives shared by the VIC-20 picture formats.</summary>
public static class Vic20Graphics {

  /// <summary>Colours the VIC-I can show.</summary>
  public const int ColorCount = 16;

  /// <summary>Colours a character's foreground may take; the upper half is for the background only.</summary>
  public const int ForegroundColorCount = 8;

  /// <summary>
  /// The sixteen colours as RGB triplets, measured from hardware rather than idealised.
  /// </summary>
  /// <remarks>
  /// The VIC-I generates its colours as composite video directly, so nothing about them is
  /// saturated or neutral and no two machines agree exactly. These are the values the reference
  /// decoder uses, so our output and it agree exactly rather than approximately. Only the first
  /// eight can be a character's foreground — the chip spends the eighth bit of the colour nibble on
  /// something else — which is why a file naming a higher one for ink is malformed rather than
  /// merely unusual.
  /// </remarks>
  public static ReadOnlySpan<byte> Palette => [
    0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x6D, 0x23, 0x27, 0xA0, 0xFE, 0xF8,
    0x8E, 0x3C, 0x97, 0x7E, 0xDA, 0x75, 0x25, 0x23, 0x90, 0xFF, 0xFF, 0x86,
    0xA4, 0x64, 0x3B, 0xFF, 0xC8, 0xA1, 0xF2, 0xA7, 0xAB, 0xDB, 0xFF, 0xFF,
    0xFF, 0xB4, 0xFF, 0xD7, 0xFF, 0xCE, 0x9D, 0x9A, 0xFF, 0xFF, 0xFF, 0xC9,
  ];

  /// <summary>The palette as RGB triplets, ready for <see cref="RawImage.Palette"/>.</summary>
  public static byte[] CreatePalette() => Palette.ToArray();
}
