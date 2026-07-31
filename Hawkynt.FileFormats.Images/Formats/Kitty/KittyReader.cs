using System;
using System.IO;

namespace FileFormat.Kitty;

/// <summary>Reads Kitty pictures from bytes, streams, or file paths.</summary>
public static class KittyReader {

  /// <summary>Ends a list, and at the top level begins the fill that finishes the picture.</summary>
  private const int _END = 255;

  /// <summary>What a pixel holds before anything has been drawn over it.</summary>
  /// <remarks>
  /// Every channel of a real pixel is either off or full, so a value of one cannot be produced by
  /// any tile and can stand for "still blank". Only the first pixel of each tile is marked, which
  /// is all the fill needs to know.
  /// </remarks>
  private const int _BLANK = 1;

  public static KittyFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static KittyFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static KittyFile FromSpan(ReadOnlySpan<byte> data) {
    var pixels = new int[KittyFile.Width * KittyFile.Height];
    for (var i = 0; i < pixels.Length; i += KittyFile.TileWidth)
      pixels[i] = _BLANK;

    var mode = 0;
    var at = 0;

    while (at < data.Length) {
      var b = data[at++];
      if (b == _END)
        return _Fill(data, ref at, pixels, mode == 0 ? 0 : 2);

      mode = b;
      var tile = at;
      at += mode < 2 ? 3 : 6;

      _ReadRectangles(data, ref at, pixels, mode, tile);
      _ReadPositions(data, ref at, pixels, mode, tile);
    }

    throw new InvalidDataException("A Kitty picture ends before its blocks do.");
  }

  /// <summary>
  /// Reads the rectangles a block's tile fills. An entry is one, two or three bytes after its
  /// first: the top two bits say whether the rectangle is a column, a row, or has both extents.
  /// </summary>
  private static void _ReadRectangles(ReadOnlySpan<byte> data, ref int at, int[] pixels, int mode, int tile) {
    for (;;) {
      if (at >= data.Length)
        throw new InvalidDataException("A Kitty block's rectangles end before the block does.");

      int high = data[at++];
      if (high == _END)
        return;

      if (at + 2 > data.Length)
        throw new InvalidDataException("A Kitty rectangle has no extent.");

      // The starting corner is a tile number rather than a pair, so the two coordinates come out
      // of one fourteen-bit value.
      var offset = ((high & 63) << 8) | data[at++];
      var left = offset % KittyFile.Columns;
      var top = offset / KittyFile.Columns;
      if (top >= KittyFile.Rows)
        throw new InvalidDataException($"A Kitty rectangle starts on row {top}.");

      int right, bottom;
      if (high >= 128) {
        right = left;
        bottom = data[at++];
      } else {
        right = data[at++];
        if (right < left || right >= KittyFile.Columns)
          throw new InvalidDataException($"A Kitty rectangle ends at column {right}.");

        if (high >= 64)
          bottom = top;
        else {
          if (at >= data.Length)
            throw new InvalidDataException("A Kitty rectangle has no bottom.");

          bottom = data[at++];
          if (bottom < top || bottom >= KittyFile.Rows)
            throw new InvalidDataException($"A Kitty rectangle ends on row {bottom}.");
        }
      }

      for (var y = top; y <= bottom; ++y)
      for (var x = left; x <= right; ++x)
        _SetTile(pixels, x, y, mode, data, tile);
    }
  }

  /// <summary>Reads the single positions a block's tile fills, each an X and a Y.</summary>
  private static void _ReadPositions(ReadOnlySpan<byte> data, ref int at, int[] pixels, int mode, int tile) {
    for (;;) {
      if (at >= data.Length)
        throw new InvalidDataException("A Kitty block's positions end before the block does.");

      int x = data[at++];
      if (x == _END)
        return;

      if (x >= KittyFile.Columns || at >= data.Length)
        throw new InvalidDataException($"A Kitty position is at column {x}.");

      int y = data[at++];
      if (y >= KittyFile.Rows)
        throw new InvalidDataException($"A Kitty position is on row {y}.");

      _SetTile(pixels, x, y, mode, data, tile);
    }
  }

  /// <summary>
  /// Fills what is still blank, in scan order, from the bytes that follow — and stretches the
  /// short mode's two hundred rows to four hundred.
  /// </summary>
  private static KittyFile _Fill(ReadOnlySpan<byte> data, ref int at, int[] pixels, int mode) {
    var stride = mode == 0 ? KittyFile.Width / 2 : KittyFile.Width;
    var tileSize = mode == 0 ? 3 : 6;

    for (var y = 0; y < KittyFile.Rows; ++y)
    for (var x = 0; x < KittyFile.Columns; ++x) {
      var offset = (y * stride + x) << 2;
      if (pixels[offset] != _BLANK)
        continue;

      if (at + tileSize > data.Length)
        throw new InvalidDataException("A Kitty picture runs out of tiles before it is full.");

      _SetTileAt(pixels, offset, mode, data, at);
      at += tileSize;
    }

    if (at != data.Length)
      throw new InvalidDataException($"A Kitty picture has {data.Length - at} bytes left over.");

    if (mode == 0) {
      // The short mode draws two hundred rows and the display shows each of them twice.
      for (var offset = (KittyFile.Height / 2 - 1) * KittyFile.Width; offset >= 0; offset -= KittyFile.Width) {
        Array.Copy(pixels, offset, pixels, (offset << 1) + KittyFile.Width, KittyFile.Width);
        Array.Copy(pixels, offset, pixels, offset << 1, KittyFile.Width);
      }
    }

    var rgb = new byte[pixels.Length * 3];
    for (var i = 0; i < pixels.Length; ++i) {
      rgb[i * 3] = (byte)(pixels[i] >> 16);
      rgb[i * 3 + 1] = (byte)(pixels[i] >> 8);
      rgb[i * 3 + 2] = (byte)pixels[i];
    }

    return new() { Pixels = rgb };
  }

  private static void _SetTile(int[] pixels, int x, int y, int mode, ReadOnlySpan<byte> data, int tile) {
    // Beyond the short mode a tile is four rows rather than two, so its row number counts double.
    if (mode != 0)
      y <<= 1;

    _SetTileAt(pixels, (y * (KittyFile.Width / 2) + x) << 2, mode, data, tile);
  }

  private static void _SetTileAt(int[] pixels, int offset, int mode, ReadOnlySpan<byte> data, int tile) {
    var value = _Tile(data, tile);
    _SetFour(pixels, offset, value >> 4);
    _SetFour(pixels, offset + KittyFile.Width, value);

    if (mode == 0)
      return;

    // In the tall mode the lower half repeats the upper unless a second tile follows it.
    if (mode >= 2)
      value = _Tile(data, tile + 3);

    _SetFour(pixels, offset + KittyFile.Width * 2, value >> 4);
    _SetFour(pixels, offset + KittyFile.Width * 3, value);
  }

  /// <summary>
  /// Reads a tile: one bit a channel for each of eight pixels, the three bytes stored blue, red,
  /// green.
  /// </summary>
  private static int _Tile(ReadOnlySpan<byte> data, int offset)
    => offset + 2 < data.Length ? (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset] : 0;

  /// <summary>Writes four pixels, taking one bit of each channel from the same bit position.</summary>
  private static void _SetFour(int[] pixels, int offset, int tile) {
    for (var x = 0; x < KittyFile.TileWidth; ++x) {
      var target = offset + x;
      if (target >= 0 && target < pixels.Length)
        pixels[target] = ((tile >> (3 - x)) & 0x010101) * 255;
    }
  }

  public static KittyFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
