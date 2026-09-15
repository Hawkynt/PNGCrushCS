using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Codecs.Mve;

/// <summary>Decodes the RGB555 form of Interplay Video's sixteen 8x8 block encodings.</summary>
/// <remarks>
/// This follows FFmpeg's LGPL-2.1-or-later Interplay decoder for the true-colour rules absent from
/// the public 8-bit description. In particular, encodings 0x2..0x4 take their motion byte from a
/// second stream inside VIDEO_DATA, encoding 0x6 is a real signed-vector copy from two pictures back,
/// and 0xF is the true-colour spelling of an unchanged two-pictures-back block rather than the 8-bit
/// checkerboard mode. The implementation is independently shaped around spans and frame buffers; the
/// bitstream decisions and constants are format facts required for interoperability.
/// </remarks>
internal static class MveRgb555BlockDecoder {

  private const int _BLOCK = 8;

  internal static void Decode(
    ReadOnlySpan<byte> videoData,
    ReadOnlySpan<byte> decodingMap,
    MveRgb555Frame? last,
    MveRgb555Frame? secondLast,
    MveRgb555Frame target) {
    if (videoData.Length < 16)
      throw new InvalidDataException("A 16-bit Interplay VIDEO_DATA payload is too short for its fourteen-byte header and motion-stream offset.");

    var blocksAcross = target.Width / _BLOCK;
    var blocksDown = target.Height / _BLOCK;
    var blocks = checked(blocksAcross * blocksDown);
    if (decodingMap.Length < (blocks + 1) / 2)
      throw new InvalidDataException(
        $"The decoding map is {decodingMap.Length} bytes, short of the {(blocks + 1) / 2} a {blocksAcross}x{blocksDown}-block picture needs.");

    var motionOffset = BinaryPrimitives.ReadUInt16LittleEndian(videoData[14..]);
    var data = new _Reader(videoData, 16);
    var motionStart = 14 + motionOffset;
    if (motionStart > videoData.Length)
      throw new InvalidDataException($"The 16-bit Interplay motion stream starts at byte {motionStart}, outside a {videoData.Length}-byte VIDEO_DATA payload.");
    var motion = new _Reader(videoData, motionStart);

    for (var by = 0; by < blocksDown; ++by)
      for (var bx = 0; bx < blocksAcross; ++bx) {
        var index = by * blocksAcross + bx;
        var packed = decodingMap[index >> 1];
        var type = (index & 1) == 0 ? packed & 0x0F : packed >> 4;
        _DecodeBlock(type, ref data, ref motion, last, secondLast, target, bx * _BLOCK, by * _BLOCK);
      }
  }

  private static void _DecodeBlock(
    int type,
    ref _Reader data,
    ref _Reader motion,
    MveRgb555Frame? last,
    MveRgb555Frame? secondLast,
    MveRgb555Frame target,
    int x,
    int y) {
    switch (type) {
      case 0x0:
        _CopyRequired(last, target, x, y, 0, 0, "previous");
        return;
      case 0x1:
      case 0xF:
        _CopyRequired(secondLast, target, x, y, 0, 0, "second-previous");
        return;
      case 0x2: {
        var (dx, dy) = _FarVector(motion.Byte());
        _CopyRequired(secondLast, target, x, y, dx, dy, "second-previous");
        return;
      }
      case 0x3: {
        var (dx, dy) = _FarVector(motion.Byte());
        _Copy(target.Pixels, target.Pixels, target.Width, target.Height, x, y, -dx, -dy);
        return;
      }
      case 0x4: {
        var packed = motion.Byte();
        _CopyRequired(last, target, x, y, -8 + (packed & 0x0F), -8 + (packed >> 4), "previous");
        return;
      }
      case 0x5:
        _CopyRequired(last, target, x, y, unchecked((sbyte)data.Byte()), unchecked((sbyte)data.Byte()), "previous");
        return;
      case 0x6:
        _CopyRequired(secondLast, target, x, y, unchecked((sbyte)data.Byte()), unchecked((sbyte)data.Byte()), "second-previous");
        return;
      case 0x7:
        _TwoColour(ref data, target, x, y);
        return;
      case 0x8:
        _TwoColourSplit(ref data, target, x, y);
        return;
      case 0x9:
        _FourColour(ref data, target, x, y);
        return;
      case 0xA:
        _FourColourSplit(ref data, target, x, y);
        return;
      case 0xB:
        for (var row = 0; row < 8; ++row)
          for (var column = 0; column < 8; ++column)
            target.Pixels[(y + row) * target.Width + x + column] = data.Word();
        return;
      case 0xC:
        for (var cellY = 0; cellY < 4; ++cellY)
          for (var cellX = 0; cellX < 4; ++cellX)
            _Fill(target, x + cellX * 2, y + cellY * 2, 2, 2, data.Word());
        return;
      case 0xD:
        for (var cellY = 0; cellY < 2; ++cellY)
          for (var cellX = 0; cellX < 2; ++cellX)
            _Fill(target, x + cellX * 4, y + cellY * 4, 4, 4, data.Word());
        return;
      case 0xE:
        _Fill(target, x, y, 8, 8, data.Word());
        return;
      default:
        throw new InvalidDataException($"Interplay RGB555 block type 0x{type:X} is outside the four-bit decoding-map range.");
    }
  }

  private static void _TwoColour(ref _Reader data, MveRgb555Frame target, int x, int y) {
    var p0 = data.Word();
    var p1 = data.Word();
    if ((p0 & 0x8000) == 0) {
      for (var row = 0; row < 8; ++row) {
        var flags = data.Byte();
        for (var column = 0; column < 8; ++column)
          target.Pixels[(y + row) * target.Width + x + column] = ((flags >> column) & 1) == 0 ? p0 : p1;
      }
      return;
    }

    var cells = data.Word();
    for (var cellY = 0; cellY < 4; ++cellY)
      for (var cellX = 0; cellX < 4; ++cellX) {
        var bit = cellY * 4 + cellX;
        _Fill(target, x + cellX * 2, y + cellY * 2, 2, 2, ((cells >> bit) & 1) == 0 ? p0 : p1);
      }
  }

  private static void _TwoColourSplit(ref _Reader data, MveRgb555Frame target, int x, int y) {
    var p0 = data.Word();
    var p1 = data.Word();
    if ((p0 & 0x8000) == 0) {
      _TwoColourQuadrant(ref data, target, x, y, p0, p1);
      _TwoColourQuadrant(ref data, target, x, y + 4, data.Word(), data.Word());
      _TwoColourQuadrant(ref data, target, x + 4, y, data.Word(), data.Word());
      _TwoColourQuadrant(ref data, target, x + 4, y + 4, data.Word(), data.Word());
      return;
    }

    var firstFlags = data.UInt32();
    var p2 = data.Word();
    var p3 = data.Word();
    var secondFlags = data.UInt32();
    if ((p2 & 0x8000) == 0) {
      _PaintOneBit(target, x, y, 4, 8, p0, p1, firstFlags);
      _PaintOneBit(target, x + 4, y, 4, 8, p2, p3, secondFlags);
    } else {
      _PaintOneBit(target, x, y, 8, 4, p0, p1, firstFlags);
      _PaintOneBit(target, x, y + 4, 8, 4, p2, p3, secondFlags);
    }
  }

  private static void _TwoColourQuadrant(ref _Reader data, MveRgb555Frame target, int x, int y, ushort p0, ushort p1) {
    var flags = data.Word();
    _PaintOneBit(target, x, y, 4, 4, p0, p1, flags);
  }

  private static void _FourColour(ref _Reader data, MveRgb555Frame target, int x, int y) {
    Span<ushort> p = stackalloc ushort[4];
    for (var i = 0; i < 4; ++i)
      p[i] = data.Word();

    if ((p[0] & 0x8000) == 0) {
      if ((p[2] & 0x8000) == 0) {
        for (var row = 0; row < 8; ++row) {
          var flags = data.Word();
          for (var column = 0; column < 8; ++column)
            target.Pixels[(y + row) * target.Width + x + column] = p[(int)((flags >> (column * 2)) & 3)];
        }
      } else {
        var flags = data.UInt32();
        for (var cellY = 0; cellY < 4; ++cellY)
          for (var cellX = 0; cellX < 4; ++cellX) {
            var code = (int)((flags >> ((cellY * 4 + cellX) * 2)) & 3);
            _Fill(target, x + cellX * 2, y + cellY * 2, 2, 2, p[code]);
          }
      }
      return;
    }

    var packed = data.UInt64();
    if ((p[2] & 0x8000) == 0) {
      for (var row = 0; row < 8; ++row)
        for (var cellX = 0; cellX < 4; ++cellX) {
          var code = (int)(packed & 3);
          packed >>= 2;
          _Fill(target, x + cellX * 2, y + row, 2, 1, p[code]);
        }
    } else {
      for (var cellY = 0; cellY < 4; ++cellY)
        for (var column = 0; column < 8; ++column) {
          var code = (int)(packed & 3);
          packed >>= 2;
          _Fill(target, x + column, y + cellY * 2, 1, 2, p[code]);
        }
    }
  }

  private static void _FourColourSplit(ref _Reader data, MveRgb555Frame target, int x, int y) {
    Span<ushort> p = stackalloc ushort[8];
    for (var i = 0; i < 4; ++i)
      p[i] = data.Word();

    if ((p[0] & 0x8000) == 0) {
      _FourColourQuadrant(ref data, target, x, y, p[..4]);
      _ReadPalette(ref data, p[..4]);
      _FourColourQuadrant(ref data, target, x, y + 4, p[..4]);
      _ReadPalette(ref data, p[..4]);
      _FourColourQuadrant(ref data, target, x + 4, y, p[..4]);
      _ReadPalette(ref data, p[..4]);
      _FourColourQuadrant(ref data, target, x + 4, y + 4, p[..4]);
      return;
    }

    var firstFlags = data.UInt64();
    for (var i = 4; i < 8; ++i)
      p[i] = data.Word();
    var secondFlags = data.UInt64();
    var vertical = (p[4] & 0x8000) == 0;
    if (vertical) {
      _PaintTwoBit(target, x, y, 4, 8, p[..4], firstFlags);
      _PaintTwoBit(target, x + 4, y, 4, 8, p[4..], secondFlags);
    } else {
      _PaintTwoBit(target, x, y, 8, 4, p[..4], firstFlags);
      _PaintTwoBit(target, x, y + 4, 8, 4, p[4..], secondFlags);
    }
  }

  private static void _FourColourQuadrant(ref _Reader data, MveRgb555Frame target, int x, int y, ReadOnlySpan<ushort> palette) {
    var flags = data.UInt32();
    _PaintTwoBit(target, x, y, 4, 4, palette, flags);
  }

  private static void _ReadPalette(ref _Reader data, Span<ushort> palette) {
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = data.Word();
  }

  private static void _PaintOneBit(MveRgb555Frame target, int x, int y, int width, int height, ushort p0, ushort p1, ulong flags) {
    var bit = 0;
    for (var row = 0; row < height; ++row)
      for (var column = 0; column < width; ++column, ++bit)
        target.Pixels[(y + row) * target.Width + x + column] = ((flags >> bit) & 1) == 0 ? p0 : p1;
  }

  private static void _PaintTwoBit(MveRgb555Frame target, int x, int y, int width, int height, ReadOnlySpan<ushort> palette, ulong flags) {
    var shift = 0;
    for (var row = 0; row < height; ++row)
      for (var column = 0; column < width; ++column, shift += 2)
        target.Pixels[(y + row) * target.Width + x + column] = palette[(int)((flags >> shift) & 3)];
  }

  private static void _Fill(MveRgb555Frame target, int x, int y, int width, int height, ushort value) {
    for (var row = 0; row < height; ++row)
      target.Pixels.AsSpan((y + row) * target.Width + x, width).Fill(value);
  }

  private static (int X, int Y) _FarVector(byte value) {
    if (value < 56)
      return (8 + value % 7, value / 7);
    var adjusted = value - 56;
    return (-14 + adjusted % 29, 8 + adjusted / 29);
  }

  private static void _CopyRequired(MveRgb555Frame? source, MveRgb555Frame target, int x, int y, int dx, int dy, string name) {
    if (source == null)
      throw new InvalidDataException($"An Interplay block predicts from the {name} picture before that reference exists.");
    _Copy(source.Pixels, target.Pixels, target.Width, target.Height, x, y, dx, dy);
  }

  private static void _Copy(ushort[] source, ushort[] destination, int width, int height, int x, int y, int dx, int dy) {
    var sx = x + dx;
    var sy = y + dy;
    if (sx >= width) {
      sx -= width;
      ++sy;
    } else if (sx < 0) {
      sx += width;
      --sy;
    }

    if (sx < 0 || sy < 0 || sx + 8 > width || sy + 8 > height)
      throw new InvalidDataException(
        $"A motion-compensated Interplay block at ({x},{y}) points to ({sx},{sy}), outside the {width}x{height} picture.");

    for (var row = 0; row < 8; ++row)
      Array.Copy(source, (sy + row) * width + sx, destination, (y + row) * width + x, 8);
  }

  private ref struct _Reader {
    private readonly ReadOnlySpan<byte> _data;
    private int _at;

    internal _Reader(ReadOnlySpan<byte> data, int at) {
      this._data = data;
      this._at = at;
    }

    internal byte Byte() {
      if ((uint)this._at >= (uint)this._data.Length)
        throw new InvalidDataException("An Interplay RGB555 block runs past the end of VIDEO_DATA.");
      return this._data[this._at++];
    }

    internal ushort Word() {
      if (this._at > this._data.Length - 2)
        throw new InvalidDataException("An Interplay RGB555 block runs past the end of VIDEO_DATA.");
      var value = BinaryPrimitives.ReadUInt16LittleEndian(this._data[this._at..]);
      this._at += 2;
      return value;
    }

    internal uint UInt32() {
      if (this._at > this._data.Length - 4)
        throw new InvalidDataException("An Interplay RGB555 block runs past the end of VIDEO_DATA.");
      var value = BinaryPrimitives.ReadUInt32LittleEndian(this._data[this._at..]);
      this._at += 4;
      return value;
    }

    internal ulong UInt64() {
      if (this._at > this._data.Length - 8)
        throw new InvalidDataException("An Interplay RGB555 block runs past the end of VIDEO_DATA.");
      var value = BinaryPrimitives.ReadUInt64LittleEndian(this._data[this._at..]);
      this._at += 8;
      return value;
    }
  }
}
