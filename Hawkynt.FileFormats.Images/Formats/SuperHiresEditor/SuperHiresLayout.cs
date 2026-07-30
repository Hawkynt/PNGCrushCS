using System;

namespace FileFormat.SuperHiresEditor;

/// <summary>Addressing and unpacking shared by the two Super-hires Editors.</summary>
/// <remarks>
/// Both editors get past the VIC-II's colour-per-cell limit the same way: they lay hardware sprites
/// over a bitmap. A sprite carries its own colour and is not tied to the character grid, so wherever
/// one covers a cell it overrides whatever the cell could otherwise show.
/// </remarks>
public static class SuperHiresLayout {

  /// <summary>Scanlines one sprite spans.</summary>
  public const int SpriteHeight = 21;

  /// <summary>Screen pixels one sprite spans.</summary>
  public const int SpriteWidth = 24;

  /// <summary>Bytes one sprite occupies, padded to a 64-byte boundary.</summary>
  public const int SpriteStride = 64;

  /// <summary>
  /// Where a sprite's bit for a given pixel lives, when sprites are stored as the hardware wants
  /// them.
  /// </summary>
  /// <remarks>
  /// A sprite is three bytes a row and twenty-one rows, padded to sixty-four so the hardware can
  /// address it by a single byte. Sprites are laid out in a grid, and <paramref name="rowShift"/>
  /// says how many of them fit across — as a shift, because it is always a power of two.
  /// </remarks>
  public static int SpriteOffset(int x, int y, int rowShift)
    => ((((y / SpriteHeight) << rowShift) + x / SpriteWidth) << 6)
      + y % SpriteHeight * 3
      + (x >> 3) % 3;

  /// <summary>
  /// Where a sprite's bit lives when the file stores them column by column instead.
  /// </summary>
  public static int ColumnSpriteOffset(int x, int y, int height) => (x >> 3) * height + y;

  /// <summary>
  /// Unpacks the run-length encoding both editors use for their compressed files.
  /// </summary>
  /// <remarks>
  /// The first byte chooses the escape. Every other byte stands for itself unless it is that escape,
  /// in which case a count and a value follow. Choosing the escape per file rather than fixing it
  /// means a picture full of one byte value can still pick a different escape and lose nothing.
  /// </remarks>
  public static byte[]? TryUnpack(ReadOnlySpan<byte> data, int unpackedLength) {
    if (data.Length < 3)
      return null;

    var unpacked = new byte[unpackedLength];
    var escape = data[0];
    var source = 1;

    for (var target = 0; target < unpackedLength;) {
      if (source >= data.Length)
        return null;

      int value = data[source++];
      var count = 1;
      if (value == escape) {
        if (source + 1 >= data.Length)
          return null;

        count = data[source++];
        value = data[source++];
      }

      while (count-- > 0 && target < unpackedLength)
        unpacked[target++] = (byte)value;
    }

    return unpacked;
  }
}
