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
}
