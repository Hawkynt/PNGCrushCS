using System;
using System.Collections.Generic;

namespace FileFormat.Kitty;

/// <summary>Assembles Kitty picture bytes from a <see cref="KittyFile"/>.</summary>
public static class KittyWriter {

  /// <summary>Ends a list, and at the top level begins the fill that finishes the picture.</summary>
  private const int _END = 255;

  /// <summary>
  /// Writes the picture as a single empty block followed by the fill.
  /// </summary>
  /// <remarks>
  /// The format's own compression is to name a tile once and then list everywhere it goes, which
  /// pays only for a drawing with large flat areas and costs more than it saves for anything else.
  /// What is written here instead is the fill that finishes every file anyway, with nothing left
  /// for it to skip.
  /// <para/>
  /// The empty block is not waste. The fill's tile size is decided by the last block's mode, and a
  /// file with no block at all falls back to the short mode, which has half the vertical
  /// resolution — so one block naming the tall mode is what buys the other two hundred rows.
  /// </remarks>
  public static byte[] ToBytes(KittyFile file) {
    var pixels = file.Pixels ?? [];
    var body = new List<byte> { 1, 0, 0, 0, _END, _END, _END };

    for (var y = 0; y < KittyFile.Rows; ++y)
    for (var x = 0; x < KittyFile.Columns; ++x) {
      // A tile is four pixels wide and four rows tall, written as two halves of two rows each.
      _Tile(body, pixels, x, y * 4);
      _Tile(body, pixels, x, y * 4 + 2);
    }

    return body.ToArray();
  }

  /// <summary>
  /// Writes one half-tile: four pixels across and two rows down, a bit a channel.
  /// </summary>
  /// <remarks>
  /// The three bytes are blue, red and green in that order, and each holds the upper row in its
  /// high nibble and the lower in its low one — so a byte is one channel of eight pixels rather
  /// than one pixel of three channels.
  /// </remarks>
  private static void _Tile(List<byte> body, ReadOnlySpan<byte> pixels, int column, int row) {
    int blue = 0, red = 0, green = 0;

    for (var r = 0; r < 2; ++r)
    for (var x = 0; x < KittyFile.TileWidth; ++x) {
      var at = ((row + r) * KittyFile.Width + column * KittyFile.TileWidth + x) * 3;
      if (at + 2 >= pixels.Length)
        continue;

      var bit = (3 - x) + (1 - r) * 4;

      // Anything past halfway is on, the machine having only one bit a channel to say it with.
      if (pixels[at] >= 128)
        red |= 1 << bit;

      if (pixels[at + 1] >= 128)
        green |= 1 << bit;

      if (pixels[at + 2] >= 128)
        blue |= 1 << bit;
    }

    body.Add((byte)blue);
    body.Add((byte)red);
    body.Add((byte)green);
  }
}
