using System;

namespace FileFormat.Codecs.Vp4;

/// <summary>
/// The sample-level operations VP4 shares with VP3: the inverse transform in its three forms, the
/// loop filter, its bounding table, and the averaging half-sample predictions.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>libavcodec/vp3dsp.c</c> (LGPL-2.1-or-later). The transform is held to
/// FFmpeg's C code step for step: sixteen-bit intermediates, the 16.16 multiplies and the order the
/// two passes run in, because a VP3-family transform is defined by the reference code rather than by
/// a tolerance. Every routine takes a plane, an offset and a stride, the stride running in the
/// direction the codec counts block rows — up the picture, VP4 being stored bottom row first.
/// </remarks>
internal static class Vp4Dsp {

  private const int _C1S7 = 64277;
  private const int _C2S6 = 60547;
  private const int _C3S5 = 54491;
  private const int _C4S4 = 46341;
  private const int _C5S3 = 36410;
  private const int _C6S2 = 25080;
  private const int _C7S1 = 12785;

  private static int _M(int a, int b) => unchecked((int)((uint)a * (uint)b)) >> 16;

  /// <summary><c>vp3_idct_put_c</c> (<paramref name="add"/> false) or <c>vp3_idct_add_c</c>: the block is consumed.</summary>
  internal static void Idct(byte[] plane, int offset, int stride, short[] block, bool add) {
    unchecked {
      for (var i = 0; i < 8; ++i) {
        if ((block[i] | block[8 + i] | block[16 + i] | block[24 + i] | block[32 + i] | block[40 + i] | block[48 + i] | block[56 + i]) != 0) {
          var a = _M(_C1S7, block[8 + i]) + _M(_C7S1, block[56 + i]);
          var b = _M(_C7S1, block[8 + i]) - _M(_C1S7, block[56 + i]);
          var c = _M(_C3S5, block[24 + i]) + _M(_C5S3, block[40 + i]);
          var d = _M(_C3S5, block[40 + i]) - _M(_C5S3, block[24 + i]);
          var ad = _M(_C4S4, a - c);
          var bd = _M(_C4S4, b - d);
          var cd = a + c;
          var dd = b + d;
          var e = _M(_C4S4, block[i] + block[32 + i]);
          var f = _M(_C4S4, block[i] - block[32 + i]);
          var g = _M(_C2S6, block[16 + i]) + _M(_C6S2, block[48 + i]);
          var h = _M(_C6S2, block[16 + i]) - _M(_C2S6, block[48 + i]);
          var ed = e - g;
          var gd = e + g;
          var add2 = f + ad;
          var bdd = bd - h;
          var fd = f - ad;
          var hd = bd + h;
          block[i] = (short)(gd + cd);
          block[56 + i] = (short)(gd - cd);
          block[8 + i] = (short)(add2 + hd);
          block[16 + i] = (short)(add2 - hd);
          block[24 + i] = (short)(ed + dd);
          block[32 + i] = (short)(ed - dd);
          block[40 + i] = (short)(fd + bdd);
          block[48 + i] = (short)(fd - bdd);
        }
      }

      for (var i = 0; i < 8; ++i) {
        var ip = i * 8;
        var dst = offset + i;
        if ((block[ip + 1] | block[ip + 2] | block[ip + 3] | block[ip + 4] | block[ip + 5] | block[ip + 6] | block[ip + 7]) != 0) {
          var a = _M(_C1S7, block[ip + 1]) + _M(_C7S1, block[ip + 7]);
          var b = _M(_C7S1, block[ip + 1]) - _M(_C1S7, block[ip + 7]);
          var c = _M(_C3S5, block[ip + 3]) + _M(_C5S3, block[ip + 5]);
          var d = _M(_C3S5, block[ip + 5]) - _M(_C5S3, block[ip + 3]);
          var ad = _M(_C4S4, a - c);
          var bd = _M(_C4S4, b - d);
          var cd = a + c;
          var dd = b + d;
          var e = _M(_C4S4, block[ip] + block[ip + 4]) + 8;
          var f = _M(_C4S4, block[ip] - block[ip + 4]) + 8;
          if (!add) {
            e += 16 * 128;
            f += 16 * 128;
          }

          var g = _M(_C2S6, block[ip + 2]) + _M(_C6S2, block[ip + 6]);
          var h = _M(_C6S2, block[ip + 2]) - _M(_C2S6, block[ip + 6]);
          var ed = e - g;
          var gd = e + g;
          var add2 = f + ad;
          var bdd = bd - h;
          var fd = f - ad;
          var hd = bd + h;

          _Out(plane, dst, add, (gd + cd) >> 4);
          _Out(plane, dst + 7 * stride, add, (gd - cd) >> 4);
          _Out(plane, dst + stride, add, (add2 + hd) >> 4);
          _Out(plane, dst + 2 * stride, add, (add2 - hd) >> 4);
          _Out(plane, dst + 3 * stride, add, (ed + dd) >> 4);
          _Out(plane, dst + 4 * stride, add, (ed - dd) >> 4);
          _Out(plane, dst + 5 * stride, add, (fd + bdd) >> 4);
          _Out(plane, dst + 6 * stride, add, (fd - bdd) >> 4);
        } else if (!add) {
          var v = _Clip(128 + ((_C4S4 * block[ip] + (8 << 16)) >> 20));
          for (var k = 0; k < 8; ++k)
            plane[dst + k * stride] = v;
        } else if (block[ip] != 0) {
          var v = (_C4S4 * block[ip] + (8 << 16)) >> 20;
          for (var k = 0; k < 8; ++k)
            plane[dst + k * stride] = _Clip(plane[dst + k * stride] + v);
        }
      }
    }

    Array.Clear(block);
  }

  /// <summary><c>vp3_idct_dc_add_c</c>.</summary>
  internal static void IdctDcAdd(byte[] plane, int offset, int stride, short[] block) {
    var dc = (block[0] + 15) >> 5;
    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x)
        plane[offset + y * stride + x] = _Clip(plane[offset + y * stride + x] + dc);
    block[0] = 0;
  }

  /// <summary><c>ff_vp3dsp_set_bounding_values</c>: the table indexed from -127 to 128, stored with 127 added.</summary>
  internal static int[] BoundingValues(int filterLimit) {
    var values = new int[256 + 4];
    const int zero = 127;
    for (var x = 0; x < filterLimit; ++x) {
      values[zero - x] = -x;
      values[zero + x] = x;
    }

    var value = filterLimit;
    for (var x = filterLimit; x < 128 && value != 0; ++x, --value) {
      values[zero + x] = value;
      values[zero - x] = -value;
    }

    if (value != 0)
      values[zero + 128] = value;
    return values;
  }

  /// <summary><c>vp3_v_loop_filter_c</c>: <paramref name="count"/> columns across the edge between rows -1 and 0.</summary>
  internal static void VerticalLoopFilter(byte[] plane, int first, int stride, int[] bounding, int count) {
    for (var x = 0; x < count; ++x) {
      var p = first + x;
      var value = plane[p - 2 * stride] - plane[p + stride] + (plane[p] - plane[p - stride]) * 3;
      value = bounding[127 + ((value + 4) >> 3)];
      plane[p - stride] = _Clip(plane[p - stride] + value);
      plane[p] = _Clip(plane[p] - value);
    }
  }

  /// <summary><c>vp3_h_loop_filter_c</c>: <paramref name="count"/> rows across the edge between columns -1 and 0.</summary>
  internal static void HorizontalLoopFilter(byte[] plane, int first, int stride, int[] bounding, int count) {
    for (var y = 0; y < count; ++y) {
      var p = first + y * stride;
      var value = plane[p - 2] - plane[p + 1] + (plane[p] - plane[p - 1]) * 3;
      value = bounding[127 + ((value + 4) >> 3)];
      plane[p - 1] = _Clip(plane[p - 1] + value);
      plane[p] = _Clip(plane[p] - value);
    }
  }

  /// <summary>
  /// The no-rounding half-sample predictions of an 8x8 block from a source window whose stride is
  /// <paramref name="sourceStride"/>: <c>put_no_rnd_pixels_tab[1]</c> for indices 0 to 2 and VP3's
  /// diagonal <c>put_no_rnd_pixels_l2</c> for index 3, whose second source is offset by <paramref name="diagonal"/>.
  /// </summary>
  internal static void Predict(
    byte[] source, int origin, int sourceStride, int halfIndex, int diagonal,
    byte[] plane, int target, int stride) {
    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x) {
        var s = origin + y * sourceStride + x;
        plane[target + y * stride + x] = halfIndex switch {
          0 => source[s],
          1 => (byte)((source[s] + source[s + 1]) >> 1),
          2 => (byte)((source[s] + source[s + sourceStride]) >> 1),
          _ => (byte)((source[s - diagonal] + source[s + sourceStride + 1 + diagonal]) >> 1),
        };
      }
  }

  private static void _Out(byte[] plane, int at, bool add, int value)
    => plane[at] = _Clip(add ? plane[at] + value : value);

  private static byte _Clip(int value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;
}
