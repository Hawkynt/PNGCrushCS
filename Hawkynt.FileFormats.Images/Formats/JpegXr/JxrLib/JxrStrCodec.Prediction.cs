using System;

namespace FileFormat.JpegXr.JxrLib;

/// <summary>DC/AC/CBP prediction and dequantisation (T.832 clauses 8.3, 9.5-9.7, jxrlib strPredQuant.c / strPredQuantDec.c).</summary>
internal sealed partial class JxrStrCodec {

  private const int _ORIENT_WEIGHT = 4;

  /// <summary>Lowpass prediction direction: 0 = from left, 1 = from top, 2 = none.</summary>
  private int _GetACPredMode() {
    var cf = this.P.ColorFormat;
    var c = this.MB.BlockDC[0];
    var strH = Math.Abs(c[1]) + Math.Abs(c[2]) + Math.Abs(c[3]);
    var strV = Math.Abs(c[4]) + Math.Abs(c[8]) + Math.Abs(c[12]);
    if (cf != JxrColorFormat.YOnly && cf != JxrColorFormat.NComponent) {
      var u = this.MB.BlockDC[1];
      var v = this.MB.BlockDC[2];
      strH += Math.Abs(u[1]) + Math.Abs(v[1]);
      if (cf == JxrColorFormat.Yuv420)
        strV += Math.Abs(u[2]) + Math.Abs(v[2]);
      else if (cf == JxrColorFormat.Yuv422) {
        strV += Math.Abs(u[2]) + Math.Abs(v[2]) + Math.Abs(u[6]) + Math.Abs(v[6]);
        strH += Math.Abs(u[5]) + Math.Abs(v[5]);
      } else
        strV += Math.Abs(u[4]) + Math.Abs(v[4]);
    }

    return strH * _ORIENT_WEIGHT < strV ? 1 : strV * _ORIENT_WEIGHT < strH ? 0 : 2;
  }

  /// <summary>DC mode (0 left, 1 top, 2 mean, 3 none) in bits 0-1 and lowpass AC mode (0 left, 1 top, 2 none) in bits 2-3.</summary>
  private int _GetDCACPredMode(int mbX) {
    int dcMode, adMode = 2;
    if (this.CtxLeft && this.CtxTop)
      dcMode = 3;
    else if (this.CtxLeft)
      dcMode = 1;
    else if (this.CtxTop)
      dcMode = 0;
    else {
      var cf = this.P.ColorFormat;
      var l = this.PredInfo[0][mbX - 1].DC;
      var t = this.PredInfoPrevRow[0][mbX].DC;
      var tl = this.PredInfoPrevRow[0][mbX - 1].DC;
      int strH, strV;
      if (cf is JxrColorFormat.YOnly or JxrColorFormat.NComponent) {
        strH = Math.Abs(tl - l);
        strV = Math.Abs(tl - t);
      } else {
        var tU = this.PredInfoPrevRow[1][mbX].DC;
        var lU = this.PredInfo[1][mbX - 1].DC;
        var tlU = this.PredInfoPrevRow[1][mbX - 1].DC;
        var tV = this.PredInfoPrevRow[2][mbX].DC;
        var lV = this.PredInfo[2][mbX - 1].DC;
        var tlV = this.PredInfoPrevRow[2][mbX - 1].DC;
        var scale = cf == JxrColorFormat.Yuv420 ? 8 : cf == JxrColorFormat.Yuv422 ? 4 : 2;
        strH = Math.Abs(tl - l) * scale + Math.Abs(tlU - lU) + Math.Abs(tlV - lV);
        strV = Math.Abs(tl - t) * scale + Math.Abs(tlU - tU) + Math.Abs(tlV - tV);
      }

      dcMode = strH * _ORIENT_WEIGHT < strV ? 1 : strV * _ORIENT_WEIGHT < strH ? 0 : 2;
    }

    if (dcMode == 1 && this.MB.QIndexLP == this.PredInfoPrevRow[0][mbX].QPIndex)
      adMode = 1;
    if (dcMode == 0 && this.MB.QIndexLP == this.PredInfo[0][mbX - 1].QPIndex)
      adMode = 0;
    return dcMode + (adMode << 2);
  }

  private void _UpdatePredInfo() {
    var cf = this.P.ColorFormat;
    var mbX = this.Column;
    var mb = this.MB;
    var channels = cf is JxrColorFormat.Yuv420 or JxrColorFormat.Yuv422 ? 1 : this.P.NumChannels;
    for (var i = 0; i < channels; ++i) {
      var pi = this.PredInfo[i][mbX];
      var p = mb.BlockDC[i];
      pi.DC = p[0];
      pi.QPIndex = mb.QIndexLP;
      pi.AD[0] = p[1];
      pi.AD[1] = p[2];
      pi.AD[2] = p[3];
      pi.AD[3] = p[4];
      pi.AD[4] = p[8];
      pi.AD[5] = p[12];
    }

    if (cf == JxrColorFormat.Yuv420)
      for (var i = 1; i < 3; ++i) {
        var pi = this.PredInfo[i][mbX];
        var p = mb.BlockDC[i];
        pi.DC = p[0];
        pi.QPIndex = mb.QIndexLP;
        pi.AD[0] = p[1];
        pi.AD[1] = p[2];
      }
    else if (cf == JxrColorFormat.Yuv422)
      for (var i = 1; i < 3; ++i) {
        var pi = this.PredInfo[i][mbX];
        var p = mb.BlockDC[i];
        pi.QPIndex = mb.QIndexLP;
        pi.DC = p[0];
        pi.AD[0] = p[1];
        pi.AD[1] = p[2];
        pi.AD[2] = p[5];
        pi.AD[3] = p[6];
        pi.AD[4] = p[4];
      }
  }

  private void _DequantizeMacroblock() {
    var cf = this.P.ColorFormat;
    var mb = this.MB;
    var tile = this.Tiles[this.TileColumn];
    var mem = this.Mem;
    for (var i = 0; i < this.P.NumChannels; ++i) {
      var rec = this.P1[i];
      var org = mb.BlockDC[i];
      mem[rec] = org[0] * tile.QuantizerDC[i]![0].QP;
      if (this.SCP.Subband == JxrSubband.DcOnly)
        continue;

      var qp = tile.QuantizerLP[i]![mb.QIndexLP].QP;
      if (i == 0 || (cf != JxrColorFormat.Yuv422 && cf != JxrColorFormat.Yuv420)) {
        var index = JxrTables.DctIndex[2];
        for (var k = 1; k < 16; ++k)
          mem[rec + index[k]] = org[k] * qp;
      } else if (cf == JxrColorFormat.Yuv422) {
        mem[rec + 64] = org[1] * qp;
        mem[rec + 16] = org[2] * qp;
        mem[rec + 80] = org[3] * qp;
        mem[rec + 32] = org[4] * qp;
        mem[rec + 96] = org[5] * qp;
        mem[rec + 48] = org[6] * qp;
        mem[rec + 112] = org[7] * qp;
      } else {
        mem[rec + 32] = org[1] * qp;
        mem[rec + 16] = org[2] * qp;
        mem[rec + 48] = org[3] * qp;
      }
    }
  }

  private void _PredDCACDec() {
    var cf = this.P.ColorFormat;
    var channels = cf is JxrColorFormat.Yuv420 or JxrColorFormat.Yuv422 ? 1 : this.P.NumChannels;
    var mb = this.MB;
    var mbX = this.Column;
    var mode = this._GetDCACPredMode(mbX);
    var dcMode = mode & 0x3;
    var adMode = mode & 0xC;

    for (var ii = 0; ii < channels; ++ii) {
      var org = mb.BlockDC[ii];
      if (dcMode == 1)
        org[0] += this.PredInfoPrevRow[ii][mbX].DC;
      else if (dcMode == 0)
        org[0] += this.PredInfo[ii][mbX - 1].DC;
      else if (dcMode == 2)
        org[0] += (this.PredInfo[ii][mbX - 1].DC + this.PredInfoPrevRow[ii][mbX].DC) >> 1;

      if (adMode == 4) {
        var r = this.PredInfoPrevRow[ii][mbX].AD;
        org[4] += r[3];
        org[8] += r[4];
        org[12] += r[5];
      } else if (adMode == 0) {
        var r = this.PredInfo[ii][mbX - 1].AD;
        org[1] += r[0];
        org[2] += r[1];
        org[3] += r[2];
      }
    }

    if (cf == JxrColorFormat.Yuv420)
      for (var ii = 1; ii < 3; ++ii) {
        var org = mb.BlockDC[ii];
        if (dcMode == 1)
          org[0] += this.PredInfoPrevRow[ii][mbX].DC;
        else if (dcMode == 0)
          org[0] += this.PredInfo[ii][mbX - 1].DC;
        else if (dcMode == 2)
          org[0] += (this.PredInfo[ii][mbX - 1].DC + this.PredInfoPrevRow[ii][mbX].DC + 1) >> 1;

        if (adMode == 4)
          org[2] += this.PredInfoPrevRow[ii][mbX].AD[1];
        else if (adMode == 0)
          org[1] += this.PredInfo[ii][mbX - 1].AD[0];
      }
    else if (cf == JxrColorFormat.Yuv422)
      for (var ii = 1; ii < 3; ++ii) {
        var org = mb.BlockDC[ii];
        if (dcMode == 1)
          org[0] += this.PredInfoPrevRow[ii][mbX].DC;
        else if (dcMode == 0)
          org[0] += this.PredInfo[ii][mbX - 1].DC;
        else if (dcMode == 2)
          org[0] += (this.PredInfo[ii][mbX - 1].DC + this.PredInfoPrevRow[ii][mbX].DC + 1) >> 1;

        if (adMode == 4) {
          var r = this.PredInfoPrevRow[ii][mbX].AD;
          org[4] += r[4];
          org[2] += r[3];
          org[6] += org[2];
        } else if (adMode == 0) {
          var r = this.PredInfo[ii][mbX - 1].AD;
          org[4] += r[4];
          org[1] += r[0];
          org[5] += r[2];
        } else if (dcMode == 1)
          org[6] += org[2];
      }

    mb.Orientation = 2 - this._GetACPredMode();
  }

  private static readonly int[] _TopPredBlocks = [1, 2, 3, 5, 6, 7, 9, 10, 11, 13, 14, 15];

  private void _PredACDec() {
    var cf = this.P.ColorFormat;
    var channels = cf is JxrColorFormat.Yuv420 or JxrColorFormat.Yuv422 ? 1 : this.P.NumChannels;
    var acMode = 2 - this.MB.Orientation;
    var m = this.Mem;

    for (var i = 0; i < channels; ++i) {
      var src = this.P1[i];
      switch (acMode) {
        case 1:
          foreach (var b in _TopPredBlocks) {
            var org = src + 16 * b;
            var r = org - 16;
            m[org + 2] += m[r + 2];
            m[org + 10] += m[r + 10];
            m[org + 9] += m[r + 9];
          }
          break;
        case 0:
          for (var j = 64; j < 256; j += 16) {
            var org = src + j;
            var r = org - 64;
            m[org + 1] += m[r + 1];
            m[org + 5] += m[r + 5];
            m[org + 6] += m[r + 6];
          }
          break;
      }
    }

    if (cf == JxrColorFormat.Yuv420)
      for (var i = 16; i <= 20; i += 4) {
        var src = this.P1[(i >> 2) - 3];
        switch (acMode) {
          case 1:
            for (var j = 1; j <= 3; j += 2) {
              var org = src + 16 * j;
              var r = org - 16;
              m[org + 2] += m[r + 2];
              m[org + 10] += m[r + 10];
              m[org + 9] += m[r + 9];
            }
            break;
          case 0:
            for (var j = 2; j <= 3; ++j) {
              var org = src + 16 * j;
              var r = org - 32;
              m[org + 1] += m[r + 1];
              m[org + 5] += m[r + 5];
              m[org + 6] += m[r + 6];
            }
            break;
        }
      }
    else if (cf == JxrColorFormat.Yuv422)
      for (var i = 16; i < 32; i += 8) {
        var src = this.P1[(i >> 3) - 1];
        switch (acMode) {
          case 1:
            for (var j = 2; j < 8; ++j) {
              var org = src + JxrTables.BlkOffsetUV422[j];
              var r = org - 16;
              m[org + 10] += m[r + 10];
              m[org + 2] += m[r + 2];
              m[org + 9] += m[r + 9];
            }
            break;
          case 0:
            for (var j = 1; j < 8; j += 2) {
              var org = src + JxrTables.BlkOffsetUV422[j];
              var r = org - 64;
              m[org + 1] += m[r + 1];
              m[org + 5] += m[r + 5];
              m[org + 6] += m[r + 6];
            }
            break;
        }
      }
  }

  private static readonly int[] _OnesCount = [0, 1, 1, 2, 1, 2, 2, 3, 1, 2, 2, 3, 2, 3, 3, 4];

  private static int _NumOnes(int i) {
    var r = 0;
    i &= 0xffff;
    while (i != 0) {
      r += _OnesCount[i & 0xf];
      i >>= 4;
    }
    return r;
  }

  private static int _Saturate32(int x) => (uint)(x + 16) >= 32 ? (x < 0 ? -16 : 15) : x;

  private static void _UpdateCbpModel(JxrCodingContext ctx, int c1, int nOrig) {
    ctx.CbpCount0[c1] = _Saturate32(ctx.CbpCount0[c1] + nOrig - JxrTables.AvgNDiff);
    ctx.CbpCount1[c1] = _Saturate32(ctx.CbpCount1[c1] + 16 - nOrig - JxrTables.AvgNDiff);
    if (ctx.CbpCount0[c1] < 0)
      ctx.CbpState[c1] = ctx.CbpCount0[c1] < ctx.CbpCount1[c1] ? 1 : 2;
    else if (ctx.CbpCount1[c1] < 0)
      ctx.CbpState[c1] = 2;
    else
      ctx.CbpState[c1] = 0;
  }

  private int _PredCBPCDec(int cbp, int mbX, int c, JxrCodingContext ctx) {
    var c1 = c != 0 ? 1 : 0;
    if (ctx.CbpState[c1] == 0) {
      if (this.CtxLeft) {
        if (this.CtxTop)
          cbp ^= 1;
        else
          cbp ^= (this.PredInfoPrevRow[c][mbX].Cbp >> 10) & 1;
      } else
        cbp ^= (this.PredInfo[c][mbX - 1].Cbp >> 5) & 1;

      cbp ^= 0x02 & (cbp << 1);
      cbp ^= 0x10 & (cbp << 3);
      cbp ^= 0x20 & (cbp << 1);
      cbp ^= (cbp & 0x33) << 2;
      cbp ^= (cbp & 0xcc) << 6;
      cbp ^= (cbp & 0x3300) << 2;
    } else if (ctx.CbpState[c1] == 2)
      cbp ^= 0xffff;

    _UpdateCbpModel(ctx, c1, _NumOnes(cbp));
    return cbp;
  }

  private int _PredCBPC420Dec(int cbp, int mbX, int c, JxrCodingContext ctx) {
    if (ctx.CbpState[1] == 0) {
      if (this.CtxLeft) {
        if (this.CtxTop)
          cbp ^= 1;
        else
          cbp ^= (this.PredInfoPrevRow[c][mbX].Cbp >> 2) & 1;
      } else
        cbp ^= (this.PredInfo[c][mbX - 1].Cbp >> 1) & 1;

      cbp ^= 0x02 & (cbp << 1);
      cbp ^= (cbp & 0x3) << 2;
    } else if (ctx.CbpState[1] == 2)
      cbp ^= 0xf;

    _UpdateCbpModel(ctx, 1, _NumOnes(cbp) * 4);
    return cbp;
  }

  private int _PredCBPC422Dec(int cbp, int mbX, int c, JxrCodingContext ctx) {
    if (ctx.CbpState[1] == 0) {
      if (this.CtxLeft) {
        if (this.CtxTop)
          cbp ^= 1;
        else
          cbp ^= (this.PredInfoPrevRow[c][mbX].Cbp >> 6) & 1;
      } else
        cbp ^= (this.PredInfo[c][mbX - 1].Cbp >> 1) & 1;

      cbp ^= (cbp & 0x1) << 1;
      cbp ^= (cbp & 0x3) << 2;
      cbp ^= (cbp & 0xc) << 2;
      cbp ^= (cbp & 0x30) << 2;
    } else if (ctx.CbpState[1] == 2)
      cbp ^= 0xff;

    _UpdateCbpModel(ctx, 1, _NumOnes(cbp) * 2);
    return cbp;
  }

  private void _PredCBPDec(JxrCodingContext ctx) {
    var cf = this.P.ColorFormat;
    var channels = cf is JxrColorFormat.Yuv420 or JxrColorFormat.Yuv422 ? 1 : this.P.NumChannels;
    var mbX = this.Column;
    var mb = this.MB;
    for (var i = 0; i < channels; ++i)
      this.PredInfo[i][mbX].Cbp = mb.Cbp[i] = this._PredCBPCDec(mb.DiffCbp[i], mbX, i, ctx);

    if (cf == JxrColorFormat.Yuv422) {
      this.PredInfo[1][mbX].Cbp = mb.Cbp[1] = this._PredCBPC422Dec(mb.DiffCbp[1], mbX, 1, ctx);
      this.PredInfo[2][mbX].Cbp = mb.Cbp[2] = this._PredCBPC422Dec(mb.DiffCbp[2], mbX, 2, ctx);
    } else if (cf == JxrColorFormat.Yuv420) {
      this.PredInfo[1][mbX].Cbp = mb.Cbp[1] = this._PredCBPC420Dec(mb.DiffCbp[1], mbX, 1, ctx);
      this.PredInfo[2][mbX].Cbp = mb.Cbp[2] = this._PredCBPC420Dec(mb.DiffCbp[2], mbX, 2, ctx);
    }
  }
}
