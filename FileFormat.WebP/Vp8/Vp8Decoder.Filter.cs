using System;

namespace FileFormat.WebP.Vp8;

// Port of filter.go: simple and normal in-loop filters (§15).
internal sealed partial class Vp8Decoder {

  private static int _Abs(int x) => x < 0 ? -x : x;

  private static int _Clamp15(int x) => x < -16 ? -16 : x > 15 ? 15 : x;

  private static int _Clamp127(int x) => x < -128 ? -128 : x > 127 ? 127 : x;

  private static byte _Clamp255(int x) => x < 0 ? (byte)0 : x > 255 ? (byte)255 : (byte)x;

  /// <summary>Filters a 2-pixel-wide or -high band along an edge (simple filter).</summary>
  private static void _Filter2(byte[] pix, int level, int index, int iStep, int jStep) {
    for (var n = 16; n > 0; --n, index += iStep) {
      int p1 = pix[index - 2 * jStep];
      int p0 = pix[index - 1 * jStep];
      int q0 = pix[index + 0 * jStep];
      int q1 = pix[index + 1 * jStep];
      if ((_Abs(p0 - q0) << 1) + (_Abs(p1 - q1) >> 1) > level) continue;
      var a = 3 * (q0 - p0) + _Clamp127(p1 - q1);
      var a1 = _Clamp15(a + 4 >> 3);
      var a2 = _Clamp15(a + 3 >> 3);
      pix[index - 1 * jStep] = _Clamp255(p0 + a2);
      pix[index + 0 * jStep] = _Clamp255(q0 - a1);
    }
  }

  /// <summary>Filters a 2-, 4- or 6-pixel band along an edge (normal filter).</summary>
  private static void _Filter246(byte[] pix, int n, int level, int ilevel, int hlevel, int index, int iStep, int jStep, bool fourNotSix) {
    for (; n > 0; --n, index += iStep) {
      int p3 = pix[index - 4 * jStep];
      int p2 = pix[index - 3 * jStep];
      int p1 = pix[index - 2 * jStep];
      int p0 = pix[index - 1 * jStep];
      int q0 = pix[index + 0 * jStep];
      int q1 = pix[index + 1 * jStep];
      int q2 = pix[index + 2 * jStep];
      int q3 = pix[index + 3 * jStep];
      if ((_Abs(p0 - q0) << 1) + (_Abs(p1 - q1) >> 1) > level) continue;
      if (_Abs(p3 - p2) > ilevel ||
          _Abs(p2 - p1) > ilevel ||
          _Abs(p1 - p0) > ilevel ||
          _Abs(q1 - q0) > ilevel ||
          _Abs(q2 - q1) > ilevel ||
          _Abs(q3 - q2) > ilevel)
        continue;
      if (_Abs(p1 - p0) > hlevel || _Abs(q1 - q0) > hlevel) {
        // Filter 2 pixels.
        var a = 3 * (q0 - p0) + _Clamp127(p1 - q1);
        var a1 = _Clamp15(a + 4 >> 3);
        var a2 = _Clamp15(a + 3 >> 3);
        pix[index - 1 * jStep] = _Clamp255(p0 + a2);
        pix[index + 0 * jStep] = _Clamp255(q0 - a1);
      } else if (fourNotSix) {
        // Filter 4 pixels.
        var a = 3 * (q0 - p0);
        var a1 = _Clamp15(a + 4 >> 3);
        var a2 = _Clamp15(a + 3 >> 3);
        var a3 = a1 + 1 >> 1;
        pix[index - 2 * jStep] = _Clamp255(p1 + a3);
        pix[index - 1 * jStep] = _Clamp255(p0 + a2);
        pix[index + 0 * jStep] = _Clamp255(q0 - a1);
        pix[index + 1 * jStep] = _Clamp255(q1 - a3);
      } else {
        // Filter 6 pixels.
        var a = _Clamp127(3 * (q0 - p0) + _Clamp127(p1 - q1));
        var a1 = 27 * a + 63 >> 7;
        var a2 = 18 * a + 63 >> 7;
        var a3 = 9 * a + 63 >> 7;
        pix[index - 3 * jStep] = _Clamp255(p2 + a3);
        pix[index - 2 * jStep] = _Clamp255(p1 + a2);
        pix[index - 1 * jStep] = _Clamp255(p0 + a1);
        pix[index + 0 * jStep] = _Clamp255(q0 - a1);
        pix[index + 1 * jStep] = _Clamp255(q1 - a2);
        pix[index + 2 * jStep] = _Clamp255(q2 - a3);
      }
    }
  }

  private void _SimpleFilter() {
    for (var mby = 0; mby < _mbh; ++mby) {
      for (var mbx = 0; mbx < _mbw; ++mbx) {
        var f = _perMBFilterParams[_mbw * mby + mbx];
        if (f.Level == 0) continue;
        int l = f.Level;
        var yIndex = (mby * _yStride + mbx) * 16;
        if (mbx > 0) _Filter2(_yPlane, l + 4, yIndex, _yStride, 1);
        if (f.Inner) {
          _Filter2(_yPlane, l, yIndex + 0x4, _yStride, 1);
          _Filter2(_yPlane, l, yIndex + 0x8, _yStride, 1);
          _Filter2(_yPlane, l, yIndex + 0xc, _yStride, 1);
        }
        if (mby > 0) _Filter2(_yPlane, l + 4, yIndex, 1, _yStride);
        if (f.Inner) {
          _Filter2(_yPlane, l, yIndex + _yStride * 0x4, 1, _yStride);
          _Filter2(_yPlane, l, yIndex + _yStride * 0x8, 1, _yStride);
          _Filter2(_yPlane, l, yIndex + _yStride * 0xc, 1, _yStride);
        }
      }
    }
  }

  private void _NormalFilter() {
    for (var mby = 0; mby < _mbh; ++mby) {
      for (var mbx = 0; mbx < _mbw; ++mbx) {
        var f = _perMBFilterParams[_mbw * mby + mbx];
        if (f.Level == 0) continue;
        int l = f.Level, il = f.Ilevel, hl = f.Hlevel;
        var yIndex = (mby * _yStride + mbx) * 16;
        var cIndex = (mby * _cStride + mbx) * 8;
        if (mbx > 0) {
          _Filter246(_yPlane, 16, l + 4, il, hl, yIndex, _yStride, 1, false);
          _Filter246(_cbPlane, 8, l + 4, il, hl, cIndex, _cStride, 1, false);
          _Filter246(_crPlane, 8, l + 4, il, hl, cIndex, _cStride, 1, false);
        }
        if (f.Inner) {
          _Filter246(_yPlane, 16, l, il, hl, yIndex + 0x4, _yStride, 1, true);
          _Filter246(_yPlane, 16, l, il, hl, yIndex + 0x8, _yStride, 1, true);
          _Filter246(_yPlane, 16, l, il, hl, yIndex + 0xc, _yStride, 1, true);
          _Filter246(_cbPlane, 8, l, il, hl, cIndex + 0x4, _cStride, 1, true);
          _Filter246(_crPlane, 8, l, il, hl, cIndex + 0x4, _cStride, 1, true);
        }
        if (mby > 0) {
          _Filter246(_yPlane, 16, l + 4, il, hl, yIndex, 1, _yStride, false);
          _Filter246(_cbPlane, 8, l + 4, il, hl, cIndex, 1, _cStride, false);
          _Filter246(_crPlane, 8, l + 4, il, hl, cIndex, 1, _cStride, false);
        }
        if (f.Inner) {
          _Filter246(_yPlane, 16, l, il, hl, yIndex + _yStride * 0x4, 1, _yStride, true);
          _Filter246(_yPlane, 16, l, il, hl, yIndex + _yStride * 0x8, 1, _yStride, true);
          _Filter246(_yPlane, 16, l, il, hl, yIndex + _yStride * 0xc, 1, _yStride, true);
          _Filter246(_cbPlane, 8, l, il, hl, cIndex + _cStride * 0x4, 1, _cStride, true);
          _Filter246(_crPlane, 8, l, il, hl, cIndex + _cStride * 0x4, 1, _cStride, true);
        }
      }
    }
  }

  /// <summary>Port of computeFilterParams (§15.4).</summary>
  private void _ComputeFilterParams() {
    for (var i = 0; i < NSegment; ++i) {
      var baseLevel = _filterHeader.Level;
      if (_segmentHeader.UseSegment) {
        baseLevel = _segmentHeader.GetFilterStrength(i);
        if (_segmentHeader.RelativeDelta)
          baseLevel = (sbyte)(baseLevel + _filterHeader.Level);
      }
      for (var j = 0; j < 2; ++j) {
        ref var p = ref _filterParams[i * 2 + j];
        p.Inner = j != 0;
        int level = baseLevel;
        if (_filterHeader.UseLfDelta) {
          level += _filterHeader.RefLfDelta0; // "only CURRENT is handled" per libwebp
          if (j != 0) level += _filterHeader.ModeLfDelta0;
        }
        if (level <= 0) { p.Level = 0; continue; }
        if (level > 63) level = 63;
        var ilevel = level;
        if (_filterHeader.Sharpness > 0) {
          if (_filterHeader.Sharpness > 4) ilevel >>= 2;
          else ilevel >>= 1;
          var max = 9 - _filterHeader.Sharpness;
          if (ilevel > max) ilevel = max;
        }
        if (ilevel < 1) ilevel = 1;
        p.Ilevel = (byte)ilevel;
        p.Level = (byte)(2 * level + ilevel);
        if (_fh.KeyFrame) {
          p.Hlevel = level < 15 ? (byte)0 : level < 40 ? (byte)1 : (byte)2;
        } else {
          p.Hlevel = level < 15 ? (byte)0 : level < 20 ? (byte)1 : level < 40 ? (byte)2 : (byte)3;
        }
      }
    }
  }
}
