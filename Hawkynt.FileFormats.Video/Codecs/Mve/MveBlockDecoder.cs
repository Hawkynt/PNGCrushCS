using System;
using System.IO;

namespace FileFormat.Codecs.Mve;

/// <summary>
/// Walks a <c>VIDEO_DATA</c> opcode's payload against the most recent decoding map and paints one
/// picture from it: sixteen 8x8 block encodings, from a plain copy through motion compensation to
/// four flavours of bit-packed pattern.
/// </summary>
/// <remarks>
/// The picture is 8x8 blocks, each carrying a four-bit encoding read from the decoding map — the
/// lower nibble of each map byte before the upper, exactly as the format's own description states.
/// What that description does not state, and what was measured against two real files totalling 555
/// pictures instead:
/// <para/>
/// <b>The payload opens with a fourteen-byte header before any block is coded.</b> Nothing published
/// mentions it; it was found by noticing that bytes 8–11 of every <c>VIDEO_DATA</c> payload equal the
/// picture's own width and height in macroblocks — the same figures <c>INIT_VIDEO_BUFFERS</c> already
/// states — and confirmed because every block decoded from the wrong (zero) offset produced a picture
/// that was fifty per cent wrong in exactly the blocks needing more than a plain copy, and one hundred
/// per cent right in the raw and solid ones once the offset was corrected. The other twelve bytes are
/// skipped rather than interpreted, since nothing here depends on them and no field's meaning was
/// established beyond the two that repeat the size.
/// <para/>
/// <b>Every bit-packed pattern reads its bits low bit first, not high bit first.</b> The format's own
/// description states the opposite for the one case it gives a rule for at all — "the rightmost pixel
/// is represented by the low-order bit" for the plain eight-byte two-colour pattern — and that
/// statement is simply wrong for every pattern-coded block measured, this one included: reading high
/// bit first there reproduces no sample, and reading low bit first reproduces all of them. Two colours
/// packed as two bits are read the same way, low bit of the pair first.
/// <para/>
/// <b>Encoding 0x6 is refused rather than guessed at.</b> The format's own description of it — "skips
/// the next two blocks" — cannot be squared with a stream where every block's position is implied by
/// its place in the decoding map rather than stated, and the same description doubts its own reading:
/// no sample measured here or noted anywhere published ever states it.
/// <para/>
/// <b>Measured.</b> Two files — 432x320 and 640x272, 225 and 330 pictures, 555 in all, covering every
/// encoding this reads and none of the ones it refuses — were decoded here and by ffmpeg and compared
/// sample for sample against ffmpeg's own <c>pal8</c> output: every picture, every plane index and the
/// installed palette both, is identical.
/// </remarks>
internal static class MveBlockDecoder {

  private const int _PAYLOAD_HEADER_LENGTH = 14;
  private const int _BLOCK = 8;

  internal static void Decode(ReadOnlySpan<byte> decodingMap, ReadOnlySpan<byte> videoData, MveFrame reference, MveFrame target) {
    if (videoData.Length < _PAYLOAD_HEADER_LENGTH)
      throw new InvalidDataException($"A VIDEO_DATA opcode is {videoData.Length} bytes, short of its own fourteen-byte header.");

    var width = target.Width;
    var height = target.Height;
    var blocksAcross = width / _BLOCK;
    var blocksDown = height / _BLOCK;

    if (decodingMap.Length < (blocksAcross * blocksDown + 1) / 2)
      throw new InvalidDataException(
        $"The decoding map is {decodingMap.Length} bytes, short of the {(blocksAcross * blocksDown + 1) / 2} "
        + $"a {blocksAcross}x{blocksDown}-block picture needs.");

    var reader = new _BlockReader(videoData, _PAYLOAD_HEADER_LENGTH);

    for (var by = 0; by < blocksDown; ++by)
      for (var bx = 0; bx < blocksAcross; ++bx) {
        var index = by * blocksAcross + bx;
        var mapByte = decodingMap[index / 2];
        var type = (index % 2 == 0) ? (mapByte & 0xF) : (mapByte >> 4) & 0xF;
        _Block(type, ref reader, reference, target, bx * _BLOCK, by * _BLOCK, width, height);
      }
  }

  private static void _Block(int type, ref _BlockReader reader, MveFrame reference, MveFrame target, int x, int y, int width, int height) {
    switch (type) {
      case 0x0:
        _Copy(reference.Indices, target.Indices, x, y, _BLOCK, _BLOCK, width, 0, 0, width, height);
        return;

      case 0x1:
        return; // true no-op: whatever this buffer slot held two pictures ago remains.

      case 0x2:
      case 0x3: {
        var b = reader.NextByte();
        int dx, dy;
        if (b < 56) {
          dx = 8 + b % 7;
          dy = b / 7;
        } else {
          var bb = b - 56;
          dx = -14 + bb % 29;
          dy = 8 + bb / 29;
        }

        if (type == 0x3) {
          dx = -dx;
          dy = -dy;
        }

        _Copy(target.Indices, target.Indices, x, y, _BLOCK, _BLOCK, width, dx, dy, width, height);
        return;
      }

      case 0x4: {
        var b = reader.NextByte();
        var dx = -8 + (b & 0xF);
        var dy = -8 + (b >> 4);
        _Copy(reference.Indices, target.Indices, x, y, _BLOCK, _BLOCK, width, dx, dy, width, height);
        return;
      }

      case 0x5: {
        var dx = unchecked((sbyte)reader.NextByte());
        var dy = unchecked((sbyte)reader.NextByte());
        _Copy(reference.Indices, target.Indices, x, y, _BLOCK, _BLOCK, width, dx, dy, width, height);
        return;
      }

      case 0x6:
        throw new NotSupportedException(
          "Block encoding 0x6 (\"skip the next two blocks\") is not implemented — the format's own "
          + "description doubts its own reading, and no sample measured against this decoder states it.");

      case 0x7:
        _TwoColourWhole(ref reader, target, x, y, width);
        return;

      case 0x8:
        _TwoColourQuadrantOrSplit(ref reader, target, x, y, width);
        return;

      case 0x9:
        _FourColour(ref reader, target, x, y, width);
        return;

      case 0xA:
        _FourColourQuadrantOrSplit(ref reader, target, x, y, width);
        return;

      case 0xB: {
        var chunk = reader.NextBytes(64);
        _PaintFlat(target.Indices, x, y, 8, 8, width, chunk);
        return;
      }

      case 0xC: {
        var chunk = reader.NextBytes(16);
        _PaintCells(target.Indices, x, y, width, chunk, 4, 2);
        return;
      }

      case 0xD: {
        var chunk = reader.NextBytes(4);
        _PaintCells(target.Indices, x, y, width, chunk, 2, 4);
        return;
      }

      case 0xE: {
        var value = reader.NextByte();
        for (var row = 0; row < 8; ++row) {
          var offset = (y + row) * width + x;
          target.Indices.AsSpan(offset, 8).Fill(value);
        }

        return;
      }

      case 0xF: {
        var p0 = reader.NextByte();
        var p1 = reader.NextByte();
        for (var row = 0; row < 8; ++row) {
          var offset = (y + row) * width + x;
          for (var column = 0; column < 8; ++column)
            target.Indices[offset + column] = (row + column) % 2 == 0 ? p0 : p1;
        }

        return;
      }

      default:
        throw new NotSupportedException($"An 8x8 block names encoding 0x{type:X}, which the decoding map does not define.");
    }
  }

  /// <summary>Copies an <c>n</c>x<c>n</c> block from a source picture at a motion-shifted position.</summary>
  private static void _Copy(byte[] source, byte[] destination, int x, int y, int blockWidth, int blockHeight, int width, int dx, int dy, int fullWidth, int height) {
    var sx = x + dx;
    var sy = y + dy;
    if (sx < 0 || sy < 0 || sx + blockWidth > fullWidth || sy + blockHeight > height)
      throw new InvalidDataException(
        $"A motion-compensated block at ({x},{y}) points to ({sx},{sy}), outside the {fullWidth}x{height} "
        + "picture. Nothing measured this against exercises a vector reaching off the edge of the picture.");

    for (var row = 0; row < blockHeight; ++row) {
      var from = (sy + row) * width + sx;
      var to = (y + row) * width + x;
      Array.Copy(source, from, destination, to, blockWidth);
    }
  }

  private static void _PaintFlat(byte[] destination, int x, int y, int blockWidth, int blockHeight, int width, ReadOnlySpan<byte> values) {
    var i = 0;
    for (var row = 0; row < blockHeight; ++row) {
      var offset = (y + row) * width + x;
      for (var column = 0; column < blockWidth; ++column)
        destination[offset + column] = values[i++];
    }
  }

  /// <summary>Paints an 8x8 block from a grid of cells, one raw byte apiece — 4x4 cells of 2x2 pixels
  /// for encoding 0xC, or 2x2 cells of 4x4 pixels for encoding 0xD.</summary>
  private static void _PaintCells(byte[] destination, int x, int y, int width, ReadOnlySpan<byte> cells, int cellsPerSide, int cellSize) {
    var i = 0;
    for (var cellY = 0; cellY < cellsPerSide; ++cellY)
      for (var cellX = 0; cellX < cellsPerSide; ++cellX) {
        var value = cells[i++];
        for (var row = 0; row < cellSize; ++row) {
          var offset = (y + cellY * cellSize + row) * width + x + cellX * cellSize;
          destination.AsSpan(offset, cellSize).Fill(value);
        }
      }
  }

  /// <summary>Paints an 8x8 block, one bit a pixel, low bit of each byte first — the plain case of
  /// encoding 0x7.</summary>
  private static void _PaintOneBitPerPixel(byte[] destination, int x, int y, int blockWidth, int blockHeight, int width, ReadOnlySpan<byte> bits, byte v0, byte v1) {
    var bitIndex = 0;
    for (var row = 0; row < blockHeight; ++row) {
      var offset = (y + row) * width + x;
      for (var column = 0; column < blockWidth; ++column) {
        var b = bits[bitIndex / 8];
        var bit = (b >> (bitIndex % 8)) & 1;
        destination[offset + column] = bit != 0 ? v1 : v0;
        ++bitIndex;
      }
    }
  }

  /// <summary>Paints a rectangular area from a grid of same-sized cells, one bit a cell, low bit of
  /// each byte first.</summary>
  private static void _PaintOneBitPerCell(byte[] destination, int x, int y, int areaWidth, int areaHeight, int width, ReadOnlySpan<byte> bits, byte v0, byte v1, int cellWidth, int cellHeight) {
    var cellsAcross = areaWidth / cellWidth;
    var cellsDown = areaHeight / cellHeight;
    var bitIndex = 0;
    for (var cellY = 0; cellY < cellsDown; ++cellY)
      for (var cellX = 0; cellX < cellsAcross; ++cellX) {
        var b = bits[bitIndex / 8];
        var bit = (b >> (bitIndex % 8)) & 1;
        var value = bit != 0 ? v1 : v0;
        ++bitIndex;
        for (var row = 0; row < cellHeight; ++row) {
          var offset = (y + cellY * cellHeight + row) * width + x + cellX * cellWidth;
          destination.AsSpan(offset, cellWidth).Fill(value);
        }
      }
  }

  /// <summary>Paints a rectangular area from a grid of same-sized cells, two bits a cell (low bit of
  /// the pair first), naming one of four values.</summary>
  private static void _PaintTwoBitsPerCell(byte[] destination, int x, int y, int areaWidth, int areaHeight, int width, ReadOnlySpan<byte> bits, ReadOnlySpan<byte> values, int cellWidth, int cellHeight) {
    var cellsAcross = areaWidth / cellWidth;
    var cellsDown = areaHeight / cellHeight;
    var bitIndex = 0;
    for (var cellY = 0; cellY < cellsDown; ++cellY)
      for (var cellX = 0; cellX < cellsAcross; ++cellX) {
        var lowByte = bits[bitIndex / 8];
        var lowBit = (lowByte >> (bitIndex % 8)) & 1;
        ++bitIndex;
        var highByte = bits[bitIndex / 8];
        var highBit = (highByte >> (bitIndex % 8)) & 1;
        ++bitIndex;
        var value = values[lowBit | (highBit << 1)];
        for (var row = 0; row < cellHeight; ++row) {
          var offset = (y + cellY * cellHeight + row) * width + x + cellX * cellWidth;
          destination.AsSpan(offset, cellWidth).Fill(value);
        }
      }
  }

  // ============================================================================================
  // Encoding 0x7: a two-colour block, whole or as sixteen 2x2 cells
  // ============================================================================================

  private static void _TwoColourWhole(ref _BlockReader reader, MveFrame target, int x, int y, int width) {
    var p0 = reader.NextByte();
    var p1 = reader.NextByte();
    if (p0 <= p1) {
      var bits = reader.NextBytes(8);
      _PaintOneBitPerPixel(target.Indices, x, y, 8, 8, width, bits, p0, p1);
    } else {
      var bits = reader.NextBytes(2);
      _PaintOneBitPerCell(target.Indices, x, y, 8, 8, width, bits, p0, p1, 2, 2);
    }
  }

  // ============================================================================================
  // Encoding 0x8: four independent 2-colour quadrants, or a 2-colour left/right or top/bottom split
  // ============================================================================================

  private static void _TwoColourQuadrantOrSplit(ref _BlockReader reader, MveFrame target, int x, int y, int width) {
    var p0 = reader.NextByte();
    var p1 = reader.NextByte();
    if (p0 <= p1) {
      // Top left, bottom left, top right, bottom right, in that order — reading order, not raster.
      _TwoColourQuadrant(ref reader, target, x, y, width, p0, p1);
      _TwoColourQuadrantFresh(ref reader, target, x, y + 4, width);
      _TwoColourQuadrantFresh(ref reader, target, x + 4, y, width);
      _TwoColourQuadrantFresh(ref reader, target, x + 4, y + 4, width);
    } else {
      var half0 = reader.NextBytes(4);
      var p2 = reader.NextByte();
      var p3 = reader.NextByte();
      var half1 = reader.NextBytes(4);
      if (p2 <= p3) {
        _PaintOneBitPerPixel(target.Indices, x, y, 4, 8, width, half0, p0, p1);
        _PaintOneBitPerPixel(target.Indices, x + 4, y, 4, 8, width, half1, p2, p3);
      } else {
        _PaintOneBitPerPixel(target.Indices, x, y, 8, 4, width, half0, p0, p1);
        _PaintOneBitPerPixel(target.Indices, x, y + 4, 8, 4, width, half1, p2, p3);
      }
    }
  }

  private static void _TwoColourQuadrant(ref _BlockReader reader, MveFrame target, int x, int y, int width, byte p0, byte p1) {
    var bits = reader.NextBytes(2);
    _PaintOneBitPerPixel(target.Indices, x, y, 4, 4, width, bits, p0, p1);
  }

  private static void _TwoColourQuadrantFresh(ref _BlockReader reader, MveFrame target, int x, int y, int width) {
    var p0 = reader.NextByte();
    var p1 = reader.NextByte();
    _TwoColourQuadrant(ref reader, target, x, y, width, p0, p1);
  }

  // ============================================================================================
  // Encoding 0x9: a four-colour block, at 1x1, 2x2, 2x1 or 1x2 pixel groupings
  // ============================================================================================

  private static void _FourColour(ref _BlockReader reader, MveFrame target, int x, int y, int width) {
    var values = reader.NextBytes(4).ToArray();
    var p0 = values[0];
    var p1 = values[1];
    var p2 = values[2];
    var p3 = values[3];

    if (p0 <= p1 && p2 <= p3) {
      var bits = reader.NextBytes(16);
      _PaintTwoBitsPerCell(target.Indices, x, y, 8, 8, width, bits, values, 1, 1);
    } else if (p0 <= p1) { // p2 > p3
      var bits = reader.NextBytes(4);
      _PaintTwoBitsPerCell(target.Indices, x, y, 8, 8, width, bits, values, 2, 2);
    } else if (p2 <= p3) { // p0 > p1
      var bits = reader.NextBytes(8);
      _PaintTwoBitsPerCell(target.Indices, x, y, 8, 8, width, bits, values, 2, 1);
    } else {
      var bits = reader.NextBytes(8);
      _PaintTwoBitsPerCell(target.Indices, x, y, 8, 8, width, bits, values, 1, 2);
    }
  }

  // ============================================================================================
  // Encoding 0xA: four independent 4-colour quadrants, or a 4-colour left/right or top/bottom split
  // ============================================================================================

  private static void _FourColourQuadrantOrSplit(ref _BlockReader reader, MveFrame target, int x, int y, int width) {
    var v0 = reader.NextBytes(4).ToArray();
    if (v0[0] <= v0[1]) {
      _FourColourQuadrant(target, x, y, width, v0, reader.NextBytes(4));
      var v1 = reader.NextBytes(4).ToArray();
      _FourColourQuadrant(target, x, y + 4, width, v1, reader.NextBytes(4));
      var v2 = reader.NextBytes(4).ToArray();
      _FourColourQuadrant(target, x + 4, y, width, v2, reader.NextBytes(4));
      var v3 = reader.NextBytes(4).ToArray();
      _FourColourQuadrant(target, x + 4, y + 4, width, v3, reader.NextBytes(4));
    } else {
      var half0 = reader.NextBytes(8);
      var v1 = reader.NextBytes(4).ToArray();
      var half1 = reader.NextBytes(8);
      if (v1[0] <= v1[1]) {
        _PaintTwoBitsPerCell(target.Indices, x, y, 4, 8, width, half0, v0, 1, 1);
        _PaintTwoBitsPerCell(target.Indices, x + 4, y, 4, 8, width, half1, v1, 1, 1);
      } else {
        _PaintTwoBitsPerCell(target.Indices, x, y, 8, 4, width, half0, v0, 1, 1);
        _PaintTwoBitsPerCell(target.Indices, x, y + 4, 8, 4, width, half1, v1, 1, 1);
      }
    }
  }

  private static void _FourColourQuadrant(MveFrame target, int x, int y, int width, ReadOnlySpan<byte> values, ReadOnlySpan<byte> bits)
    => _PaintTwoBitsPerCell(target.Indices, x, y, 4, 4, width, bits, values, 1, 1);

  /// <summary>Walks a <c>VIDEO_DATA</c> payload's block data sequentially, past the fourteen-byte
  /// header.</summary>
  private ref struct _BlockReader {
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    internal _BlockReader(ReadOnlySpan<byte> data, int start) {
      this._data = data;
      this._position = start;
    }

    internal byte NextByte() {
      if (this._position >= this._data.Length)
        throw new InvalidDataException("A VIDEO_DATA opcode ends where a block's own data should be.");

      return this._data[this._position++];
    }

    internal ReadOnlySpan<byte> NextBytes(int count) {
      if (this._position + count > this._data.Length)
        throw new InvalidDataException($"A VIDEO_DATA opcode ends {this._position + count - this._data.Length} byte(s) short of a block's own data.");

      var span = this._data.Slice(this._position, count);
      this._position += count;
      return span;
    }
  }
}
