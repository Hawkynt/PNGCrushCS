using System;

namespace FileFormat.JpegXr.JxrLib;

/// <summary>Entropy decoding of DC, lowpass and highpass bands (T.832 clause 8, jxrlib segdec.c / decode.c).</summary>
internal sealed partial class JxrStrCodec {

  private const int _BAND_DC = 1;
  private const int _BAND_LP = 2;
  private const int _BAND_AC = 3;
  private const int _MODEL_WEIGHT = 70;

  private static readonly int[] _Alphabet = [5, 4, 8, 7, 7, 12, 6, 6, 12, 6, 6, 7, 7, 12, 6, 6, 12, 6, 6, 7, 7];

  #region coding contexts

  private void _AllocateCodingContexts(int count) {
    if (count > JxrTables.MaxTiles || count < 1)
      throw _Bad("invalid tile column count");
    var cbpSize = this.P.ColorFormat is JxrColorFormat.YOnly or JxrColorFormat.NComponent or JxrColorFormat.Cmyk ? 5 : 9;
    this.Contexts = new JxrCodingContext[count];
    for (var i = 0; i < count; ++i) {
      var ctx = new JxrCodingContext {
        AdaptHuffCBPCY = new JxrAdaptiveHuffman(cbpSize),
        AdaptHuffCBPCY1 = new JxrAdaptiveHuffman(5),
      };
      for (var k = 0; k < JxrTables.NumVlcTables; ++k)
        ctx.AHexpt[k] = new JxrAdaptiveHuffman(_Alphabet[k]);
      this.Contexts[i] = ctx;
      _ResetCodingContext(ctx);
    }
  }

  private static void _ResetCodingContext(JxrCodingContext ctx) {
    ctx.AdaptHuffCBPCY.Initialized = false;
    ctx.AdaptHuffCBPCY1.Initialized = false;
    for (var k = 0; k < JxrTables.NumVlcTables; ++k)
      ctx.AHexpt[k].Initialized = false;

    _AdaptLowpass(ctx);
    _AdaptHighpass(ctx);

    for (var i = 0; i < 16; ++i) {
      ctx.ScanLowpass[i].Scan = (uint)JxrTables.ZigzagInvLowpass[i];
      ctx.ScanHoriz[i].Scan = (uint)JxrTables.DctIndex[0][JxrTables.ZigzagInvH[i]];
      ctx.ScanVert[i].Scan = (uint)JxrTables.DctIndex[0][JxrTables.ZigzagInvV[i]];
    }

    ctx.ModelAC.Reset(_BAND_AC, 0);
    ctx.ModelLP.Reset(_BAND_LP, 4);
    ctx.ModelDC.Reset(_BAND_DC, 8);

    ctx.CBPCountMax = ctx.CBPCountZero = 1;
    ctx.CbpCount0[0] = ctx.CbpCount0[1] = -4;
    ctx.CbpCount1[0] = ctx.CbpCount1[1] = 4;
    ctx.CbpState[0] = ctx.CbpState[1] = 0;
  }

  private static void _AdaptLowpass(JxrCodingContext ctx) {
    for (var k = 0; k < JxrTables.ContextX + JxrTables.CtDc; ++k)
      ctx.AHexpt[k].AdaptDiscriminant();
  }

  private static void _AdaptHighpass(JxrCodingContext ctx) {
    ctx.AdaptHuffCBPCY.AdaptDiscriminant();
    ctx.AdaptHuffCBPCY1.AdaptDiscriminant();
    for (var k = 0; k < JxrTables.ContextX; ++k)
      ctx.AHexpt[k + JxrTables.ContextX + JxrTables.CtDc].AdaptDiscriminant();
  }

  private static readonly int[] _AWeight0 = [240, 12, 1];

  private static readonly int[][] _AWeight1 = [
    [0, 240, 120, 80, 60, 48, 40, 34, 30, 27, 24, 22, 20, 18, 17, 16],
    [0, 12, 6, 4, 3, 2, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1],
    [0, 16, 8, 5, 4, 3, 3, 2, 2, 2, 2, 1, 1, 1, 1, 1],
  ];

  private static readonly int[] _AWeight2 = [120, 37, 2, 120, 18, 1];

  /// <summary>Updates how many raw low bits the band sends, from the number of significant values seen (jxrlib UpdateModelMB).</summary>
  private static void _UpdateModelMB(JxrColorFormat cf, int channels, Span<int> laplacianMean, JxrAdaptiveModel model) {
    var band = model.Band - _BAND_DC;
    laplacianMean[0] *= _AWeight0[band];
    if (cf == JxrColorFormat.Yuv420)
      laplacianMean[1] *= _AWeight2[band];
    else if (cf == JxrColorFormat.Yuv422)
      laplacianMean[1] *= _AWeight2[3 + band];
    else {
      laplacianMean[1] *= _AWeight1[band][channels - 1];
      if (model.Band == _BAND_AC)
        laplacianMean[1] >>= 4;
    }

    for (var j = 0; j < 2; ++j) {
      var lm = laplacianMean[j];
      var ms = model.FlcState[j];
      var delta = (lm - _MODEL_WEIGHT) >> 2;
      if (delta <= -8) {
        delta += 4;
        if (delta < -16)
          delta = -16;
        ms += delta;
        if (ms < -8) {
          if (model.FlcBits[j] == 0)
            ms = -8;
          else {
            ms = 0;
            --model.FlcBits[j];
          }
        }
      } else if (delta >= 8) {
        delta -= 4;
        if (delta > 15)
          delta = 15;
        ms += delta;
        if (ms > 8) {
          if (model.FlcBits[j] >= 15) {
            model.FlcBits[j] = 15;
            ms = 8;
          } else {
            ms = 0;
            ++model.FlcBits[j];
          }
        }
      }

      model.FlcState[j] = ms;
      if (cf == JxrColorFormat.YOnly)
        break;
    }
  }

  #endregion

  #region symbol decoders

  private static readonly int[] _CbpTab = [6, 9, 10, 12];

  /// <summary>Four-bit pattern: 1 => single bit, 2 => one of the 2-bit pairs, 3 => three bits, 4 => all.</summary>
  private static int _DecodeQuadPattern(int code, JxrBitReader io, int oneCode, int twoCode, int threeCode, int fourCode) {
    if (code == twoCode) {
      var v = (int)io.Get(2);
      return v switch {
        0 => 3,
        1 => 5,
        _ => _CbpTab[v * 2 + (int)io.GetBool() - 4],
      };
    }
    if (code == oneCode)
      return 1 << (int)io.Get(2);
    if (code == threeCode)
      return 0xf ^ (1 << (int)io.Get(2));
    if (code == fourCode)
      return 0xf;
    return code;
  }

  private static readonly int[] _GFLC0 = [0, 2, 1, 2, 2, 0];
  private static readonly int[] _GOff0 = [0, 4, 2, 8, 12, 1];
  private static readonly int[] _GOut0 = [0, 15, 3, 12, 1, 2, 4, 8, 5, 6, 9, 10, 7, 11, 13, 14];
  private static readonly int[] _Shift422 = [0, 1, 4, 5];

  private void _DecodeCBP(JxrCodingContext ctx) {
    var io = ctx.IOAC!;
    var cf = this.P.ColorFormat;
    var channels = cf is JxrColorFormat.NComponent or JxrColorFormat.Cmyk ? this.P.NumChannels : 1;
    var ahCbp = ctx.AdaptHuffCBPCY;
    var ahCbp1 = ctx.AdaptHuffCBPCY1;
    var ahEx1 = ctx.AHexpt[1];

    for (var i = 0; i < channels; ++i) {
      int cbpY = 0, cbpU = 0, cbpV = 0;
      var numCbp = JxrAdaptiveHuffman.GetHuffShort(ahCbp1.DecodeTable, io);
      ahCbp1.Discriminant += ahCbp1.DeltaAt(numCbp);
      numCbp = _DecodeQuadPattern(numCbp, io, 1, 2, 3, 4);

      for (var block = 0; block < 4; ++block) {
        if ((numCbp & (1 << block)) == 0)
          continue;

        var numBlockCbp = JxrAdaptiveHuffman.GetHuff(ahCbp.DecodeTable, io);
        var val = numBlockCbp + 1;
        ahCbp.Discriminant += ahCbp.DeltaAt(numBlockCbp);
        numBlockCbp = 0;
        if (val >= 6) {
          if (io.GetBool() != 0)
            numBlockCbp = 0x10;
          else if (io.GetBool() != 0)
            numBlockCbp = 0x20;
          else
            numBlockCbp = 0x30;
          if (val == 9) {
            if (io.GetBool() != 0) {
            } else if (io.GetBool() != 0)
              val = 10;
            else
              val = 11;
          }
          val -= 6;
        }

        var code1 = _GOff0[val];
        if (_GFLC0[val] != 0)
          code1 += (int)io.Get(_GFLC0[val]);
        numBlockCbp += _GOut0[code1];

        switch (cf) {
          case JxrColorFormat.Yuv444:
            cbpY |= (numBlockCbp & 0xf) << (block * 4);
            for (var k = 0; k < 2; ++k) {
              if (((numBlockCbp >> (k + 4)) & 1) == 0)
                continue;
              var code = JxrAdaptiveHuffman.GetHuffShort(ahEx1.DecodeTable, io);
              code = _DecodeQuadPattern(code, io, 0, 1, 2, 3);
              if (k == 0)
                cbpU |= code << (block * 4);
              else
                cbpV |= code << (block * 4);
            }
            break;
          case JxrColorFormat.Yuv420:
            cbpY |= (numBlockCbp & 0xf) << (block * 4);
            cbpU |= ((numBlockCbp >> 4) & 1) << block;
            cbpV |= ((numBlockCbp >> 5) & 1) << block;
            break;
          case JxrColorFormat.Yuv422:
            cbpY |= (numBlockCbp & 0xf) << (block * 4);
            for (var k = 0; k < 2; ++k) {
              var code = 5;
              if (((numBlockCbp >> (k + 4)) & 1) == 0)
                continue;
              if (io.GetBool() != 0)
                code = 1;
              else if (io.GetBool() != 0)
                code = 4;
              code <<= _Shift422[block];
              if (k == 0)
                cbpU |= code;
              else
                cbpV |= code;
            }
            break;
          default:
            cbpY |= numBlockCbp << (block * 4);
            break;
        }
      }

      this.MB.DiffCbp[i] = cbpY;
      if (cf is JxrColorFormat.Yuv420 or JxrColorFormat.Yuv444 or JxrColorFormat.Yuv422) {
        this.MB.DiffCbp[1] = cbpU;
        this.MB.DiffCbp[2] = cbpV;
      }
    }
  }

  private static readonly int[] _RunRemap = [1, 2, 3, 5, 7, 1, 2, 3, 5, 7, 1, 2, 3, 4, 5];

  private static int _DecodeSignificantRun(int maxRun, JxrAdaptiveHuffman ah, JxrBitReader io) {
    if (maxRun < 5) {
      if (maxRun == 1)
        return 1;
      if (io.GetBool() != 0)
        return 1;
      if (maxRun == 2 || io.GetBool() != 0)
        return 2;
      if (maxRun == 3 || io.GetBool() != 0)
        return 3;
      return 4;
    }

    var bin = JxrTables.SignificantRunBin[maxRun];
    var index = JxrAdaptiveHuffman.GetHuffShort(ah.DecodeTable, io) + bin * 5;
    var run = _RunRemap[index];
    var flc = JxrTables.SignificantRunFixedLength[index];
    if (flc != 0)
      run += (int)io.Get(flc);
    return run;
  }

  private static int _DecodeFirstIndex(JxrAdaptiveHuffman ah, JxrBitReader io) {
    var index = JxrAdaptiveHuffman.GetHuff(ah.DecodeTable, io);
    ah.Discriminant += ah.DeltaAt(index);
    ah.Discriminant1 += ah.Delta1At(index);
    return index;
  }

  private static int _DecodeIndex(int loc, JxrAdaptiveHuffman ah, JxrBitReader io) {
    if (loc < 15) {
      var index = JxrAdaptiveHuffman.GetHuffShort(ah.DecodeTable, io);
      ah.Discriminant += ah.DeltaAt(index);
      ah.Discriminant1 += ah.Delta1At(index);
      return index;
    }

    if (loc == 15) {
      if (io.GetBool() == 0)
        return 0;
      if (io.GetBool() == 0)
        return 2;
      return 1 + 2 * (int)io.GetBool();
    }

    return (int)io.Get(1);
  }

  private static readonly int[] _LevelRemap = [2, 3, 4, 6, 10, 14];
  private static readonly int[] _LevelFixedLength = [0, 0, 1, 2, 2, 2];

  private static int _DecodeSignificantAbsLevel(JxrAdaptiveHuffman ah, JxrBitReader io) {
    var index = JxrAdaptiveHuffman.GetHuff(ah.DecodeTable, io);
    if ((uint)index > 6)
      throw _Bad("invalid level symbol");
    ah.Discriminant += ah.DeltaAt(index);
    if (index < 2)
      return index + 2;
    if (index < 6)
      return _LevelRemap[index] + (int)io.Get(_LevelFixedLength[index]);

    var fixedBits = (int)io.Get(4) + 4;
    if (fixedBits == 19) {
      fixedBits += (int)io.Get(2);
      if (fixedBits == 22)
        fixedBits += (int)io.Get(3);
    }

    var level = 2 + (1 << fixedBits);
    return level + (int)io.Get32(fixedBits);
  }

  /// <summary>Run/level decode of a lowpass block into (run, level) pairs (jxrlib DecodeBlock).</summary>
  private static int _DecodeBlock(bool chroma, int[] localCoef, JxrAdaptiveHuffman[] ah, int contextOffset, JxrBitReader io, int location) {
    var numNonzero = 1;
    var ah1 = contextOffset + (chroma ? 3 : 0);

    var index = _DecodeFirstIndex(ah[ah1], io);
    var sr = index & 1;
    var srn = index >> 2;
    var cont = sr & srn;
    var sign = io.GetSign();
    localCoef[1] = (index & 2) != 0
      ? (_DecodeSignificantAbsLevel(ah[6 + contextOffset + cont], io) ^ sign) - sign
      : 1 | sign;

    localCoef[0] = 0;
    if (sr == 0)
      localCoef[0] = _DecodeSignificantRun(15 - location, ah[0], io);
    location += localCoef[0] + 1;

    while (srn != 0) {
      if (numNonzero * 2 + 1 >= localCoef.Length)
        throw _Bad("too many lowpass coefficients");
      sr = srn & 1;
      localCoef[numNonzero * 2] = 0;
      if (sr == 0)
        localCoef[numNonzero * 2] = _DecodeSignificantRun(15 - location, ah[0], io);
      location += localCoef[numNonzero * 2] + 1;
      index = _DecodeIndex(location, ah[ah1 + cont + 1], io);
      srn = index >> 1;
      cont &= srn;
      sign = io.GetSign();
      localCoef[numNonzero * 2 + 1] = (index & 1) != 0
        ? (_DecodeSignificantAbsLevel(ah[6 + contextOffset + cont], io) ^ sign) - sign
        : 1 | sign;
      ++numNonzero;
    }

    return numNonzero;
  }

  /// <summary>Highpass block decode straight into dequantised coefficients with adaptive scan reordering.</summary>
  private int _DecodeBlockHighpass(bool chroma, JxrAdaptiveHuffman[] ah, JxrBitReader io, int qp, int coef, JxrAdaptiveScan[] scan) {
    const int contextOffset = JxrTables.CtDc + JxrTables.ContextX;
    var mem = this.Mem;
    var loc = 1;
    var numNonzero = 1;
    var ah1 = contextOffset + (chroma ? 3 : 0);

    var index = _DecodeFirstIndex(ah[ah1], io);
    var sr = index & 1;
    var srn = index >> 2;
    var cont = sr & srn;
    var sign = io.GetSign();
    var level = (qp ^ sign) - sign;
    if ((index & 2) != 0)
      level *= _DecodeSignificantAbsLevel(ah[6 + contextOffset + cont], io);
    if (sr == 0)
      loc += _DecodeSignificantRun(15 - loc, ah[0], io);
    loc &= 0xf;
    _PlaceCoefficient(mem, coef, scan, loc, level);
    loc = (loc + 1) & 0xf;

    while (srn != 0) {
      sr = srn & 1;
      if (sr == 0) {
        loc += _DecodeSignificantRun(15 - loc, ah[0], io);
        if (loc >= 16)
          return 16;
      }

      index = _DecodeIndex(loc + 1, ah[ah1 + cont + 1], io);
      srn = index >> 1;
      cont &= srn;
      sign = io.GetSign();
      level = (qp ^ sign) - sign;
      if ((index & 1) != 0)
        level *= _DecodeSignificantAbsLevel(ah[6 + contextOffset + cont], io);
      _PlaceCoefficient(mem, coef, scan, loc, level);
      loc = (loc + 1) & 0xf;
      if (++numNonzero > 16)
        throw _Bad("too many highpass coefficients");
    }

    return numNonzero;

    static void _PlaceCoefficient(int[] mem, int coef, JxrAdaptiveScan[] scan, int loc, int level) {
      mem[coef + (int)scan[loc].Scan] = level;
      ++scan[loc].Total;
      if (loc != 0 && scan[loc].Total > scan[loc - 1].Total)
        (scan[loc], scan[loc - 1]) = (scan[loc - 1], scan[loc]);
    }
  }

  private int _DecodeBlockAdaptive(bool noSkip, bool chroma, JxrAdaptiveHuffman[] ah, JxrBitReader io, JxrBitReader? ioFL,
    int coeffs, JxrAdaptiveScan[] scan, int modelBits, int trim, int qp, int[] order, bool skipFlexbits) {
    var mem = this.Mem;
    var numNonzero = 0;
    var flex = modelBits - trim;
    if (flex < 0 || skipFlexbits)
      flex = 0;

    if (noSkip)
      numNonzero = this._DecodeBlockHighpass(chroma, ah, io, qp << modelBits, coeffs, scan);

    if (flex == 0)
      return numNonzero;

    var fl = ioFL!;
    var qp1 = qp + trim == 1 ? 1 : qp << trim;
    for (var k = 1; k < 16; ++k) {
      var at = coeffs + order[k];
      var c = mem[at];
      if (c < 0)
        mem[at] = c - qp1 * (int)fl.Get(flex);
      else if (c > 0)
        mem[at] = c + qp1 * (int)fl.Get(flex);
      else
        mem[at] = qp1 * fl.GetSigned(flex);
    }

    return numNonzero;
  }

  private static int _DecodeQPIndex(JxrBitReader io, int bits) {
    if (io.Get(1) == 0)
      return 0;
    return (int)io.Get(bits) + 1;
  }

  #endregion

  #region macroblock bands

  private void _DecodeMacroblockDC(JxrCodingContext ctx) {
    var tile = this.Tiles[this.TileColumn];
    var mb = this.MB;
    var cf = this.P.ColorFormat;
    var channels = this.P.NumChannels;
    var io = ctx.IODC!;
    Span<int> lm = stackalloc int[2];
    var lmIndex = 0;
    var modelBits = ctx.ModelDC.FlcBits[0];

    for (var i = 0; i < channels; ++i)
      Array.Clear(mb.BlockDC[i]);

    mb.QIndexLP = mb.QIndexHP = 0;
    if (!this.SCP.FrequencyOrder && this.SCP.Subband != JxrSubband.DcOnly) {
      if (tile.BitsLP > 0)
        mb.QIndexLP = _DecodeQPIndex(io, tile.BitsLP);
      if (this.SCP.Subband != JxrSubband.NoHighpass && tile.BitsHP > 0)
        mb.QIndexHP = _DecodeQPIndex(io, tile.BitsHP);
    }

    if (tile.BitsHP == 0 && tile.NumQPHP > 1)
      mb.QIndexHP = mb.QIndexLP;
    if (mb.QIndexLP >= tile.NumQPLP || mb.QIndexHP >= tile.NumQPHP)
      throw _Bad("macroblock quantiser index out of range");

    if (cf is JxrColorFormat.YOnly or JxrColorFormat.Cmyk or JxrColorFormat.NComponent) {
      for (var i = 0; i < channels; ++i) {
        var q = 0;
        if (io.GetBool() != 0) {
          q = _DecodeSignificantAbsLevel(ctx.AHexpt[3], io) - 1;
          lm[lmIndex] += 1;
        }

        if (modelBits != 0)
          q = (q << modelBits) | (int)io.Get(modelBits);
        if (q != 0 && io.GetBool() != 0)
          q = -q;
        mb.BlockDC[i][0] = q;
        lmIndex = 1;
        modelBits = ctx.ModelDC.FlcBits[1];
      }
    } else {
      var index = JxrAdaptiveHuffman.GetHuff(ctx.AHexpt[2].DecodeTable, io);
      var qY = index >> 2;
      var qU = (index >> 1) & 1;
      var qV = index & 1;

      if (qY != 0) {
        qY = _DecodeSignificantAbsLevel(ctx.AHexpt[3], io) - 1;
        lm[0] += 1;
      }
      if (modelBits != 0)
        qY = (qY << modelBits) | (int)io.Get(modelBits);
      if (qY != 0 && io.GetBool() != 0)
        qY = -qY;
      mb.BlockDC[0][0] = qY;

      modelBits = ctx.ModelDC.FlcBits[1];
      if (qU != 0) {
        qU = _DecodeSignificantAbsLevel(ctx.AHexpt[4], io) - 1;
        lm[1] += 1;
      }
      if (modelBits != 0)
        qU = (qU << modelBits) | (int)io.Get(modelBits);
      if (qU != 0 && io.GetBool() != 0)
        qU = -qU;
      mb.BlockDC[1][0] = qU;

      if (qV != 0) {
        qV = _DecodeSignificantAbsLevel(ctx.AHexpt[4], io) - 1;
        lm[1] += 1;
      }
      if (modelBits != 0)
        qV = (qV << modelBits) | (int)io.Get(modelBits);
      if (qV != 0 && io.GetBool() != 0)
        qV = -qV;
      mb.BlockDC[2][0] = qV;
    }

    _UpdateModelMB(cf, channels, lm, ctx.ModelDC);

    if (((!(!this.SCP.FrequencyOrder || this.D.ThumbnailScale < 16)) || this.SCP.Subband == JxrSubband.DcOnly) && this.ResetContext)
      for (var kk = 2; kk < 5; ++kk)
        ctx.AHexpt[kk].AdaptDiscriminant();
  }

  private static readonly int[] _ChromaRemap = [4, 1, 2, 3, 5, 6, 7];

  private void _DecodeMacroblockLowpass(JxrCodingContext ctx) {
    var cf = this.P.ColorFormat;
    var channels = this.P.NumChannels;
    var fullPlanes = cf is JxrColorFormat.Yuv420 or JxrColorFormat.Yuv422 ? 2 : channels;
    var scan = ctx.ScanLowpass;
    var io = ctx.IOLP!;
    var modelBits = ctx.ModelLP.FlcBits[0];
    var rl = new int[32];
    Span<int> lm = stackalloc int[2];
    var lmIndex = 0;
    var cbp = 0;
    var mb = this.MB;
    var tile = this.Tiles[this.TileColumn];
    Span<int> temp = stackalloc int[16];

    if (this.SCP.FrequencyOrder && tile.BitsLP > 0)
      mb.QIndexLP = _DecodeQPIndex(io, tile.BitsLP);

    var aDC = mb.BlockDC;

    if (this.ResetRGITotals) {
      const int scale = 2;
      var weight = scale * 16;
      scan[0].Total = JxrTables.MaxTotal;
      for (var k = 1; k < 16; ++k) {
        scan[k].Total = (uint)weight;
        weight -= scale;
      }
    }

    if (cf is JxrColorFormat.Yuv420 or JxrColorFormat.Yuv422 or JxrColorFormat.Yuv444) {
      var countM = ctx.CBPCountMax;
      var countZ = ctx.CBPCountZero;
      var max = fullPlanes * 4 - 5;
      if (countZ <= 0 || countM < 0) {
        cbp = 0;
        if (io.GetBool() != 0) {
          cbp = 1;
          var k = (int)io.Get(fullPlanes - 1);
          if (k != 0)
            cbp = k * 2 + (int)io.Get(1);
        }
        if (countM < countZ)
          cbp = max - cbp;
      } else
        cbp = (int)io.Get(fullPlanes);

      countM += 1 - 4 * (cbp == max ? 1 : 0);
      countZ += 1 - 4 * (cbp == 0 ? 1 : 0);
      ctx.CBPCountMax = Math.Clamp(countM, -8, 7);
      ctx.CBPCountZero = Math.Clamp(countZ, -8, 7);
    } else
      for (var c = 0; c < channels; ++c)
        cbp |= (int)io.Get(1) << c;

    for (var channel = 0; channel < fullPlanes; ++channel) {
      var coeffs = aDC[channel];
      var subsampledChroma = cf is JxrColorFormat.Yuv420 or JxrColorFormat.Yuv422 && channel != 0;
      if ((cbp & 1) != 0) {
        var location = 1 + 9 * (cf == JxrColorFormat.Yuv420 && channel == 1 ? 1 : 0) + (cf == JxrColorFormat.Yuv422 && channel == 1 ? 1 : 0);
        var numNonzero = _DecodeBlock(channel > 0, rl, ctx.AHexpt, JxrTables.CtDc, io, location);
        lm[lmIndex] += numNonzero;
        if (subsampledChroma) {
          temp.Clear();
          var remapOffset = cf == JxrColorFormat.Yuv420 ? 1 : 0;
          var count = cf == JxrColorFormat.Yuv420 ? 6 : 14;
          var index = 0;
          for (var k = 0; k < numNonzero; ++k) {
            index += rl[k * 2];
            temp[index & 0xf] = rl[k * 2 + 1];
            ++index;
          }
          for (var k = 0; k < count; ++k)
            aDC[(k & 1) + 1][_ChromaRemap[remapOffset + (k >> 1)]] = temp[k];
        } else {
          var index = 1;
          for (var k = 0; k < numNonzero; ++k) {
            index += rl[k * 2];
            if ((uint)index > 15)
              throw _Bad("lowpass run beyond block");
            coeffs[scan[index].Scan] = rl[k * 2 + 1];
            ++scan[index].Total;
            if (scan[index].Total > scan[index - 1].Total)
              (scan[index], scan[index - 1]) = (scan[index - 1], scan[index]);
            ++index;
          }
        }
      }

      if (modelBits != 0) {
        if (subsampledChroma) {
          var limit = cf == JxrColorFormat.Yuv420 ? 4 : 8;
          for (var k = 1; k < limit; ++k) {
            _Refine(aDC[1], k, modelBits, io);
            _Refine(aDC[2], k, modelBits, io);
          }
        } else
          for (var k = 1; k < 16; ++k) {
            if (coeffs[k] > 0) {
              coeffs[k] <<= modelBits;
              coeffs[k] += (int)io.Get(modelBits);
            } else if (coeffs[k] < 0) {
              coeffs[k] <<= modelBits;
              coeffs[k] -= (int)io.Get(modelBits);
            } else
              coeffs[k] = io.GetSigned(modelBits);
          }
      }

      lmIndex = 1;
      modelBits = ctx.ModelLP.FlcBits[1];
      cbp >>= 1;
    }

    _UpdateModelMB(cf, channels, lm, ctx.ModelLP);
    if (this.ResetContext)
      _AdaptLowpass(ctx);

    static void _Refine(int[] block, int k, int modelBits, JxrBitReader io) {
      if (block[k] > 0) {
        block[k] <<= modelBits;
        block[k] += (int)io.Get(modelBits);
      } else if (block[k] < 0) {
        block[k] <<= modelBits;
        block[k] -= (int)io.Get(modelBits);
      } else {
        block[k] = (int)io.Get(modelBits);
        if (block[k] != 0 && io.GetBool() != 0)
          block[k] = -block[k];
      }
    }
  }

  private void _DecodeMacroblockHighpass(JxrCodingContext ctx) {
    if (this.ResetRGITotals) {
      const int scale = 2;
      var weight = scale * 16;
      ctx.ScanHoriz[0].Total = ctx.ScanVert[0].Total = JxrTables.MaxTotal;
      for (var k = 1; k < 16; ++k) {
        ctx.ScanHoriz[k].Total = ctx.ScanVert[k].Total = (uint)weight;
        weight -= scale;
      }
    }

    var tile = this.Tiles[this.TileColumn];
    if (this.SCP.FrequencyOrder && tile.BitsHP > 0) {
      this.MB.QIndexHP = _DecodeQPIndex(ctx.IOAC!, tile.BitsHP);
      if (this.MB.QIndexHP >= tile.NumQPHP)
        throw _Bad("macroblock highpass quantiser index out of range");
    } else if (tile.BitsHP == 0 && tile.NumQPHP > 1)
      this.MB.QIndexHP = this.MB.QIndexLP;

    this._DecodeCBP(ctx);
    this._PredCBPDec(ctx);
    this._DecodeCoeffs(ctx, ctx.IOAC!, ctx.IOFL);

    if (this.ResetContext)
      _AdaptHighpass(ctx);
  }

  private void _DecodeCoeffs(JxrCodingContext ctx, JxrBitReader io, JxrBitReader? ioFL) {
    var tile = this.Tiles[this.TileColumn];
    var cf = this.P.ColorFormat;
    var channels = this.P.NumChannels;
    var planes = cf is JxrColorFormat.Yuv420 or JxrColorFormat.Yuv422 ? 1 : channels;
    var nBlocks = 4;
    var modelBits = ctx.ModelAC.FlcBits[0];
    Span<int> lm = stackalloc int[2];
    var lmIndex = 0;
    var order = JxrTables.DctIndex[0];
    var scan = this.MB.Orientation == 1 ? ctx.ScanVert : ctx.ScanHoriz;
    var chroma = false;
    var cbpU = this.MB.Cbp[1];
    var cbpV = this.MB.Cbp[2];
    var cbpY = this.MB.Cbp[0];

    if (cf == JxrColorFormat.Yuv420) {
      nBlocks = 6;
      cbpY += (cbpU << 16) + (cbpV << 20);
    } else if (cf == JxrColorFormat.Yuv422) {
      nBlocks = 8;
      cbpY += (cbpU << 16) + (cbpV << 24);
    }

    for (var i = 0; i < planes; ++i) {
      var index = 0;
      for (var block = 0; block < nBlocks; ++block) {
        var qpChannel = planes > 1 ? i : block > 3 ? (cf == JxrColorFormat.Yuv420 ? block - 3 : block / 2 - 1) : 0;
        var qp = this.P.Transcode ? 1 : tile.QuantizerHP[qpChannel]![this.MB.QIndexHP].QP;
        for (var sub = 0; sub < 4; ++sub, ++index, cbpY >>= 1) {
          var coeffs = this.P1[i] + JxrTables.BlkOffset[index & 0xf];
          if (block >= 4) {
            if (cf == JxrColorFormat.Yuv420)
              coeffs = this.P1[block - 3] + JxrTables.BlkOffsetUV[sub];
            else
              coeffs = this.P1[1 + (1 & (block >> 1))] + (block & 1) * 32 + JxrTables.BlkOffsetUV422[sub];
          }

          var numNonZero = this._DecodeBlockAdaptive((cbpY & 1) != 0, chroma, ctx.AHexpt, io, ioFL, coeffs, scan,
            modelBits, ctx.TrimFlexBits, qp, order, this.D.SkipFlexbits);
          if (numNonZero > 16)
            throw _Bad("too many highpass coefficients");
          lm[lmIndex] += numNonZero;
        }

        if (block == 3) {
          modelBits = ctx.ModelAC.FlcBits[1];
          lmIndex = 1;
          chroma = true;
        }
      }

      cbpY = this.MB.Cbp[(i + 1) & 0xf];
    }

    _UpdateModelMB(cf, channels, lm, ctx.ModelAC);
  }

  #endregion
}
