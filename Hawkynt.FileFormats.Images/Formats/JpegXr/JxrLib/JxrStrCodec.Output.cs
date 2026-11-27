using System;
using System.Buffers.Binary;

namespace FileFormat.JpegXr.JxrLib;

/// <summary>
/// Output stage (jxrlib outputMBRow and friends): inverse colour conversion of one finished macroblock row and
/// packing into the native pixel format, little-endian, with jxrlib's offset tables for flips.
/// </summary>
internal sealed partial class JxrStrCodec {

  private static readonly int[,] _IS4 = { { 0, 1, 2, 3 }, { 2, 3, 0, 1 }, { 1, 0, 3, 2 }, { 3, 2, 1, 0 }, { 1, 3, 0, 2 }, { 3, 1, 2, 0 }, { 0, 2, 1, 3 }, { 2, 0, 3, 1 } };

  #region sample helpers

  private static int _Clip(int v, int lo, int hi) => v < lo ? lo : hi < v ? hi : v;

  private static int _Idx(int column, int row) => ((column >> 4) << 8) + JxrTables.IdxCC[row, column & 15];

  private void _W8(long element, int v) => this.Out[element] = (byte)v;
  private void _W16(long element, int v) => BinaryPrimitives.WriteUInt16LittleEndian(this.Out.AsSpan((int)(element * 2)), (ushort)v);
  private void _W32(long element, int v) => BinaryPrimitives.WriteInt32LittleEndian(this.Out.AsSpan((int)(element * 4)), v);

  /// <summary>Signed magnitude half float stored in the codec's two's complement integer domain.</summary>
  private static int _BackwardHalf(int h) {
    var s = h >> 31;
    return (ushort)(((h & 0x7fff) ^ s) - s);
  }

  /// <summary>Integer sample to IEEE single with the stream's mantissa length and exponent bias (jxrlib pixel2float).</summary>
  private static int _Pixel2Float(int h, sbyte c, int lm) {
    var lmShift = 1 << lm;
    var s = h >> 31;
    var t = unchecked((h ^ s) - s);
    var e = (int)((uint)t >> lm);
    var m = (t & (lmShift - 1)) | lmShift;
    if (e == 0) {
      m ^= lmShift;
      e = 1;
    }

    e += 127 - c;
    while (m < lmShift && e > 1 && m > 0) {
      --e;
      m <<= 1;
    }

    if (m < lmShift)
      e = 0;
    else
      m ^= lmShift;
    m <<= 23 - lm;
    return unchecked((int)((uint)s & 0x80000000u)) | (e << 23) | m;
  }

  private static void _InverseConvert(int f, out int rgb, out int e) {
    if (f <= 0) {
      rgb = e = 0;
    } else if ((f >> 7) > 1) {
      e = (byte)(f >> 7);
      rgb = (byte)((f & 0x7f) | 0x80);
    } else {
      e = 1;
      rgb = (byte)f;
    }
  }

  private void _InverseConvertRGBE(int fr, int fg, int fb, long at) {
    _InverseConvert(fr, out var r, out var rE);
    _InverseConvert(fg, out var g, out var gE);
    _InverseConvert(fb, out var b, out var bE);
    var e = Math.Max(Math.Max(rE, gE), bE);
    if (e > rE)
      r = (byte)((r * 2 + 1) >> (e - rE + 1));
    if (e > gE)
      g = (byte)((g * 2 + 1) >> (e - gE + 1));
    if (e > bE)
      b = (byte)((b * 2 + 1) >> (e - bE + 1));
    this.Out[at] = (byte)r;
    this.Out[at + 1] = (byte)g;
    this.Out[at + 2] = (byte)b;
    this.Out[at + 3] = (byte)e;
  }

  #endregion

  private void _CheckImageBuffer(long width, long rows) {
    var bd = this.II.BitDepth;
    var cf = this.II.ColorFormat;
    if (cf == JxrColorFormat.Yuv420)
      rows = (rows + 1) / 2;
    if (rows < 0 || rows > this.OutLines)
      throw _Bad("output buffer too small");
    if (cf is JxrColorFormat.Yuv422 or JxrColorFormat.Yuv420)
      width = (width + 1) / 2;
    var bytes = bd == JxrBitDepth.Bd1 ? (this.II.BitsPerUnit * width + 7) / 8 : (this.II.BitsPerUnit + 7) / 8 * width;
    if (bytes > this.OutStride)
      throw _Bad("output stride too small");
  }

  /// <summary>Upsamples 4:2:0 / 4:2:2 chroma of the finished row to 4:4:4 (or 4:2:2) by averaging neighbours.</summary>
  private void _InterpolateUV() {
    var cfExt = this.II.ColorFormat;
    var width = this.MbWidth * 16;
    var m = this.Mem;
    var srcU = this.A0[1];
    var srcV = this.A0[2];
    var dstU = this.ResU;
    var dstV = this.ResV;
    int idxS, idxD = 0;

    if (this.P.ColorFormat == JxrColorFormat.Yuv422) {
      for (var row = 0; row < 16; ++row) {
        int column;
        for (column = 0; column < width; column += 2) {
          idxS = ((column >> 4) << 7) + JxrTables.IdxCC[row, (column >> 1) & 7];
          idxD = ((column >> 4) << 8) + JxrTables.IdxCC[row, column & 15];
          dstU[idxD] = m[srcU + idxS];
          dstV[idxD] = m[srcV + idxS];
          if (column > 0) {
            var l = column - 2;
            var idxL = ((l >> 4) << 8) + JxrTables.IdxCC[row, l & 15];
            var c = column - 1;
            var idxC = ((c >> 4) << 8) + JxrTables.IdxCC[row, c & 15];
            dstU[idxC] = (dstU[idxL] + dstU[idxD] + 1) >> 1;
            dstV[idxC] = (dstV[idxL] + dstV[idxD] + 1) >> 1;
          }
        }

        idxS = (((column - 1) >> 4) << 8) + JxrTables.IdxCC[row, (column - 1) & 15];
        dstU[idxS] = dstU[idxD];
        dstV[idxS] = dstV[idxD];
      }

      return;
    }

    var shift = cfExt == JxrColorFormat.Yuv422 ? 3 : 4;
    for (var column = 0; column < width; column += 2) {
      var cMB = (column >> 4) << (4 + shift);
      var cPix = (column >> (4 - shift)) & ((1 << shift) - 1);
      for (var row = 0; row < 16; row += 2) {
        idxS = ((column >> 4) << 6) + JxrTables.IdxCC420[row >> 1, (column >> 1) & 7];
        idxD = cMB + JxrTables.IdxCC[row, cPix];
        dstU[idxD] = m[srcU + idxS];
        dstV[idxD] = m[srcV + idxS];
        if (row > 0) {
          var idxT = cMB + JxrTables.IdxCC[row - 2, cPix];
          var idxC = cMB + JxrTables.IdxCC[row - 1, cPix];
          dstU[idxC] = (dstU[idxT] + dstU[idxD] + 1) >> 1;
          dstV[idxC] = (dstV[idxT] + dstV[idxD] + 1) >> 1;
        }
      }

      idxS = cMB + JxrTables.IdxCC[15, cPix];
      if (this.Row == this.MbHeight) {
        dstU[idxS] = dstU[idxD];
        dstV[idxS] = dstV[idxD];
      } else {
        var idxB = ((column >> 4) << 6) + JxrTables.IdxCC420[0, (column >> 1) & 7];
        dstU[idxS] = (m[this.A1[1] + idxB] + dstU[idxD] + 1) >> 1;
        dstV[idxS] = (m[this.A1[2] + idxB] + dstV[idxD] + 1) >> 1;
      }
    }

    if (cfExt == JxrColorFormat.Yuv422)
      return;

    idxS = 0;
    for (var row = 0; row < 16; ++row) {
      for (var column = 1; column < width - 2; column += 2) {
        var idxL = (((column - 1) >> 4) << 8) + JxrTables.IdxCC[row, (column - 1) & 15];
        idxD = ((column >> 4) << 8) + JxrTables.IdxCC[row, column & 15];
        idxS = (((column + 1) >> 4) << 8) + JxrTables.IdxCC[row, (column + 1) & 15];
        dstU[idxD] = (dstU[idxS] + dstU[idxL] + 1) >> 1;
        dstV[idxD] = (dstV[idxS] + dstV[idxL] + 1) >> 1;
      }

      idxD = (((width - 1) >> 4) << 8) + JxrTables.IdxCC[row, (width - 1) & 15];
      dstU[idxD] = dstU[idxS];
      dstV[idxD] = dstV[idxS];
    }
  }

  /// <summary>Writes Y-only, 4:4:4 and N-component rows sample by sample (jxrlib outputNChannel).</summary>
  private void _OutputNChannel(int firstRow, int firstColumn, int width, int height, int shift, int bias) {
    var cChannel = this.II.ColorFormat == JxrColorFormat.YOnly ? 1 : this.SCP.Channels;
    var nLen = this.SCP.LenMantissaOrShift;
    var expBias = this.SCP.ExpBias;
    var offX = this.D.OffsetX;
    var offY = this.D.OffsetY;
    var oy = (this.Row - 1) * 16;
    Span<int> baseIndex = stackalloc int[16];
    var arrays = new int[]?[16];
    for (var c = 0; c < cChannel; ++c) {
      arrays[c & 15] = this.Mem;
      baseIndex[c & 15] = this.A0[c];
    }

    if (this.UVResolutionChange) {
      arrays[1] = this.ResU;
      baseIndex[1] = 0;
      arrays[2] = this.ResV;
      baseIndex[2] = 0;
    }

    var bd = this.II.BitDepth;
    for (var row = firstRow; row < height; ++row) {
      long y = offY[oy + row];
      for (var column = firstColumn; column < width; ++column) {
        var dst = y + offX[column];
        var idx = _Idx(column, row);
        for (var c = 0; c < cChannel; ++c) {
          var p = (arrays[c & 15]![baseIndex[c & 15] + idx] + bias) >> shift;
          switch (bd) {
            case JxrBitDepth.Bd8:
              this._W8(dst + c, _Clip(p, 0, 255));
              break;
            case JxrBitDepth.Bd16:
              this._W16(dst + c, _Clip(p << nLen, 0, 65535));
              break;
            case JxrBitDepth.Bd16S:
              this._W16(dst + c, _Clip(p << nLen, -32768, 32767));
              break;
            case JxrBitDepth.Bd16F:
              this._W16(dst + c, _BackwardHalf(p));
              break;
            case JxrBitDepth.Bd32:
            case JxrBitDepth.Bd32S:
              this._W32(dst + c, p << nLen);
              break;
            case JxrBitDepth.Bd32F:
              this._W32(dst + c, _Pixel2Float(p, expBias, nLen));
              break;
            default:
              return;
          }
        }
      }
    }
  }

  /// <summary>Interleaved alpha output into the colour samples (jxrlib outputMBRowAlpha); only used when the caller asks for alpha.</summary>
  private void _OutputMBRowAlpha() {
    if (this.II.BitDepth == JxrBitDepth.Bd8 && this.SCP.ColorFormat == JxrColorFormat.Rgb)
      return;
    if (this.Secondary || this.Next == null)
      return;

    var bd = this.II.BitDepth;
    var shift = this.P.ScaledArith ? JxrTables.ShiftZero + JxrTables.QpFracBits : 0;
    var d = this.D;
    var height = (int)Math.Min(d.RoiBottomY + 1L - (this.Row - 1L) * 16, 16);
    var width = d.RoiRightX + 1;
    var firstRow = (this.Row - 1) * 16 > d.RoiTopY ? 0 : d.RoiTopY & 0xf;
    var firstColumn = d.RoiLeftX;
    var alphaPos = this.II.LeadingPadding + (this.II.ColorFormat == JxrColorFormat.Cmyk ? 4 : 3);
    var am = this.Next.Mem;
    var a0 = this.Next.A0[0];
    var nLen = this.SCP.LenMantissaOrShift;
    var expBias = this.SCP.ExpBias;
    var oy = (this.Row - 1) * 16;
    // jxrlib writes interleaved alpha for RGB and CMYK only, and its own encoder does not produce a
    // readable n-channel picture with interleaved alpha, so there is nothing to check one against.
    if (this.II.ColorFormat != JxrColorFormat.Rgb && this.II.ColorFormat != JxrColorFormat.Cmyk)
      throw new NotSupportedException("JPEG XR: an n-channel picture with interleaved alpha is not decoded; the reference decoder refuses it too.");

    var bias = bd switch {
      JxrBitDepth.Bd8 => (1 << (shift + 7)) + (shift == 0 ? 0 : 1 << (shift - 1)),
      JxrBitDepth.Bd16 => (1 << (shift + 15)) + (shift == 0 ? 0 : 1 << (shift - 1)),
      JxrBitDepth.Bd16S or JxrBitDepth.Bd16F or JxrBitDepth.Bd32S or JxrBitDepth.Bd32F => shift == 0 ? 0 : 1 << (shift - 1),
      _ => throw _Bad("unsupported alpha bit depth"),
    };

    for (var row = firstRow; row < height; ++row) {
      long y = d.OffsetY[oy + row];
      for (var column = firstColumn; column < width; ++column) {
        var a = (am[a0 + _Idx(column, row)] + bias) >> shift;
        var at = d.OffsetX[column] + y + alphaPos;
        switch (bd) {
          case JxrBitDepth.Bd8: this._W8(at, _Clip(a, 0, 255)); break;
          case JxrBitDepth.Bd16: this._W16(at, _Clip(a << nLen, 0, 65535)); break;
          case JxrBitDepth.Bd16S: this._W16(at, _Clip(a << nLen, -32768, 32767)); break;
          case JxrBitDepth.Bd16F: this._W16(at, _BackwardHalf(a)); break;
          case JxrBitDepth.Bd32S: this._W32(at, a << nLen); break;
          default: this._W32(at, _Pixel2Float(a, expBias, nLen)); break;
        }
      }
    }
  }

  /// <summary>Converts and stores the macroblock row above the one just decoded.</summary>
  private void _OutputMBRow() {
    var cfExt = this.P.ColorFormat == JxrColorFormat.YOnly ? JxrColorFormat.YOnly : this.II.ColorFormat;
    var bd = this.II.BitDepth;
    var shift = this.P.ScaledArith ? JxrTables.ShiftZero + JxrTables.QpFracBits : 0;
    var scaled = this.P.ScaledArith;
    var d = this.D;
    var height = (int)Math.Min(d.RoiBottomY + 1L - (this.Row - 1L) * 16, 16);
    var width = d.RoiRightX + 1;
    var firstRow = (this.Row - 1) * 16 > d.RoiTopY ? 0 : d.RoiTopY & 0xf;
    var firstColumn = d.RoiLeftX;
    var m = this.Mem;
    var pY = this.A0[0];
    var uArr = this.UVResolutionChange ? this.ResU : m;
    var uBase = this.UVResolutionChange ? 0 : this.A0[1];
    var vArr = this.UVResolutionChange ? this.ResV : m;
    var vBase = this.UVResolutionChange ? 0 : this.A0[2];
    var iB = this.II.Rgb ? 2 : 0;
    var iR = 2 - iB;
    var nLen = this.SCP.LenMantissaOrShift;
    var expBias = this.SCP.ExpBias;
    var offX = this.D.OffsetX;
    var offY = this.D.OffsetY;
    var oy = (this.Row - 1) * (cfExt == JxrColorFormat.Yuv420 ? 8 : 16);
    var orientation = this.II.Orientation;
    var roundBias = scaled ? (1 << (JxrTables.ShiftZero + JxrTables.QpFracBits - 1)) - 1 : 0;

    this._CheckImageBuffer(orientation >= 4 ? this.II.RoiHeight : this.II.RoiWidth, (long)height - firstRow);

    if (this.UVResolutionChange)
      this._InterpolateUV();

    switch (bd) {
      case JxrBitDepth.Bd8: {
        var bias1 = 128 << shift;
        var bias2 = roundBias;
        var bias = bias1 + bias2;
        switch (cfExt) {
          case JxrColorFormat.Rgb: {
            var withAlpha = this.Next != null && this.SCP.AlphaMode > 0;
            var am = this.Next?.Mem;
            var a0 = this.Next?.A0[0] ?? 0;
            for (var row = firstRow; row < height; ++row) {
              long y = offY[oy + row];
              for (var column = firstColumn; column < width; ++column) {
                var idx = _Idx(column, row);
                var g = m[pY + idx] + bias;
                var r = -uArr[uBase + idx];
                var b = vArr[vBase + idx];
                _Icc(ref r, ref g, ref b);
                var a = withAlpha ? am![a0 + idx] + bias : 0;
                if (scaled) {
                  g >>= shift;
                  b >>= shift;
                  r >>= shift;
                  a >>= shift;
                }

                var at = offX[column] + y;
                this.Out[at + iR] = (byte)_Clip(r, 0, 255);
                this.Out[at + 1] = (byte)_Clip(g, 0, 255);
                this.Out[at + iB] = (byte)_Clip(b, 0, 255);
                if (withAlpha)
                  this.Out[at + 3] = (byte)_Clip(a, 0, 255);
              }
            }
            break;
          }
          case JxrColorFormat.YOnly:
          case JxrColorFormat.Yuv444:
          case JxrColorFormat.NComponent:
            this._OutputNChannel(firstRow, firstColumn, width, height, shift, bias);
            break;
          case JxrColorFormat.Yuv422:
            for (var row = firstRow; row < height; ++row) {
              long y = offY[oy + row];
              for (var column = firstColumn; column < width; column += 2) {
                var idx = ((column >> 4) << 7) + JxrTables.IdxCC[row, (column >> 1) & 7];
                var u = (uArr[uBase + idx] + bias) >> shift;
                var v = (vArr[vBase + idx] + bias) >> shift;
                var y0 = (m[pY + _Idx(column, row)] + bias) >> shift;
                var y1 = (m[pY + _Idx(column + 1, row)] + bias) >> shift;
                var at = offX[column >> 1] + y;
                this.Out[at] = (byte)_Clip(u, 0, 255);
                this.Out[at + 1] = (byte)_Clip(y0, 0, 255);
                this.Out[at + 2] = (byte)_Clip(v, 0, 255);
                this.Out[at + 3] = (byte)_Clip(y1, 0, 255);
              }
            }
            break;
          case JxrColorFormat.Yuv420: {
            int i0 = _IS4[orientation, 0], i1 = _IS4[orientation, 1], i2 = _IS4[orientation, 2], i3 = _IS4[orientation, 3];
            for (var row = firstRow; row < height; row += 2) {
              long y = offY[oy + (row >> 1)];
              for (var column = firstColumn; column < width; column += 2) {
                var idx = ((column >> 4) << 6) + JxrTables.IdxCC420[row >> 1, (column >> 1) & 7];
                var u = (uArr[uBase + idx] + bias) >> shift;
                var v = (vArr[vBase + idx] + bias) >> shift;
                var y0 = (m[pY + _Idx(column, row)] + bias) >> shift;
                var y1 = (m[pY + _Idx(column + 1, row)] + bias) >> shift;
                var y2 = (m[pY + _Idx(column, row + 1)] + bias) >> shift;
                var y3 = (m[pY + _Idx(column + 1, row + 1)] + bias) >> shift;
                var at = offX[column >> 1] + y;
                this.Out[at + i0] = (byte)_Clip(y0, 0, 255);
                this.Out[at + i1] = (byte)_Clip(y1, 0, 255);
                this.Out[at + i2] = (byte)_Clip(y2, 0, 255);
                this.Out[at + i3] = (byte)_Clip(y3, 0, 255);
                this.Out[at + 4] = (byte)_Clip(u, 0, 255);
                this.Out[at + 5] = (byte)_Clip(v, 0, 255);
              }
            }
            break;
          }
          case JxrColorFormat.Cmyk: {
            var pK = this.A0[3];
            for (var row = firstRow; row < height; ++row) {
              long y = offY[oy + row];
              for (var column = firstColumn; column < width; ++column) {
                var idx = _Idx(column, row);
                var mm = -m[pY + idx] + bias1;
                var c = uArr[uBase + idx];
                var yy = -vArr[vBase + idx];
                var k = m[pK + idx] + bias2;
                _IccCmyk(ref c, ref mm, ref yy, ref k);
                var at = offX[column] + y;
                this.Out[at] = (byte)_Clip(c >> shift, 0, 255);
                this.Out[at + 1] = (byte)_Clip(mm >> shift, 0, 255);
                this.Out[at + 2] = (byte)_Clip(yy >> shift, 0, 255);
                this.Out[at + 3] = (byte)_Clip(k >> shift, 0, 255);
              }
            }
            break;
          }
          case JxrColorFormat.Rgbe:
            for (var row = firstRow; row < height; ++row) {
              long y = offY[oy + row];
              for (var column = firstColumn; column < width; ++column) {
                var idx = _Idx(column, row);
                var g = m[pY + idx] + bias2;
                var r = -uArr[uBase + idx];
                var b = vArr[vBase + idx];
                _Icc(ref r, ref g, ref b);
                this._InverseConvertRGBE(r >> shift, g >> shift, b >> shift, offX[column] + y);
              }
            }
            break;
        }
        break;
      }

      case JxrBitDepth.Bd16: {
        var bias = (((1 << 15) >> nLen) << shift) + (shift == 0 ? 0 : 1 << (shift - 1));
        switch (cfExt) {
          case JxrColorFormat.Rgb:
            for (var row = firstRow; row < height; ++row) {
              long y = offY[oy + row];
              for (var column = firstColumn; column < width; ++column) {
                var idx = _Idx(column, row);
                var g = m[pY + idx] + bias;
                var r = -uArr[uBase + idx];
                var b = vArr[vBase + idx];
                _Icc(ref r, ref g, ref b);
                var at = offX[column] + y;
                this._W16(at, _Clip((r >> shift) << nLen, 0, 65535));
                this._W16(at + 1, _Clip((g >> shift) << nLen, 0, 65535));
                this._W16(at + 2, _Clip((b >> shift) << nLen, 0, 65535));
              }
            }
            break;
          case JxrColorFormat.YOnly:
          case JxrColorFormat.Yuv444:
          case JxrColorFormat.NComponent:
            this._OutputNChannel(firstRow, firstColumn, width, height, shift, bias);
            break;
          case JxrColorFormat.Yuv422: {
            var i0 = orientation is 2 or 3 or 5 or 7 ? 1 : 0;
            var i1 = 1 - i0;
            for (var row = firstRow; row < height; ++row) {
              long y = offY[oy + row];
              for (var column = firstColumn; column < width; column += 2) {
                var idx = ((column >> 4) << 7) + JxrTables.IdxCC[row, (column >> 1) & 7];
                var u = ((uArr[uBase + idx] + bias) >> shift) << nLen;
                var v = ((vArr[vBase + idx] + bias) >> shift) << nLen;
                var y0 = ((m[pY + _Idx(column, row)] + bias) >> shift) << nLen;
                var y1 = ((m[pY + _Idx(column + 1, row)] + bias) >> shift) << nLen;
                var at = offX[column >> 1] + y;
                this._W16(at + i0, _Clip(u, 0, 65535));
                this._W16(at + i1, _Clip(y0, 0, 65535));
                this._W16(at + 2, _Clip(v, 0, 65535));
                this._W16(at + 3, _Clip(y1, 0, 65535));
              }
            }
            break;
          }
          case JxrColorFormat.Yuv420: {
            int i0 = _IS4[orientation, 0], i1 = _IS4[orientation, 1], i2 = _IS4[orientation, 2], i3 = _IS4[orientation, 3];
            for (var row = firstRow; row < height; row += 2) {
              long y = offY[oy + (row >> 1)];
              for (var column = firstColumn; column < width; column += 2) {
                // jxrlib addresses the 16-bit 4:2:0 chroma with the 4:4:4 row table here
                var idx = ((column >> 3) << 6) + JxrTables.IdxCC[row, (column >> 1) & 7];
                var u = ((uArr[uBase + idx] + bias) >> shift) << nLen;
                var v = ((vArr[vBase + idx] + bias) >> shift) << nLen;
                var y0 = ((m[pY + _Idx(column, row)] + bias) >> shift) << nLen;
                var y1 = ((m[pY + _Idx(column + 1, row)] + bias) >> shift) << nLen;
                var y2 = ((m[pY + _Idx(column, row + 1)] + bias) >> shift) << nLen;
                var y3 = ((m[pY + _Idx(column + 1, row + 1)] + bias) >> shift) << nLen;
                var at = offX[column >> 1] + y;
                this._W16(at + i0, _Clip(y0, 0, 65535));
                this._W16(at + i1, _Clip(y1, 0, 65535));
                this._W16(at + i2, _Clip(y2, 0, 65535));
                this._W16(at + i3, _Clip(y3, 0, 65535));
                this._W16(at + 4, _Clip(u, 0, 65535));
                this._W16(at + 5, _Clip(v, 0, 65535));
              }
            }
            break;
          }
          case JxrColorFormat.Cmyk: {
            var pK = this.A0[3];
            var bias1 = (32768 >> nLen) << shift;
            var bias2 = bias - bias1;
            for (var row = firstRow; row < height; ++row) {
              long y = offY[oy + row];
              for (var column = firstColumn; column < width; ++column) {
                var idx = _Idx(column, row);
                var mm = -m[pY + idx] + bias1;
                var c = uArr[uBase + idx];
                var yy = -vArr[vBase + idx];
                var k = m[pK + idx] + bias2;
                _IccCmyk(ref c, ref mm, ref yy, ref k);
                var at = offX[column] + y;
                this._W16(at, _Clip((c >> shift) << nLen, 0, 65535));
                this._W16(at + 1, _Clip((mm >> shift) << nLen, 0, 65535));
                this._W16(at + 2, _Clip((yy >> shift) << nLen, 0, 65535));
                this._W16(at + 3, _Clip((k >> shift) << nLen, 0, 65535));
              }
            }
            break;
          }
        }
        break;
      }

      case JxrBitDepth.Bd16S: {
        var bias = roundBias;
        switch (cfExt) {
          case JxrColorFormat.Rgb:
            for (var row = firstRow; row < height; ++row) {
              long y = offY[oy + row];
              for (var column = firstColumn; column < width; ++column) {
                var idx = _Idx(column, row);
                var g = m[pY + idx] + bias;
                var r = -uArr[uBase + idx];
                var b = vArr[vBase + idx];
                _Icc(ref r, ref g, ref b);
                var at = offX[column] + y;
                this._W16(at, _Clip((r >> shift) << nLen, -32768, 32767));
                this._W16(at + 1, _Clip((g >> shift) << nLen, -32768, 32767));
                this._W16(at + 2, _Clip((b >> shift) << nLen, -32768, 32767));
              }
            }
            break;
          case JxrColorFormat.YOnly:
          case JxrColorFormat.Yuv444:
          case JxrColorFormat.NComponent:
            this._OutputNChannel(firstRow, firstColumn, width, height, shift, bias);
            break;
          case JxrColorFormat.Cmyk: {
            var pK = this.A0[3];
            for (var row = firstRow; row < height; ++row) {
              long y = offY[oy + row];
              for (var column = firstColumn; column < width; ++column) {
                var idx = _Idx(column, row);
                var mm = -m[pY + idx];
                var c = uArr[uBase + idx];
                var yy = -vArr[vBase + idx];
                var k = m[pK + idx] + bias;
                _IccCmyk(ref c, ref mm, ref yy, ref k);
                var at = offX[column] + y;
                this._W16(at, (c >> shift) << nLen);
                this._W16(at + 1, (mm >> shift) << nLen);
                this._W16(at + 2, (yy >> shift) << nLen);
                this._W16(at + 3, (k >> shift) << nLen);
              }
            }
            break;
          }
        }
        break;
      }

      case JxrBitDepth.Bd16F: {
        var bias = roundBias;
        switch (cfExt) {
          case JxrColorFormat.Rgb:
            for (var row = firstRow; row < height; ++row) {
              long y = offY[oy + row];
              for (var column = firstColumn; column < width; ++column) {
                var idx = _Idx(column, row);
                var g = m[pY + idx] + bias;
                var r = -uArr[uBase + idx];
                var b = vArr[vBase + idx];
                _Icc(ref r, ref g, ref b);
                var at = offX[column] + y;
                this._W16(at, _BackwardHalf(r >> shift));
                this._W16(at + 1, _BackwardHalf(g >> shift));
                this._W16(at + 2, _BackwardHalf(b >> shift));
              }
            }
            break;
          case JxrColorFormat.YOnly:
          case JxrColorFormat.Yuv444:
          case JxrColorFormat.NComponent:
            this._OutputNChannel(firstRow, firstColumn, width, height, shift, bias);
            break;
        }
        break;
      }

      case JxrBitDepth.Bd32:
      case JxrBitDepth.Bd32S:
      case JxrBitDepth.Bd32F: {
        var bias = bd == JxrBitDepth.Bd32 ? (((1 << 31) >> nLen) << shift) + (shift == 0 ? 0 : 1 << (shift - 1)) : roundBias;
        switch (cfExt) {
          case JxrColorFormat.Rgb:
            for (var row = firstRow; row < height; ++row) {
              long y = offY[oy + row];
              for (var column = firstColumn; column < width; ++column) {
                var idx = _Idx(column, row);
                var g = m[pY + idx] + bias;
                var r = -uArr[uBase + idx];
                var b = vArr[vBase + idx];
                _Icc(ref r, ref g, ref b);
                var at = offX[column] + y;
                if (bd == JxrBitDepth.Bd32F) {
                  this._W32(at, _Pixel2Float(r >> shift, expBias, nLen));
                  this._W32(at + 1, _Pixel2Float(g >> shift, expBias, nLen));
                  this._W32(at + 2, _Pixel2Float(b >> shift, expBias, nLen));
                } else {
                  this._W32(at, (r >> shift) << nLen);
                  this._W32(at + 1, (g >> shift) << nLen);
                  this._W32(at + 2, (b >> shift) << nLen);
                }
              }
            }
            break;
          case JxrColorFormat.YOnly:
          case JxrColorFormat.Yuv444:
          case JxrColorFormat.NComponent:
            this._OutputNChannel(firstRow, firstColumn, width, height, shift, bias);
            break;
        }
        break;
      }

      case JxrBitDepth.Bd5:
      case JxrBitDepth.Bd565:
      case JxrBitDepth.Bd10: {
        var bias = (bd == JxrBitDepth.Bd5 ? 16 << shift : bd == JxrBitDepth.Bd565 ? 32 << shift : 512 << shift) + roundBias;
        for (var row = firstRow; row < height; ++row) {
          long y = offY[oy + row];
          for (var column = firstColumn; column < width; ++column) {
            var idx = _Idx(column, row);
            var g = m[pY + idx] + bias;
            var r = -uArr[uBase + idx];
            var b = vArr[vBase + idx];
            _Icc(ref r, ref g, ref b);
            var at = offX[column] + y;
            if (bd == JxrBitDepth.Bd5) {
              g >>= shift;
              b >>= shift;
              r >>= shift;
              int lo = this.P.RbSwapped ? b : r, hi = this.P.RbSwapped ? r : b;
              this._W16(at, _Clip(lo, 0, 31) + (_Clip(g, 0, 31) << 5) + (_Clip(hi, 0, 31) << 10));
            } else if (bd == JxrBitDepth.Bd565) {
              g >>= shift;
              b >>= shift + 1;
              r >>= shift + 1;
              int lo = this.P.RbSwapped ? b : r, hi = this.P.RbSwapped ? r : b;
              this._W16(at, _Clip(lo, 0, 31) + (_Clip(g, 0, 63) << 5) + (_Clip(hi, 0, 31) << 11));
            } else {
              g >>= shift;
              b >>= shift;
              r >>= shift;
              int lo = this.P.RbSwapped ? b : r, hi = this.P.RbSwapped ? r : b;
              this._W32(at, _Clip(lo, 0, 1023) + (_Clip(g, 0, 1023) << 10) + (_Clip(hi, 0, 1023) << 20));
            }
          }
        }
        break;
      }

      case JxrBitDepth.Bd1: {
        var pos = this.II.LeadingPadding;
        var th = shift > 0 ? 1 << (shift - 1) : 1;
        var bw = this.SCP.BlackWhite ? 1 : 0;
        for (var row = firstRow; row < height; ++row) {
          long y = offY[oy + row] + pos;
          for (var column = firstColumn; column < width; ++column) {
            var bit = (bw + (m[pY + _Idx(column, row)] >= th ? 1 : 0)) & 1;
            long at;
            int cShift;
            if (orientation < 4) {
              at = (offX[column] >> 3) + y;
              cShift = 7 - (offX[column] & 7);
            } else {
              at = offX[column] + (y >> 3);
              cShift = (int)(7 - (y & 7));
            }

            this.Out[at] = (byte)((this.Out[at] & ~(1 << cShift)) | (bit << cShift));
          }
        }
        break;
      }
    }

    if (this.SCP.AlphaMode > 0)
      this._OutputMBRowAlpha();

    this.LinesDecoded = height - firstRow;

    if (this.II.ColorFormat == JxrColorFormat.Rgb && this.SCP.ColorFormat == JxrColorFormat.YOnly) {
      var size = bd switch {
        JxrBitDepth.Bd8 => 1,
        JxrBitDepth.Bd16 or JxrBitDepth.Bd16S or JxrBitDepth.Bd16F => 2,
        JxrBitDepth.Bd32 or JxrBitDepth.Bd32S or JxrBitDepth.Bd32F => 4,
        _ => 0,
      };
      if (size != 0)
        for (var row = firstRow; row < height; ++row) {
          long y = offY[oy + row];
          for (var column = firstColumn; column < width; ++column) {
            var at = (y + offX[column]) * size;
            for (var k = 1; k <= 2; ++k)
              Buffer.BlockCopy(this.Out, (int)at, this.Out, (int)(at + k * size), size);
          }
        }
    }
  }

  private static void _Icc(ref int r, ref int g, ref int b) {
    g -= r >> 1;
    r -= ((b + 1) >> 1) - g;
    b += r;
  }

  private static void _IccCmyk(ref int c, ref int m, ref int y, ref int k) {
    k -= (m + 1) >> 1;
    m -= (c >> 1) - k;
    c -= ((y + 1) >> 1) - m;
    y += c;
  }
}
