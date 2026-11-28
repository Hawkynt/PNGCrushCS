using System;

namespace FileFormat.Codecs.Vp6;

/// <summary>
/// The three inverse transforms VP6 chooses between: VP3's full 8x8 IDCT, its ten-coefficient short
/// form, and a DC-only add.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/vp3dsp.c</c>, LGPL-2.1-or-later; see
/// <c>THIRD-PARTY-NOTICE.FFmpeg.txt</c> beside this file. The three are not interchangeable: the short
/// form reads only the top-left four rows, so it differs from the full transform whenever a
/// coefficient lies outside them, which VP6 bitstreams before sub-version 7 can produce at selector 10;
/// and the DC-only add rounds <c>(dc + 15) &gt;&gt; 5</c> where the full transform rounds to nearest.
/// Which one a block gets is part of the format, so each is carried exactly.
/// <para/>
/// Coefficients are in the transposed (column-major) layout VP3's transform expects. The destination
/// is an 8x8 block with a stride of eight.
/// </remarks>
internal static class Vp6InverseDct {

  private const int _C1S7 = 64277;
  private const int _C2S6 = 60547;
  private const int _C3S5 = 54491;
  private const int _C4S4 = 46341;
  private const int _C5S3 = 36410;
  private const int _C6S2 = 25080;
  private const int _C7S1 = 12785;

  private static int _M(int a, int b) => (int)unchecked((uint)a * (uint)b) >> 16;

  private static byte _Clip(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

  /// <summary>Writes the transform of <paramref name="block"/>, choosing the form <paramref name="selector"/> names.</summary>
  internal static void Put(Span<short> block, Span<byte> destination, int selector) {
    if (selector > 10 || selector == 1)
      _Full(block, destination, put: true);
    else
      _Ten(block, destination, put: true);
    block.Clear();
  }

  /// <summary>Adds the transform of <paramref name="block"/> to <paramref name="destination"/>.</summary>
  internal static void Add(Span<short> block, Span<byte> destination, int selector) {
    if (selector > 10)
      _Full(block, destination, put: false);
    else if (selector > 1)
      _Ten(block, destination, put: false);
    else
      _DcAdd(block, destination);
    block.Clear();
  }

  private static void _DcAdd(Span<short> block, Span<byte> destination) {
    var dc = (block[0] + 15) >> 5;
    for (var i = 0; i < 64; ++i)
      destination[i] = _Clip(destination[i] + dc);
  }

  private static void _Full(Span<short> ip, Span<byte> dst, bool put) {
    Span<int> t = stackalloc int[64];
    for (var i = 0; i < 64; ++i)
      t[i] = ip[i];

    for (var i = 0; i < 8; ++i) {
      if ((t[i] | t[8 + i] | t[16 + i] | t[24 + i] | t[32 + i] | t[40 + i] | t[48 + i] | t[56 + i]) == 0)
        continue;

      var a = _M(_C1S7, t[8 + i]) + _M(_C7S1, t[56 + i]);
      var b = _M(_C7S1, t[8 + i]) - _M(_C1S7, t[56 + i]);
      var c = _M(_C3S5, t[24 + i]) + _M(_C5S3, t[40 + i]);
      var d = _M(_C3S5, t[40 + i]) - _M(_C5S3, t[24 + i]);
      var ad = _M(_C4S4, a - c);
      var bd = _M(_C4S4, b - d);
      var cd = a + c;
      var dd = b + d;
      var e = _M(_C4S4, t[i] + t[32 + i]);
      var f = _M(_C4S4, t[i] - t[32 + i]);
      var g = _M(_C2S6, t[16 + i]) + _M(_C6S2, t[48 + i]);
      var h = _M(_C6S2, t[16 + i]) - _M(_C2S6, t[48 + i]);
      var ed = e - g;
      var gd = e + g;
      var add = f + ad;
      var bdd = bd - h;
      var fd = f - ad;
      var hd = bd + h;

      // The row pass writes back into 16-bit coefficients, as the reference does.
      t[i] = (short)(gd + cd);
      t[56 + i] = (short)(gd - cd);
      t[8 + i] = (short)(add + hd);
      t[16 + i] = (short)(add - hd);
      t[24 + i] = (short)(ed + dd);
      t[32 + i] = (short)(ed - dd);
      t[40 + i] = (short)(fd + bdd);
      t[48 + i] = (short)(fd - bdd);
    }

    for (var i = 0; i < 8; ++i) {
      var row = i * 8;
      if ((t[row + 1] | t[row + 2] | t[row + 3] | t[row + 4] | t[row + 5] | t[row + 6] | t[row + 7]) != 0) {
        var a = _M(_C1S7, t[row + 1]) + _M(_C7S1, t[row + 7]);
        var b = _M(_C7S1, t[row + 1]) - _M(_C1S7, t[row + 7]);
        var c = _M(_C3S5, t[row + 3]) + _M(_C5S3, t[row + 5]);
        var d = _M(_C3S5, t[row + 5]) - _M(_C5S3, t[row + 3]);
        var ad = _M(_C4S4, a - c);
        var bd = _M(_C4S4, b - d);
        var cd = a + c;
        var dd = b + d;
        var e = _M(_C4S4, t[row] + t[row + 4]) + 8;
        var f = _M(_C4S4, t[row] - t[row + 4]) + 8;
        if (put) {
          e += 16 * 128;
          f += 16 * 128;
        }
        var g = _M(_C2S6, t[row + 2]) + _M(_C6S2, t[row + 6]);
        var h = _M(_C6S2, t[row + 2]) - _M(_C2S6, t[row + 6]);
        var ed = e - g;
        var gd = e + g;
        var add = f + ad;
        var bdd = bd - h;
        var fd = f - ad;
        var hd = bd + h;

        _Out(dst, i, 0, (gd + cd) >> 4, put);
        _Out(dst, i, 7, (gd - cd) >> 4, put);
        _Out(dst, i, 1, (add + hd) >> 4, put);
        _Out(dst, i, 2, (add - hd) >> 4, put);
        _Out(dst, i, 3, (ed + dd) >> 4, put);
        _Out(dst, i, 4, (ed - dd) >> 4, put);
        _Out(dst, i, 5, (fd + bdd) >> 4, put);
        _Out(dst, i, 6, (fd - bdd) >> 4, put);
      } else if (put) {
        var v = _Clip(128 + ((_C4S4 * t[row] + (8 << 16)) >> 20));
        for (var k = 0; k < 8; ++k)
          dst[k * 8 + i] = v;
      } else if (t[row] != 0) {
        var v = (_C4S4 * t[row] + (8 << 16)) >> 20;
        for (var k = 0; k < 8; ++k)
          dst[k * 8 + i] = _Clip(dst[k * 8 + i] + v);
      }
    }
  }

  private static void _Ten(Span<short> ip, Span<byte> dst, bool put) {
    Span<int> t = stackalloc int[64];
    for (var i = 0; i < 64; ++i)
      t[i] = ip[i];

    for (var i = 0; i < 4; ++i) {
      if ((t[i] | t[8 + i] | t[16 + i] | t[24 + i]) == 0)
        continue;

      var a = _M(_C1S7, t[8 + i]);
      var b = _M(_C7S1, t[8 + i]);
      var c = _M(_C3S5, t[24 + i]);
      var d = -_M(_C5S3, t[24 + i]);
      var ad = _M(_C4S4, a - c);
      var bd = _M(_C4S4, b - d);
      var cd = a + c;
      var dd = b + d;
      var e = _M(_C4S4, t[i]);
      var f = e;
      var g = _M(_C2S6, t[16 + i]);
      var h = _M(_C6S2, t[16 + i]);
      var ed = e - g;
      var gd = e + g;
      var add = f + ad;
      var bdd = bd - h;
      var fd = f - ad;
      var hd = bd + h;

      t[i] = (short)(gd + cd);
      t[56 + i] = (short)(gd - cd);
      t[8 + i] = (short)(add + hd);
      t[16 + i] = (short)(add - hd);
      t[24 + i] = (short)(ed + dd);
      t[32 + i] = (short)(ed - dd);
      t[40 + i] = (short)(fd + bdd);
      t[48 + i] = (short)(fd - bdd);
    }

    for (var i = 0; i < 8; ++i) {
      var row = i * 8;
      if ((t[row] | t[row + 1] | t[row + 2] | t[row + 3]) != 0) {
        var a = _M(_C1S7, t[row + 1]);
        var b = _M(_C7S1, t[row + 1]);
        var c = _M(_C3S5, t[row + 3]);
        var d = -_M(_C5S3, t[row + 3]);
        var ad = _M(_C4S4, a - c);
        var bd = _M(_C4S4, b - d);
        var cd = a + c;
        var dd = b + d;
        var e = _M(_C4S4, t[row]);
        if (put)
          e += 16 * 128;
        var f = e;
        var g = _M(_C2S6, t[row + 2]);
        var h = _M(_C6S2, t[row + 2]);
        var ed = e - g + 8;
        var gd = e + g + 8;
        var add = f + ad + 8;
        var bdd = bd - h;
        var fd = f - ad + 8;
        var hd = bd + h;

        _Out(dst, i, 0, (gd + cd) >> 4, put);
        _Out(dst, i, 7, (gd - cd) >> 4, put);
        _Out(dst, i, 1, (add + hd) >> 4, put);
        _Out(dst, i, 2, (add - hd) >> 4, put);
        _Out(dst, i, 3, (ed + dd) >> 4, put);
        _Out(dst, i, 4, (ed - dd) >> 4, put);
        _Out(dst, i, 5, (fd + bdd) >> 4, put);
        _Out(dst, i, 6, (fd - bdd) >> 4, put);
      } else if (put) {
        for (var k = 0; k < 8; ++k)
          dst[k * 8 + i] = 128;
      }
    }
  }

  // The column pass walks i across the eight columns of the output and writes down each one.
  private static void _Out(Span<byte> dst, int column, int row, int value, bool put) {
    var at = row * 8 + column;
    dst[at] = put ? _Clip(value) : _Clip(dst[at] + value);
  }
}
