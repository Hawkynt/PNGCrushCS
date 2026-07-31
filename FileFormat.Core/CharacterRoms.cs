using System;
using System.IO;
using System.Reflection;

namespace FileFormat.Core;

/// <summary>The character generator ROMs a few formats cannot be read without.</summary>
/// <remarks>
/// Several formats store nothing but character codes — a screen of a machine's built-in font, saved
/// as the machine held it — so the font is not something the file names but something the reader
/// has to already know. There is no way to decode such a file without it, and no way to derive it:
/// it is a table of shapes somebody drew.
/// </remarks>
public static class CharacterRoms {

  private static byte[]? _atari8;
  private static byte[]? _zx81;

  /// <summary>
  /// The Atari 8-bit character set: 128 characters of eight bytes, one bit a pixel.
  /// </summary>
  /// <remarks>
  /// A character code's high bit is not an index into a second half of the set but an instruction
  /// to invert what the low seven bits select, which is why 1024 bytes cover 256 codes.
  /// </remarks>
  public static ReadOnlySpan<byte> Atari8 => _atari8 ??= _Load("atari8.fnt", 1024);

  /// <summary>The ZX81 character set: 64 characters of eight bytes, one bit a pixel.</summary>
  /// <remarks>
  /// The machine has no lower case and no true graphics mode; its "graphics" are the block
  /// characters in this set, and its high bit inverts as the Atari's does.
  /// </remarks>
  public static ReadOnlySpan<byte> Zx81 => _zx81 ??= _Load("zx81.fnt", 512);

  private static byte[] _Load(string name, int size) {
    using var stream = typeof(CharacterRoms).GetTypeInfo().Assembly
                         .GetManifestResourceStream($"FileFormat.Core.Roms.{name}")
                       ?? throw new InvalidOperationException($"The {name} character set is missing from the assembly.");

    var data = new byte[size];
    stream.ReadExactly(data);

    return data;
  }

  /// <summary>
  /// Draws a screen of Atari 8-bit character codes into a frame of GTIA colour bytes.
  /// </summary>
  /// <param name="font">The character set, which need not be the ROM one.</param>
  /// <param name="stride">Character codes per row of the screen.</param>
  /// <remarks>
  /// The two colours are not free: the text mode takes its hue from PF2 and its luminance for lit
  /// pixels from PF1, so the foreground is always the background's hue at another brightness. That
  /// is why text on the machine is a shade of one colour rather than two colours.
  /// </remarks>
  public static void DecodeGraphics0(
    ReadOnlySpan<byte> characters, int charactersOffset, int stride,
    ReadOnlySpan<byte> font, Span<byte> frame, int width, int height,
    byte background = 0, byte foregroundLuminance = 14) {
    var colors = new[] { background, (byte)((background & 240) | (foregroundLuminance & 14)) };

    for (var y = 0; y < height; ++y) {
      var row = charactersOffset + (y >> 3) * stride;

      for (var x = 0; x < width; ++x) {
        var at = row + (x >> 3);
        var character = at >= 0 && at < characters.Length ? characters[at] : 0;

        var shape = ((character & 127) << 3) + (y & 7);
        var bits = shape < font.Length ? font[shape] : 0;

        // The high bit of a code inverts the character rather than selecting another one.
        var lit = ((bits >> (~x & 7)) ^ (character >> 7)) & 1;
        frame[y * width + x] = colors[lit];
      }
    }
  }
}
