using System;

namespace FileFormat.Core;

/// <summary>Primitives shared by the ZX81 picture formats.</summary>
/// <remarks>
/// The machine has no bitmap display at all. What passes for graphics is a screen of character
/// codes, sixty-four of which are shapes and the rest the same shapes inverted, so a picture is
/// only ever as detailed as those shapes allow — a quarter-block here, a half there. Every ZX81
/// format is therefore a screen of codes and nothing else.
/// </remarks>
public static class Zx81Graphics {

  /// <summary>Pixels across.</summary>
  public const int Width = 256;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>Character cells across.</summary>
  public const int Columns = Width / 8;

  /// <summary>Character cell rows.</summary>
  public const int Rows = Height / 8;

  /// <summary>Character codes a screen holds.</summary>
  public const int ScreenSize = Columns * Rows;

  /// <summary>Draws a screen of character codes, one bit a pixel.</summary>
  /// <remarks>
  /// A code's high bit inverts its shape, and the low six select it — the two bits between are not
  /// used, the set having only sixty-four characters in it.
  /// </remarks>
  public static byte[] Decode(ReadOnlySpan<byte> screen) {
    var font = CharacterRoms.Zx81;
    var pixels = new byte[Width * Height];

    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var at = ((y >> 3) << 5) | (x >> 3);
      var character = at < screen.Length ? screen[at] : 0;
      var bits = font[((character & 63) << 3) | (y & 7)];

      // A set bit and an inverted character both mean paper; either alone means ink.
      pixels[y * Width + x] = (byte)(((bits >> (~x & 7)) & 1) == character >> 7 ? 0 : 1);
    }

    return pixels;
  }

  /// <summary>The two colours a ZX81 picture has: white paper and black ink.</summary>
  public static byte[] CreatePalette() => [255, 255, 255, 0, 0, 0];
}
