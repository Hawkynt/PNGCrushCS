using System;

namespace FileFormat.Core;

/// <summary>Primitives shared by the Amstrad CPC picture formats.</summary>
public static class AmstradGraphics {

  /// <summary>Colours the hardware can produce.</summary>
  public const int ColorCount = 32;

  /// <summary>The value a stored colour is biased by, so that it stays a printable byte.</summary>
  public const int ColorBias = 64;

  /// <summary>
  /// The thirty-two colours the Gate Array produces, as RGB triplets.
  /// </summary>
  /// <remarks>
  /// Each channel takes one of three levels rather than a range, so the palette is not a cube of
  /// 27 but a list of 32 with duplicates — two of the entries are the same grey, because the
  /// hardware's encoding has more room than its output does.
  /// </remarks>
  public static ReadOnlySpan<byte> Palette => [
    0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x00, 0xFF, 0x80, 0xFF, 0xFF, 0x80,
    0x00, 0x00, 0x80, 0xFF, 0x00, 0x80, 0x00, 0x80, 0x80, 0xFF, 0x80, 0x80,
    0xFF, 0x00, 0x80, 0xFF, 0xFF, 0x80, 0xFF, 0xFF, 0x00, 0xFF, 0xFF, 0xFF,
    0xFF, 0x00, 0x00, 0xFF, 0x00, 0xFF, 0xFF, 0x80, 0x00, 0xFF, 0x80, 0xFF,
    0x00, 0x00, 0x80, 0x00, 0xFF, 0x80, 0x00, 0xFF, 0x00, 0x00, 0xFF, 0xFF,
    0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x80, 0x00, 0x00, 0x80, 0xFF,
    0x80, 0x00, 0x80, 0x80, 0xFF, 0x80, 0x80, 0xFF, 0x00, 0x80, 0xFF, 0xFF,
    0x80, 0x00, 0x00, 0x80, 0x00, 0xFF, 0x80, 0x80, 0x00, 0x80, 0x80, 0xFF,
  ];

  /// <summary>
  /// Length of the AMSDOS header a file may carry, or zero when it carries none.
  /// </summary>
  /// <remarks>
  /// The header is 128 bytes with no signature of its own, so the only way to know it is there is
  /// that it describes the rest of the file and adds up: the stored length must match, be repeated
  /// where the second copy lives, and the first 67 bytes must sum to the checksum that follows.
  /// A file without one simply starts with its data, and the checksum is what keeps that from
  /// being mistaken for a header.
  /// </remarks>
  public static int HeaderLength(ReadOnlySpan<byte> data) {
    if (data.Length < 128 || (data[24] | (data[25] << 8)) != data.Length - 128
        || data[64] != data[24] || data[65] != data[25] || data[66] != 0)
      return 0;

    var sum = 0;
    for (var i = 0; i < 67; ++i)
      sum += data[i];

    return (data[67] | (data[68] << 8)) == sum ? 128 : 0;
  }

  /// <summary>
  /// The palette index a mode 1 pixel selects, given its byte already shifted into place.
  /// </summary>
  /// <remarks>
  /// Mode 1 spreads four pixels across a byte the same way mode 0 spreads two: a pixel's two bits
  /// sit four apart rather than side by side, so the Gate Array's shift-left-per-pixel brings both
  /// into the positions it reads without any rearranging.
  /// </remarks>
  public static int Mode1Index(int b) => ((b & 1) << 1) | ((b >> 4) & 1);

  /// <summary>
  /// The colours the firmware names, which are three levels a channel rather than a table.
  /// </summary>
  /// <remarks>
  /// The firmware numbers colours by counting rather than by hardware value: a number is a
  /// three-digit base-three figure whose digits are green, red and blue. That gives 27 of them, of
  /// which the hardware can show every one — the two duplicate entries in its own table are what
  /// the extra five hardware values cost.
  /// </remarks>
  public static bool TryFirmwareColor(int value, Span<byte> rgb) {
    if (value > 26)
      return false;

    ReadOnlySpan<byte> levels = [0, 128, 255];
    rgb[0] = levels[value / 3 % 3];
    rgb[1] = levels[value / 9];
    rgb[2] = levels[value % 3];

    return true;
  }

  /// <summary>
  /// The palette index a mode 0 pixel selects, given the byte holding it and which of the two it is.
  /// </summary>
  /// <remarks>
  /// Mode 0 puts two pixels in a byte and interleaves their bits rather than splitting the byte in
  /// half: a pixel's four bits are at positions 7, 3, 5 and 1, and the other pixel's at 6, 2, 4 and
  /// 0. The Gate Array shifts the byte left twice per pixel, so the bits it wants arrive together
  /// without any rearranging — the cost of that is paid once here instead.
  /// </remarks>
  public static int Mode0Index(int b, bool second) {
    if (!second)
      b >>= 1;

    return ((b & 1) << 3) | ((b >> 2) & 4) | ((b >> 1) & 2) | ((b >> 6) & 1);
  }

  /// <summary>Places two palette indices in one mode 0 byte, the inverse of reading them.</summary>
  /// <remarks>
  /// Derived from <see cref="Mode0Index"/> bit for bit rather than written again from the
  /// description: the interleaving has no pattern worth remembering, and the two halves only agree
  /// if one is the other read backwards.
  /// </remarks>
  public static byte Mode0Byte(int first, int second) {
    var b = 0;
    for (var bit = 0; bit < 4; ++bit) {
      // Where a bit of each index lands follows from where reading takes it from.
      if ((first & (1 << bit)) != 0)
        b |= 1 << _Mode0BitPosition(bit, false);

      if ((second & (1 << bit)) != 0)
        b |= 1 << _Mode0BitPosition(bit, true);
    }

    return (byte)b;
  }

  /// <summary>Which bit of the byte holds a given bit of a given one of the two pixels.</summary>
  private static int _Mode0BitPosition(int bit, bool second) {
    // Reading takes bit 3 from position 0, bit 2 from 4, bit 1 from 2 and bit 0 from 6, with the
    // first pixel's byte shifted right one first — so the first pixel sits one place higher.
    var position = bit switch {
      3 => 0,
      2 => 4,
      1 => 2,
      _ => 6,
    };

    return second ? position : position + 1;
  }

  /// <summary>The firmware colour nearest a given one.</summary>
  public static byte NearestFirmwareColor(int red, int green, int blue) {
    Span<byte> rgb = stackalloc byte[3];
    byte best = 0;
    var bestCost = int.MaxValue;

    for (var candidate = 0; candidate <= 26; ++candidate) {
      TryFirmwareColor(candidate, rgb);
      int dr = red - rgb[0], dg = green - rgb[1], db = blue - rgb[2];
      var cost = dr * dr + dg * dg + db * db;
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = (byte)candidate;
    }

    return best;
  }
}
