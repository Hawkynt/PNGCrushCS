using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace FileFormat.Mrf;

/// <summary>Writes a Monochrome Recursive Format picture: thirteen bytes of header, then the quadtree.</summary>
/// <remarks>
/// The inverse of the read. The canvas is rounded up to whole tiles of sixty-four, the tiles are
/// coded in reading order, and a square that is one colour throughout spends a bit saying so and a
/// second giving the colour rather than being split. A square of one pixel spends only the colour
/// bit, because there is nothing it could be split into.
/// <para/>
/// The padding outside the picture is filled with the pixel nearest it rather than with black. It is
/// never decoded — the reader crops to the stated size — but a border of black would break up the
/// squares that straddle the edge and cost bits for a region nobody sees.
/// </remarks>
public static class MrfWriter {

  /// <summary>The same bound the reader puts on a stated size.</summary>
  private const int _MaxDimension = 1 << 16;

  public static byte[] ToBytes(MrfFile file) {
    var width = file.Width;
    var height = file.Height;
    if (width is < 1 or > _MaxDimension || height is < 1 or > _MaxDimension)
      throw new ArgumentException($"An MRF picture states its size in an unsigned long but is coded in tiles of {MrfFile.TileSize}; {width} by {height} is outside the {_MaxDimension} this writes.", nameof(file));

    var pixels = file.PixelData ?? new byte[width * height];
    if (pixels.Length < width * height)
      throw new ArgumentException($"An MRF picture of {width} by {height} needs {width * height} bytes and has {pixels.Length}.", nameof(file));

    var tilesAcross = (width + MrfFile.TileSize - 1) / MrfFile.TileSize;
    var tilesDown = (height + MrfFile.TileSize - 1) / MrfFile.TileSize;
    var paddedWidth = tilesAcross * MrfFile.TileSize;
    var paddedHeight = tilesDown * MrfFile.TileSize;

    var canvas = new byte[paddedWidth * paddedHeight];
    for (var y = 0; y < paddedHeight; ++y) {
      var source = Math.Min(y, height - 1) * width;
      var row = y * paddedWidth;
      for (var x = 0; x < paddedWidth; ++x)
        canvas[row + x] = (byte)(pixels[source + Math.Min(x, width - 1)] != 0 ? 1 : 0);
    }

    var bits = new _BitWriter();
    for (var tileY = 0; tileY < tilesDown; ++tileY)
      for (var tileX = 0; tileX < tilesAcross; ++tileX)
        _WriteSquare(bits, canvas, paddedWidth, tileX * MrfFile.TileSize, tileY * MrfFile.TileSize, MrfFile.TileSize);

    var body = bits.ToArray();
    var result = new byte[MrfFile.HeaderSize + body.Length];
    MrfFile.Magic.CopyTo(result);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), (uint)width);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8), (uint)height);
    result[12] = 0;
    body.CopyTo(result, MrfFile.HeaderSize);

    return result;
  }

  /// <summary>Codes one square, splitting it only when it holds both colours.</summary>
  private static void _WriteSquare(_BitWriter bits, byte[] canvas, int stride, int left, int top, int size) {
    if (size == 1) {
      bits.WriteBit(canvas[top * stride + left]);
      return;
    }

    if (_IsUniform(canvas, stride, left, top, size, out var colour)) {
      bits.WriteBit(1);
      bits.WriteBit(colour);
      return;
    }

    bits.WriteBit(0);
    var half = size >> 1;
    _WriteSquare(bits, canvas, stride, left, top, half);
    _WriteSquare(bits, canvas, stride, left + half, top, half);
    _WriteSquare(bits, canvas, stride, left, top + half, half);
    _WriteSquare(bits, canvas, stride, left + half, top + half, half);
  }

  private static bool _IsUniform(byte[] canvas, int stride, int left, int top, int size, out int colour) {
    colour = canvas[top * stride + left];
    for (var y = 0; y < size; ++y) {
      var row = (top + y) * stride + left;
      for (var x = 0; x < size; ++x)
        if (canvas[row + x] != colour)
          return false;
    }

    return true;
  }

  /// <summary>Collects the bit stream, most significant bit of a byte first.</summary>
  private sealed class _BitWriter {

    private readonly List<byte> _bytes = [];
    private int _current;
    private int _bit = 7;

    public void WriteBit(int value) {
      if (value != 0)
        this._current |= 1 << this._bit;

      if (--this._bit >= 0)
        return;

      this._bytes.Add((byte)this._current);
      this._current = 0;
      this._bit = 7;
    }

    /// <summary>The stream with its last byte finished off, since a file is whole bytes.</summary>
    /// <remarks>
    /// The reader stops when the picture is complete rather than at the end of the file, so the bits
    /// spent rounding out the last byte are never looked at. There is always at least one byte: the
    /// smallest picture is a single tile, which costs two bits at the least.
    /// </remarks>
    public byte[] ToArray() {
      if (this._bit != 7)
        this._bytes.Add((byte)this._current);

      return [.. this._bytes];
    }
  }
}
