using System;
using FileFormat.Codecs.H265;

namespace FileFormat.Bpg.Codec;

/// <summary>
/// Turns a decoded BPG picture into interleaved RGB or RGBA, the way libbpg's
/// <c>bpg_decoder_get_line</c> does it, so the result is the one <c>bpgdec</c> writes.
/// </summary>
/// <remarks>
/// Chroma below full resolution is brought up by libbpg's own filters — an eight-tap Lanczos for a
/// sample that falls on a luma position and a seven-tap one for a sample between two — first
/// vertically into a sixteen-bit intermediate, then horizontally; the colour transform is integer
/// arithmetic on 30-bit fixed-point coefficients derived from the colour space's <c>K_r</c> and
/// <c>K_b</c>. An alpha plane is scaled to the output depth and, where the colour was premultiplied,
/// divided back out; a W plane (CMYK) multiplies the colour instead.
/// <para/>
/// Converted from libbpg (<c>libbpg.c</c>, MIT licence, Fabrice Bellard); the filter taps and the
/// division tables are libbpg's.
/// </remarks>
internal static class BpgColourOutput {

  private const int _Taps2 = 4;
  private const int _Taps = 2 * _Taps2;

  // 8-tap Lanczos (phase 0, symmetric) and 7-tap Lanczos (phase 0.5).
  private const int _Ip0C0 = 40, _Ip0C1 = -11, _Ip0C2 = 4, _Ip0C3 = -1;
  private const int _Ip1C0 = -1, _Ip1C1 = 4, _Ip1C2 = -10, _Ip1C3 = 57, _Ip1C4 = 18, _Ip1C5 = -6, _Ip1C6 = 2;

  private sealed class Convert {
    internal int CShift, CRnd, COne, YOne, YOffset, CrR, CbG, CrG, CbB, Center, BitDepth;
    internal bool LimitedRange;
  }

  private static Convert _Init(int inBitDepth, int outBitDepth, BpgColorSpace colourSpace, bool limitedRange) {
    var s = new Convert();
    var cShift = 30 - outBitDepth;
    var inMax = (1 << inBitDepth) - 1;
    var outMax = (1 << outBitDepth) - 1;
    var mult = (double)outMax * (1 << cShift) / inMax;
    double multY, multC;
    if (limitedRange) {
      multY = (double)outMax * (1 << cShift) / (219 << (inBitDepth - 8));
      multC = (double)outMax * (1 << cShift) / (224 << (inBitDepth - 8));
    } else {
      multY = mult;
      multC = mult;
    }

    double kR = 0, kB = 0;
    var ycc = true;
    switch (colourSpace) {
      case BpgColorSpace.YCbCrBT601: kR = 0.299; kB = 0.114; break;
      case BpgColorSpace.YCbCrBT709: kR = 0.2126; kB = 0.0722; break;
      case BpgColorSpace.YCbCrBT2020Ncl: kR = 0.2627; kB = 0.0593; break;
      default: ycc = false; break;
    }

    if (ycc) {
      s.CrR = _Lrint(2 * (1 - kR) * multC);
      s.CbG = _Lrint(2 * kB * (1 - kB) / (1 - kB - kR) * multC);
      s.CrG = _Lrint(2 * kR * (1 - kR) / (1 - kB - kR) * multC);
      s.CbB = _Lrint(2 * (1 - kB) * multC);
    }

    s.COne = _Lrint(mult);
    s.CShift = cShift;
    s.CRnd = 1 << (cShift - 1);
    s.Center = 1 << (inBitDepth - 1);
    if (limitedRange) {
      s.YOne = _Lrint(multY);
      s.YOffset = -(16 << (inBitDepth - 8)) * s.YOne + s.CRnd;
    } else {
      s.YOne = s.COne;
      s.YOffset = s.CRnd;
    }

    s.BitDepth = inBitDepth;
    s.LimitedRange = limitedRange;
    return s;
  }

  /// <summary>C's <c>lrint</c> in the default rounding mode: nearest, ties to even.</summary>
  private static int _Lrint(double value) => (int)Math.Round(value, MidpointRounding.ToEven);

  private static int _Clamp(int a, int max) => a < 0 ? 0 : a > max ? max : a;

  /// <summary>
  /// The frame as interleaved samples: eight bits per sample when <paramref name="deep"/> is false,
  /// sixteen (big-endian) when it is true; three samples a pixel, or four with <paramref name="rgba"/>.
  /// </summary>
  internal static byte[] ToPixels(
    H265Picture colour, H265Picture? alpha, int width, int height, BpgPixelFormat format, int bitDepth,
    BpgColorSpace colourSpace, bool limitedRange, bool premultiplied, bool wPlane, bool rgba, bool deep) {
    var outBits = deep ? 16 : 8;
    var outMax = (1 << outBits) - 1;
    var s = _Init(bitDepth, outBits, colourSpace, limitedRange);
    var incr = rgba ? 4 : 3;
    var line = new int[width * incr];
    var result = new byte[width * height * incr * (deep ? 2 : 1)];

    var gray = format == BpgPixelFormat.Grayscale;
    var is420 = format is BpgPixelFormat.YCbCr420 or BpgPixelFormat.YCbCr420Mpeg2;
    var is422 = format is BpgPixelFormat.YCbCr422 or BpgPixelFormat.YCbCr422Mpeg2;
    var phase = format is BpgPixelFormat.YCbCr420Mpeg2 or BpgPixelFormat.YCbCr422Mpeg2 ? 0 : 1;
    var w2 = (width + 1) / 2;
    var h2 = (height + 1) / 2;
    var cb2 = new int[width];
    var cr2 = new int[width];
    var tmp = new int[w2 + 2 * _Taps2 - 1 + width];
    var cbRing = new int[_Taps][];
    var crRing = new int[_Taps][];
    if (is420)
      for (var i = 0; i < _Taps; ++i) {
        cbRing[i] = new int[w2];
        crRing[i] = new int[w2];
      }

    var cStride = colour.ChromaWidth;
    void ChromaRow(int row, int[] cbDest, int[] crDest, int count) {
      for (var x = 0; x < count; ++x) {
        cbDest[x] = colour.Cb[row * cStride + x];
        crDest[x] = colour.Cr[row * cStride + x];
      }
    }

    var y = new int[width];
    var cbRow = new int[width];
    var crRow = new int[width];
    for (var row = 0; row < height; ++row) {
      for (var x = 0; x < width; ++x)
        y[x] = colour.Luma[row * colour.Width + x];

      if (gray) {
        _GrayToRgb(s, line, y, width, incr, outMax, outBits);
      } else {
        int[] cbLine, crLine;
        if (is420) {
          if (row == 0)
            for (var i = 0; i < _Taps; ++i) {
              var y1 = i;
              if (y1 > _Taps2)
                y1 -= _Taps;
              y1 = Math.Clamp(y1, 0, h2 - 1);
              ChromaRow(y1, cbRing[i], crRing[i], w2);
            }

          var y2 = row >> 1;
          var pos = y2 % _Taps;
          var frac = row & 1;
          _Interp2Vh(cb2, cbRing, width, pos, tmp, bitDepth, frac, phase);
          _Interp2Vh(cr2, crRing, width, pos, tmp, bitDepth, frac, phase);
          if (frac != 0) {
            pos = (pos + _Taps2 + 1) % _Taps;
            var y1 = Math.Min(y2 + _Taps2 + 1, h2 - 1);
            ChromaRow(y1, cbRing[pos], crRing[pos], w2);
          }

          cbLine = cb2;
          crLine = cr2;
        } else if (is422) {
          ChromaRow(row, cbRow, crRow, w2);
          _Interp2H(cb2, cbRow, width, bitDepth, phase, tmp);
          _Interp2H(cr2, crRow, width, bitDepth, phase, tmp);
          cbLine = cb2;
          crLine = cr2;
        } else {
          ChromaRow(row, cbRow, crRow, width);
          cbLine = cbRow;
          crLine = crRow;
        }

        switch (colourSpace) {
          case BpgColorSpace.Rgb: _RgbToRgb(s, line, y, cbLine, crLine, width, incr, outMax, outBits); break;
          case BpgColorSpace.YCgCo: _YcgcoToRgb(s, line, y, cbLine, crLine, width, incr, outMax); break;
          default: _YccToRgb(s, line, y, cbLine, crLine, width, incr, outMax); break;
        }
      }

      if (wPlane) {
        // RGB times W, then opaque if the caller asked for RGBA.
        var shift = s.BitDepth;
        var rnd = 1 << (shift - 1);
        for (var x = 0; x < width; ++x) {
          var a = alpha!.Luma[row * alpha.Width + x];
          for (var c = 0; c < 3; ++c)
            line[x * incr + c] = (line[x * incr + c] * a + rnd) >> shift;
          if (rgba)
            line[x * incr + 3] = outMax;
        }
      } else if (rgba) {
        if (alpha != null) {
          for (var x = 0; x < width; ++x) {
            var a = alpha.Luma[row * alpha.Width + x];
            line[x * 4 + 3] = deep || bitDepth != 8 ? (a * s.COne + s.CRnd) >> s.CShift : a;
          }

          if (premultiplied) {
            if (deep)
              _AlphaDivide16(line, width);
            else
              _AlphaDivide8(line, width);
          }
        } else
          for (var x = 0; x < width; ++x)
            line[x * 4 + 3] = outMax;
      }

      var at = row * width * incr * (deep ? 2 : 1);
      for (var i = 0; i < width * incr; ++i) {
        if (deep) {
          result[at++] = (byte)(line[i] >> 8);
          result[at++] = (byte)line[i];
        } else
          result[at++] = (byte)line[i];
      }
    }

    return result;
  }

  private static void _YccToRgb(Convert s, int[] q, int[] yv, int[] cb, int[] cr, int n, int incr, int max) {
    for (var x = 0; x < n; ++x) {
      var y = yv[x] * s.YOne;
      var b = cb[x] - s.Center;
      var r = cr[x] - s.Center;
      q[x * incr] = _Clamp((y + s.CrR * r + s.YOffset) >> s.CShift, max);
      q[x * incr + 1] = _Clamp((y - s.CbG * b - s.CrG * r + s.YOffset) >> s.CShift, max);
      q[x * incr + 2] = _Clamp((y + s.CbB * b + s.YOffset) >> s.CShift, max);
    }
  }

  private static void _YcgcoToRgb(Convert s, int[] q, int[] yv, int[] cb, int[] cr, int n, int incr, int max) {
    for (var x = 0; x < n; ++x) {
      var y = yv[x];
      var b = cb[x] - s.Center;
      var r = cr[x] - s.Center;
      q[x * incr] = _Clamp(((y - b + r) * s.YOne + s.YOffset) >> s.CShift, max);
      q[x * incr + 1] = _Clamp(((y + b) * s.YOne + s.YOffset) >> s.CShift, max);
      q[x * incr + 2] = _Clamp(((y - b - r) * s.YOne + s.YOffset) >> s.CShift, max);
    }
  }

  private static void _GrayToRgb(Convert s, int[] q, int[] yv, int n, int incr, int max, int outBits) {
    for (var x = 0; x < n; ++x) {
      var v = outBits == 8 && s.BitDepth == 8 && !s.LimitedRange ? yv[x] : _Clamp((yv[x] * s.YOne + s.YOffset) >> s.CShift, max);
      q[x * incr] = q[x * incr + 1] = q[x * incr + 2] = v;
    }
  }

  /// <summary>BPG's RGB colour space stores G, B, R in its three planes.</summary>
  private static void _RgbToRgb(Convert s, int[] q, int[] yv, int[] cb, int[] cr, int n, int incr, int max, int outBits) {
    var direct = outBits == 8 && s.BitDepth == 8 && !s.LimitedRange;
    for (var x = 0; x < n; ++x) {
      q[x * incr] = direct ? cr[x] : _Clamp((cr[x] * s.YOne + s.YOffset) >> s.CShift, max);
      q[x * incr + 1] = direct ? yv[x] : _Clamp((yv[x] * s.YOne + s.YOffset) >> s.CShift, max);
      q[x * incr + 2] = direct ? cb[x] : _Clamp((cb[x] * s.YOne + s.YOffset) >> s.CShift, max);
    }
  }

  private static readonly uint[] _Divide8 = _BuildDivide8();

  private static uint[] _BuildDivide8() {
    var table = new uint[256];
    for (var i = 1; i < 256; ++i)
      table[i] = (uint)(((255 << 16) + i / 2 + 128) / i);
    return table;
  }

  private static void _AlphaDivide8(int[] q, int n) {
    for (var x = 0; x < n; ++x) {
      var a = (uint)q[x * 4 + 3];
      if (a == 0) {
        q[x * 4] = q[x * 4 + 1] = q[x * 4 + 2] = 255;
        continue;
      }

      var inv = _Divide8[a];
      for (var c = 0; c < 3; ++c) {
        var v = (uint)q[x * 4 + c];
        q[x * 4 + c] = v >= a ? 255 : (int)((v * inv + (1u << 15)) >> 16);
      }
    }
  }

  private static void _AlphaDivide16(int[] q, int n) {
    for (var x = 0; x < n; ++x) {
      var a = (uint)q[x * 4 + 3];
      if (a == 0) {
        q[x * 4] = q[x * 4 + 1] = q[x * 4 + 2] = 65535;
        continue;
      }

      var inv = ((65535u << 15) + a / 2) / a;
      for (var c = 0; c < 3; ++c) {
        var v = (uint)q[x * 4 + c];
        q[x * 4 + c] = v >= a ? 65535 : (int)((v * inv + (1u << 14)) >> 15);
      }
    }
  }

  /// <summary>Horizontal doubling, the chroma sample on (<paramref name="phase"/> 0) or between (1) luma samples.</summary>
  private static void _Interp2H(int[] dst, int[] src, int n, int bitDepth, int phase, int[] tmp) {
    var n2 = (n + 1) / 2;
    Array.Copy(src, 0, tmp, _Taps2 - 1, n2);
    for (var i = 0; i < _Taps2 - 1; ++i)
      tmp[i] = src[0];
    for (var i = 0; i < _Taps2; ++i)
      tmp[_Taps2 - 1 + n2 + i] = src[n2 - 1];
    var max = (1 << bitDepth) - 1;
    if (phase == 0)
      _P0(dst, tmp, _Taps2 - 1, n, 32, 6, max, 0, 0, simple: true);
    else
      _P1(dst, tmp, _Taps2 - 1, n, 32, 6, max);
  }

  private static void _Interp2Vh(int[] dst, int[][] ring, int n, int yPos, int[] tmp, int bitDepth, int frac, int phase) {
    var s0 = ring[(yPos - 3) & 7];
    var s1 = ring[(yPos - 2) & 7];
    var s2 = ring[(yPos - 1) & 7];
    var s3 = ring[yPos & 7];
    var s4 = ring[(yPos + 1) & 7];
    var s5 = ring[(yPos + 2) & 7];
    var s6 = ring[(yPos + 3) & 7];
    var shift = bitDepth - 8;
    var rnd = (1 << shift) >> 1;
    var n2 = (n + 1) / 2;
    for (var i = 0; i < n2; ++i) {
      var sum = frac == 0
        ? s0[i] * _Ip1C6 + s1[i] * _Ip1C5 + s2[i] * _Ip1C4 + s3[i] * _Ip1C3 + s4[i] * _Ip1C2 + s5[i] * _Ip1C1 + s6[i] * _Ip1C0
        : s0[i] * _Ip1C0 + s1[i] * _Ip1C1 + s2[i] * _Ip1C2 + s3[i] * _Ip1C3 + s4[i] * _Ip1C4 + s5[i] * _Ip1C5 + s6[i] * _Ip1C6;
      // libbpg keeps the intermediate in an int16_t.
      tmp[_Taps2 - 1 + i] = (short)((sum + rnd) >> shift);
    }

    var v = tmp[_Taps2 - 1];
    for (var i = 0; i < _Taps2 - 1; ++i)
      tmp[i] = v;
    v = tmp[_Taps2 - 1 + n2 - 1];
    for (var i = 0; i < _Taps2; ++i)
      tmp[_Taps2 - 1 + n2 + i] = v;

    var max = (1 << bitDepth) - 1;
    var shift1 = 20 - bitDepth;
    if (phase == 0) {
      var shift0 = 14 - bitDepth;
      _P0(dst, tmp, _Taps2 - 1, n, 1 << (shift1 - 1), shift1, max, shift0, (1 << shift0) >> 1, simple: false);
    } else
      _P1(dst, tmp, _Taps2 - 1, n, 1 << (shift1 - 1), shift1, max);
  }

  /// <summary>libbpg's <c>interp2p0_simple</c> (shift0 zero) and <c>interp2p0_simple16</c>.</summary>
  private static void _P0(int[] dst, int[] src, int at, int n, int offset, int shift, int max, int shift0, int offset0, bool simple) {
    var d = 0;
    while (n >= 2) {
      dst[d] = simple ? src[at] : _Clamp((src[at] + offset0) >> shift0, max);
      dst[d + 1] = _Clamp(((src[at - 3] + src[at + 4]) * _Ip0C3 + (src[at - 2] + src[at + 3]) * _Ip0C2
        + (src[at - 1] + src[at + 2]) * _Ip0C1 + (src[at] + src[at + 1]) * _Ip0C0 + offset) >> shift, max);
      d += 2;
      ++at;
      n -= 2;
    }

    if (n != 0)
      dst[d] = simple ? src[at] : _Clamp((src[at] + offset0) >> shift0, max);
  }

  /// <summary>libbpg's <c>interp2p1_simple</c> and <c>interp2p1_simple16</c>.</summary>
  private static void _P1(int[] dst, int[] src, int at, int n, int offset, int shift, int max) {
    var a1 = src[at - 3];
    var a2 = src[at - 2];
    var a3 = src[at - 1];
    var a4 = src[at];
    var a5 = src[at + 1];
    var a6 = src[at + 2];
    var d = 0;
    while (true) {
      var a0 = a1;
      a1 = a2;
      a2 = a3;
      a3 = a4;
      a4 = a5;
      a5 = a6;
      a6 = src[at + 3];
      dst[d] = _Clamp((a0 * _Ip1C6 + a1 * _Ip1C5 + a2 * _Ip1C4 + a3 * _Ip1C3 + a4 * _Ip1C2 + a5 * _Ip1C1 + a6 * _Ip1C0 + offset) >> shift, max);
      if (n < 2)
        break;
      dst[d + 1] = _Clamp((a0 * _Ip1C0 + a1 * _Ip1C1 + a2 * _Ip1C2 + a3 * _Ip1C3 + a4 * _Ip1C4 + a5 * _Ip1C5 + a6 * _Ip1C6 + offset) >> shift, max);
      d += 2;
      ++at;
      n -= 2;
      if (n == 0)
        break;
    }
  }
}
