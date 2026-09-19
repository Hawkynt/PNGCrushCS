using System;
using System.IO;

namespace FileFormat.Codecs.Roq;

internal static class RoqPictureDecoder {
  private const int _MB = 16;

  internal static void Decode(ReadOnlySpan<byte> data, RoqCodebook cb, int mx, int my, RoqFrame reference, RoqFrame target, int motionScale = 1) {
    var bits = new _CodeReader(data);
    for (var y = 0; y < target.Height; y += _MB)
    for (var x = 0; x < target.Width; x += _MB) {
      _Block8(ref bits, cb, mx, my, motionScale, reference, target, x, y);
      _Block8(ref bits, cb, mx, my, motionScale, reference, target, x + 8, y);
      _Block8(ref bits, cb, mx, my, motionScale, reference, target, x, y + 8);
      _Block8(ref bits, cb, mx, my, motionScale, reference, target, x + 8, y + 8);
    }
  }

  private static void _Block8(ref _CodeReader bits, RoqCodebook cb, int mx, int my, int scale, RoqFrame reference, RoqFrame target, int x, int y) {
    switch (bits.NextCode()) {
      case 0: return;
      case 1: _Motion(reference, target, x, y, 8, bits.NextByte(), mx, my, scale); return;
      case 2: _Paint4Upsampled(cb, target, x, y, _Cb4Index(cb, bits.NextByte())); return;
      default:
        _Block4(ref bits, cb, mx, my, scale, reference, target, x, y);
        _Block4(ref bits, cb, mx, my, scale, reference, target, x + 4, y);
        _Block4(ref bits, cb, mx, my, scale, reference, target, x, y + 4);
        _Block4(ref bits, cb, mx, my, scale, reference, target, x + 4, y + 4);
        return;
    }
  }

  private static void _Block4(ref _CodeReader bits, RoqCodebook cb, int mx, int my, int scale, RoqFrame reference, RoqFrame target, int x, int y) {
    switch (bits.NextCode()) {
      case 0: return;
      case 1: _Motion(reference, target, x, y, 4, bits.NextByte(), mx, my, scale); return;
      case 2: _Paint4(cb, target, x, y, _Cb4Index(cb, bits.NextByte())); return;
      default:
        _Paint2Checked(cb, target, x, y, bits.NextByte());
        _Paint2Checked(cb, target, x + 2, y, bits.NextByte());
        _Paint2Checked(cb, target, x, y + 2, bits.NextByte());
        _Paint2Checked(cb, target, x + 2, y + 2, bits.NextByte());
        return;
    }
  }

  private static int _Cb4Index(RoqCodebook cb, byte index) {
    if (index >= cb.Cb4Count)
      throw new InvalidDataException($"A RoQ SLD code names 4x4 cell {index}, but only {cb.Cb4Count} have been defined.");
    return index;
  }

  private static void _Motion(RoqFrame source, RoqFrame target, int x, int y, int size, byte arg, int mx, int my, int scale) {
    var dx = (mx + (arg >> 4) - 8) * scale;
    var dy = (my + (arg & 15) - 8) * scale;
    var sx = x - dx;
    var sy = y - dy;
    if (sx < 0 || sy < 0 || sx + size > source.Width || sy + size > source.Height)
      throw new InvalidDataException($"A RoQ motion block at ({x},{y}) points outside the {source.Width}x{source.Height} reference picture.");

    for (var row = 0; row < size; ++row) {
      var from = (sy + row) * source.Width + sx;
      var to = (y + row) * target.Width + x;
      Array.Copy(source.Y, from, target.Y, to, size);
      Array.Copy(source.Cb, from, target.Cb, to, size);
      Array.Copy(source.Cr, from, target.Cr, to, size);
      Array.Copy(source.A, from, target.A, to, size);
    }
  }

  private static void _Paint2Checked(RoqCodebook cb, RoqFrame target, int x, int y, byte index) {
    if (index >= cb.Cb2Count)
      throw new InvalidDataException($"A RoQ terminal code names 2x2 cell {index}, but only {cb.Cb2Count} have been defined.");
    _Paint2(cb, target, x, y, index);
  }

  private static void _Paint2(RoqCodebook cb, RoqFrame target, int x, int y, int index) {
    var cell = cb.Cb2(index);
    var p0 = y * target.Width + x;
    var p1 = p0 + target.Width;
    if (cb.HasAlpha) {
      target.Y[p0] = cell[0]; target.A[p0] = cell[1];
      target.Y[p0 + 1] = cell[2]; target.A[p0 + 1] = cell[3];
      target.Y[p1] = cell[4]; target.A[p1] = cell[5];
      target.Y[p1 + 1] = cell[6]; target.A[p1 + 1] = cell[7];
      _Chroma(target, p0, p1, cell[8], cell[9]);
    } else {
      target.Y[p0] = cell[0]; target.Y[p0 + 1] = cell[1];
      target.Y[p1] = cell[2]; target.Y[p1 + 1] = cell[3];
      target.A[p0] = target.A[p0 + 1] = target.A[p1] = target.A[p1 + 1] = 255;
      _Chroma(target, p0, p1, cell[4], cell[5]);
    }
  }

  private static void _Chroma(RoqFrame target, int p0, int p1, byte cb, byte cr) {
    target.Cb[p0] = target.Cb[p0 + 1] = target.Cb[p1] = target.Cb[p1 + 1] = cb;
    target.Cr[p0] = target.Cr[p0 + 1] = target.Cr[p1] = target.Cr[p1 + 1] = cr;
  }

  private static void _Paint4(RoqCodebook cb, RoqFrame target, int x, int y, int index) {
    var q = cb.Cb4(index);
    _Paint2Checked(cb, target, x, y, q[0]);
    _Paint2Checked(cb, target, x + 2, y, q[1]);
    _Paint2Checked(cb, target, x, y + 2, q[2]);
    _Paint2Checked(cb, target, x + 2, y + 2, q[3]);
  }

  private static void _Paint4Upsampled(RoqCodebook cb, RoqFrame target, int x, int y, int index) {
    var q = cb.Cb4(index);
    for (var quadrant = 0; quadrant < 4; ++quadrant) {
      var cellIndex = q[quadrant];
      if (cellIndex >= cb.Cb2Count)
        throw new InvalidDataException($"A RoQ 4x4 cell names 2x2 cell {cellIndex}, but only {cb.Cb2Count} have been defined.");
      var cell = cb.Cb2(cellIndex);
      var ox = (quadrant & 1) * 4;
      var oy = (quadrant >> 1) * 4;
      for (var sy = 0; sy < 2; ++sy)
      for (var sx = 0; sx < 2; ++sx) {
        var sample = sy * 2 + sx;
        var yy = cb.HasAlpha ? cell[sample * 2] : cell[sample];
        var aa = cb.HasAlpha ? cell[sample * 2 + 1] : (byte)255;
        var chromaAt = cb.HasAlpha ? 8 : 4;
        for (var dy = 0; dy < 2; ++dy) {
          var at = (y + oy + sy * 2 + dy) * target.Width + x + ox + sx * 2;
          target.Y[at] = target.Y[at + 1] = yy;
          target.A[at] = target.A[at + 1] = aa;
          target.Cb[at] = target.Cb[at + 1] = cell[chromaAt];
          target.Cr[at] = target.Cr[at + 1] = cell[chromaAt + 1];
        }
      }
    }
  }

  private ref struct _CodeReader {
    private readonly ReadOnlySpan<byte> _data;
    private int _position;
    private uint _word;
    private int _bitsLeft;
    internal _CodeReader(ReadOnlySpan<byte> data) { this._data = data; this._position = 0; }
    internal int NextCode() {
      if (this._bitsLeft == 0) {
        if (this._position + 2 > this._data.Length)
          throw new InvalidDataException("A RoQ QUAD_VQ code stream ends before all picture blocks are described.");
        this._word = (uint)(this._data[this._position] | (this._data[this._position + 1] << 8));
        this._position += 2;
        this._bitsLeft = 16;
      }
      this._bitsLeft -= 2;
      return (int)((this._word >> this._bitsLeft) & 3);
    }
    internal byte NextByte() {
      if (this._position >= this._data.Length)
        throw new InvalidDataException("A RoQ QUAD_VQ chunk ends where a block argument byte should be.");
      return this._data[this._position++];
    }
  }
}
