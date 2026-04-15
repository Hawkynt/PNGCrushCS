using System;

namespace FileFormat.WebP.Vp8;

// Port of reconstruct.go: macroblock reconstruction — residual parsing, prediction, IDCT, output.
internal sealed partial class Vp8Decoder {

  /// <summary>§13.3 position → band mapping (17 entries, 16 coefficients + 1 for the "end" sentinel).</summary>
  private static readonly byte[] _Bands = [0, 1, 2, 3, 6, 4, 5, 6, 6, 6, 6, 6, 6, 6, 6, 7, 0];

  /// <summary>§14.2 zigzag scan order for 4x4 coefficients.</summary>
  private static readonly byte[] _Zigzag = [0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15];

  /// <summary>§13.2 coefficient category probabilities for categories 3-6.</summary>
  private static readonly byte[][] _Cat3456 = [
    [173, 148, 140, 0, 0, 0, 0, 0, 0, 0, 0, 0],
    [176, 155, 140, 135, 0, 0, 0, 0, 0, 0, 0, 0],
    [180, 157, 141, 134, 130, 0, 0, 0, 0, 0, 0, 0],
    [254, 254, 243, 230, 196, 177, 153, 140, 133, 130, 129, 0],
  ];

  /// <summary>4-byte unpacking of a nibble into [x&1, x&2, x&4, x&8] 0/1 values.</summary>
  private static readonly byte[][] _Unpack = [
    [0,0,0,0],[1,0,0,0],[0,1,0,0],[1,1,0,0],
    [0,0,1,0],[1,0,1,0],[0,1,1,0],[1,1,1,0],
    [0,0,0,1],[1,0,0,1],[0,1,0,1],[1,1,0,1],
    [0,0,1,1],[1,0,1,1],[0,1,1,1],[1,1,1,1],
  ];

  private static uint _Pack(byte x0, byte x1, byte x2, byte x3, int shift) =>
    ((uint)x0 | (uint)x1 << 1 | (uint)x2 << 2 | (uint)x3 << 3) << shift;

  private static byte _Btou(bool b) => b ? (byte)1 : (byte)0;

  /// <summary>Initialize the top+left border of the ybr workspace for macroblock (mbx, mby).</summary>
  private void _PrepareYBR(int mbx, int mby) {
    if (mbx == 0) {
      for (var y = 0; y < 17; ++y)
        _ybr[y * 32 + 7] = 0x81;
      for (var y = 17; y < 26; ++y) {
        _ybr[y * 32 + 7] = 0x81;
        _ybr[y * 32 + 23] = 0x81;
      }
    } else {
      for (var y = 0; y < 17; ++y)
        _ybr[y * 32 + 7] = _ybr[y * 32 + 7 + 16];
      for (var y = 17; y < 26; ++y) {
        _ybr[y * 32 + 7] = _ybr[y * 32 + 15];
        _ybr[y * 32 + 23] = _ybr[y * 32 + 31];
      }
    }
    if (mby == 0) {
      for (var x = 7; x < 28; ++x)
        _ybr[0 * 32 + x] = 0x7f;
      for (var x = 7; x < 16; ++x)
        _ybr[17 * 32 + x] = 0x7f;
      for (var x = 23; x < 32; ++x)
        _ybr[17 * 32 + x] = 0x7f;
    } else {
      for (var i = 0; i < 16; ++i)
        _ybr[0 * 32 + 8 + i] = _yPlane[(16 * mby - 1) * _yStride + 16 * mbx + i];
      for (var i = 0; i < 8; ++i) {
        _ybr[17 * 32 + 8 + i] = _cbPlane[(8 * mby - 1) * _cStride + 8 * mbx + i];
        _ybr[17 * 32 + 24 + i] = _crPlane[(8 * mby - 1) * _cStride + 8 * mbx + i];
      }
      if (mbx == _mbw - 1) {
        for (var i = 16; i < 20; ++i)
          _ybr[0 * 32 + 8 + i] = _yPlane[(16 * mby - 1) * _yStride + 16 * mbx + 15];
      } else {
        for (var i = 16; i < 20; ++i)
          _ybr[0 * 32 + 8 + i] = _yPlane[(16 * mby - 1) * _yStride + 16 * mbx + i];
      }
    }
    for (var y = 4; y < 16; y += 4) {
      _ybr[y * 32 + 24] = _ybr[0 * 32 + 24];
      _ybr[y * 32 + 25] = _ybr[0 * 32 + 25];
      _ybr[y * 32 + 26] = _ybr[0 * 32 + 26];
      _ybr[y * 32 + 27] = _ybr[0 * 32 + 27];
    }
  }

  /// <summary>§13.3 parse one 4x4 region of residual coefficients. Returns 1 if any non-zero, 0 otherwise.</summary>
  private byte _ParseResiduals4(Vp8Partition r, int plane, byte context, ushort quantDc, ushort quantAc, bool skipFirstCoeff, int coeffBase) {
    var n = 0;
    if (skipFirstCoeff) n = 1;
    var band = _Bands[n];
    var pBase = TpIdx(plane, band, context, 0);
    if (!r.ReadBit(_tokenProb[pBase + 0])) return 0;
    while (n != 16) {
      ++n;
      if (!r.ReadBit(_tokenProb[pBase + 1])) {
        pBase = TpIdx(plane, _Bands[n], 0, 0);
        continue;
      }
      uint v;
      if (!r.ReadBit(_tokenProb[pBase + 2])) {
        v = 1;
        pBase = TpIdx(plane, _Bands[n], 1, 0);
      } else {
        if (!r.ReadBit(_tokenProb[pBase + 3])) {
          if (!r.ReadBit(_tokenProb[pBase + 4])) v = 2;
          else v = 3 + r.ReadUint(_tokenProb[pBase + 5], 1);
        } else if (!r.ReadBit(_tokenProb[pBase + 6])) {
          if (!r.ReadBit(_tokenProb[pBase + 7])) {
            // Category 1.
            v = 5 + r.ReadUint(159, 1);
          } else {
            // Category 2.
            v = 7 + 2 * r.ReadUint(165, 1) + r.ReadUint(145, 1);
          }
        } else {
          // Categories 3, 4, 5 or 6.
          var b1 = r.ReadUint(_tokenProb[pBase + 8], 1);
          var b0 = r.ReadUint(_tokenProb[pBase + 9 + (int)b1], 1);
          var cat = 2 * b1 + b0;
          var tab = _Cat3456[cat];
          v = 0;
          for (var i = 0; tab[i] != 0; ++i) {
            v *= 2;
            v += r.ReadUint(tab[i], 1);
          }
          v += 3u + (8u << (int)cat);
        }
        pBase = TpIdx(plane, _Bands[n], 2, 0);
      }
      var z = _Zigzag[n - 1];
      var c = (int)v * (int)(z > 0 ? quantAc : quantDc);
      if (r.ReadBit(Vp8Partition.UniformProb))
        c = -c;
      _coeff[coeffBase + z] = (short)c;
      if (n == 16 || !r.ReadBit(_tokenProb[pBase + 0]))
        return 1;
    }
    return 1;
  }

  /// <summary>Parse all 24 residual 4x4 blocks (16 Y + 4 U + 4 V, plus optional Y2 WHT).</summary>
  private bool _ParseResiduals(int mbx, int mby) {
    var partition = _op[mby & (_nOp - 1)];
    var plane = PlaneY1SansY2;
    ref var q = ref _quant[_segment];

    // Parse the DC coefficient of each 4x4 luma region (via Y2 plane) if using Y16 prediction.
    if (_usePredY16) {
      var nz = _ParseResiduals4(partition, PlaneY2, (byte)(_leftMB.NzY16 + _upMB[mbx].NzY16), q.Y2Dc, q.Y2Ac, false, WhtCoeffBase);
      _leftMB.NzY16 = nz;
      _upMB[mbx].NzY16 = nz;
      _InverseWht16();
      plane = PlaneY1WithY2;
    }

    uint nzDCMask = 0, nzACMask = 0;
    var coeffBase = 0;
    byte nzDC0 = 0, nzDC1 = 0, nzDC2 = 0, nzDC3 = 0;
    byte nzAC0 = 0, nzAC1 = 0, nzAC2 = 0, nzAC3 = 0;

    // Parse luma coefficients.
    var lnz = (byte[])_Unpack[_leftMB.NzMask & 0x0f].Clone();
    var unz = (byte[])_Unpack[_upMB[mbx].NzMask & 0x0f].Clone();
    for (var y = 0; y < 4; ++y) {
      var nz = lnz[y];
      for (var x = 0; x < 4; ++x) {
        nz = _ParseResiduals4(partition, plane, (byte)(nz + unz[x]), q.Y1Dc, q.Y1Ac, _usePredY16, coeffBase);
        unz[x] = nz;
        switch (x) {
          case 0: nzAC0 = nz; nzDC0 = _Btou(_coeff[coeffBase] != 0); break;
          case 1: nzAC1 = nz; nzDC1 = _Btou(_coeff[coeffBase] != 0); break;
          case 2: nzAC2 = nz; nzDC2 = _Btou(_coeff[coeffBase] != 0); break;
          case 3: nzAC3 = nz; nzDC3 = _Btou(_coeff[coeffBase] != 0); break;
        }
        coeffBase += 16;
      }
      lnz[y] = nz;
      nzDCMask |= _Pack(nzDC0, nzDC1, nzDC2, nzDC3, y * 4);
      nzACMask |= _Pack(nzAC0, nzAC1, nzAC2, nzAC3, y * 4);
    }
    var lnzMask = _Pack(lnz[0], lnz[1], lnz[2], lnz[3], 0);
    var unzMask = _Pack(unz[0], unz[1], unz[2], unz[3], 0);

    // Parse chroma coefficients (2 x 2x2 blocks of 4x4 regions).
    lnz = (byte[])_Unpack[_leftMB.NzMask >> 4].Clone();
    unz = (byte[])_Unpack[_upMB[mbx].NzMask >> 4].Clone();
    for (var c = 0; c < 4; c += 2) {
      byte lac0 = 0, lac1 = 0, lac2 = 0, lac3 = 0;
      byte ldc0 = 0, ldc1 = 0, ldc2 = 0, ldc3 = 0;
      for (var y = 0; y < 2; ++y) {
        var nz = lnz[y + c];
        for (var x = 0; x < 2; ++x) {
          nz = _ParseResiduals4(partition, PlaneUV, (byte)(nz + unz[x + c]), q.UvDc, q.UvAc, false, coeffBase);
          unz[x + c] = nz;
          var idx = y * 2 + x;
          var dcBit = _Btou(_coeff[coeffBase] != 0);
          switch (idx) {
            case 0: lac0 = nz; ldc0 = dcBit; break;
            case 1: lac1 = nz; ldc1 = dcBit; break;
            case 2: lac2 = nz; ldc2 = dcBit; break;
            case 3: lac3 = nz; ldc3 = dcBit; break;
          }
          coeffBase += 16;
        }
        lnz[y + c] = nz;
      }
      nzDCMask |= _Pack(ldc0, ldc1, ldc2, ldc3, 16 + c * 2);
      nzACMask |= _Pack(lac0, lac1, lac2, lac3, 16 + c * 2);
    }
    lnzMask |= _Pack(lnz[0], lnz[1], lnz[2], lnz[3], 4);
    unzMask |= _Pack(unz[0], unz[1], unz[2], unz[3], 4);

    _leftMB.NzMask = (byte)lnzMask;
    _upMB[mbx].NzMask = (byte)unzMask;
    _nzDcMask = nzDCMask;
    _nzAcMask = nzACMask;

    return nzDCMask == 0 && nzACMask == 0;
  }

  /// <summary>Apply prediction + IDCT for one MB, writing into the ybr workspace.</summary>
  private void _ReconstructMacroblock(int mbx, int mby) {
    if (_usePredY16) {
      var p = _CheckTopLeftPred(mbx, mby, _predY16);
      _Pred16(p, 1, 8);
      for (var j = 0; j < 4; ++j)
        for (var i = 0; i < 4; ++i) {
          var n = 4 * j + i;
          var y = 4 * j + 1;
          var x = 4 * i + 8;
          var mask = 1u << n;
          if ((_nzAcMask & mask) != 0) _InverseDct4(y, x, 16 * n);
          else if ((_nzDcMask & mask) != 0) _InverseDct4DcOnly(y, x, 16 * n);
        }
    } else {
      for (var j = 0; j < 4; ++j)
        for (var i = 0; i < 4; ++i) {
          var n = 4 * j + i;
          var y = 4 * j + 1;
          var x = 4 * i + 8;
          _Pred4(_predY4[j * 4 + i], y, x);
          var mask = 1u << n;
          if ((_nzAcMask & mask) != 0) _InverseDct4(y, x, 16 * n);
          else if ((_nzDcMask & mask) != 0) _InverseDct4DcOnly(y, x, 16 * n);
        }
    }
    var pc = _CheckTopLeftPred(mbx, mby, _predC8);
    _Pred8(pc, YbrBY, YbrBX);
    if ((_nzAcMask & 0x0f0000) != 0) _InverseDct8(YbrBY, YbrBX, BCoeffBase);
    else if ((_nzDcMask & 0x0f0000) != 0) _InverseDct8DcOnly(YbrBY, YbrBX, BCoeffBase);
    _Pred8(pc, YbrRY, YbrRX);
    if ((_nzAcMask & 0xf00000) != 0) _InverseDct8(YbrRY, YbrRX, RCoeffBase);
    else if ((_nzDcMask & 0xf00000) != 0) _InverseDct8DcOnly(YbrRY, YbrRX, RCoeffBase);
  }

  /// <summary>Decode one macroblock end-to-end: pred modes → coeffs → predict+IDCT → copy into Y/Cb/Cr.</summary>
  private bool _Reconstruct(int mbx, int mby) {
    if (_segmentHeader.UpdateMap) {
      if (!_fp.ReadBit(_segmentHeader.Prob0))
        _segment = (int)_fp.ReadUint(_segmentHeader.Prob1, 1);
      else
        _segment = (int)_fp.ReadUint(_segmentHeader.Prob2, 1) + 2;
    }
    var skip = false;
    if (_useSkipProb) skip = _fp.ReadBit(_skipProb);

    Array.Clear(_coeff, 0, _coeff.Length);
    _PrepareYBR(mbx, mby);

    _usePredY16 = _fp.ReadBit(145);
    if (_usePredY16) _ParsePredModeY16(mbx);
    else _ParsePredModeY4(mbx);
    _ParsePredModeC8();

    if (!skip) {
      skip = _ParseResiduals(mbx, mby);
    } else {
      if (_usePredY16) {
        _leftMB.NzY16 = 0;
        _upMB[mbx].NzY16 = 0;
      }
      _leftMB.NzMask = 0;
      _upMB[mbx].NzMask = 0;
      _nzDcMask = 0;
      _nzAcMask = 0;
    }
    _ReconstructMacroblock(mbx, mby);

    // Copy reconstructed Y, Cb, Cr samples from ybr workspace into the image planes.
    for (int i = (mby * _yStride + mbx) * 16, y = 0; y < 16; i += _yStride, ++y)
      Buffer.BlockCopy(_ybr, (YbrYY + y) * 32 + YbrYX, _yPlane, i, 16);
    for (int i = (mby * _cStride + mbx) * 8, y = 0; y < 8; i += _cStride, ++y) {
      Buffer.BlockCopy(_ybr, (YbrBY + y) * 32 + YbrBX, _cbPlane, i, 8);
      Buffer.BlockCopy(_ybr, (YbrRY + y) * 32 + YbrRX, _crPlane, i, 8);
    }
    return skip;
  }
}
