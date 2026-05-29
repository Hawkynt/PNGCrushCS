using System;

namespace FileFormat.WebP.Vp8;

// Bitstream writing: frame tag, keyframe header, partition 0 (headers + pred modes),
// and coefficient partition (tokens). Selected per-MB modes from _mbY16Mode/_mbUvMode
// drive the mode-coding bits; quantized coefficients from _mbY2/_mbYAc/_mbUv drive the tokens.
internal sealed partial class Vp8Encoder {

  private byte[] _WriteBitstream() {
    var bw = new Vp8BitWriter(_mbw * _mbh * 4);
    var coeffs = new Vp8BitWriter(_mbw * _mbh * 8);

    // --- Frame-level headers (after 10-byte frame tag which is written last) ---
    bw.PutBitUniform(0); // colorspace
    bw.PutBitUniform(0); // clamp_type

    // Segment header: no segments.
    bw.PutBitUniform(0);

    // Filter header: enable decoder-side normal loop filter with a level derived from the
    // base quantizer. The filter smooths quantization-induced blocking artifacts at MB
    // boundaries. Since the decoder applies it at the end of the frame (after all MBs are
    // reconstructed), it does not affect prediction context — encoder and decoder stay in
    // sync on unfiltered reconstructions. Empirically picked: level scales with quantizer,
    // with a floor so we don't over-filter low-Q output.
    // Filter only at lower quality where blocking artifacts are visible. Above quality ~68
    // (baseQ < 40) filter is disabled to avoid blurring legitimate high-frequency detail.
    var filterLevel = _baseQ < 40 ? 0 : _baseQ * 5 >> 5;
    if (filterLevel > 63) filterLevel = 63;
    bw.PutBitUniform(0);                 // simple=0 (use normal filter)
    bw.PutBits((uint)filterLevel, 6);    // level
    bw.PutBits(0, 3);                    // sharpness = 0
    bw.PutBitUniform(0);                 // mode_ref_lf_delta_enabled = 0

    // Partition count = 1.
    bw.PutBits(0, 2);

    // Quantization.
    bw.PutBits((uint)_baseQ, 7);
    bw.PutSignedBits(0, 4);
    bw.PutSignedBits(0, 4);
    bw.PutSignedBits(0, 4);
    bw.PutSignedBits(0, 4);
    bw.PutSignedBits(0, 4);

    // Refresh flag (spec: decoder consumes this bit on keyframes too).
    bw.PutBitUniform(0);

    // Coefficient-probability updates. First, count how often each prob slot would emit a
    // 0 vs 1 under the default prob table (stats are value-only and don't depend on the
    // probs used, just on the coefficient values). Then compare: is it cheaper to keep the
    // default or to transmit a new proba? Emit the decision bit (at update_prob) and, when
    // updating, 8 bits of new proba. Returns the adaptive prob table to drive the actual
    // token emission below.
    _CountAllCoeffs();
    var adaptiveProbs = _EmitProbUpdatesAndComputeProbs(bw);

    // Skip proba: count MBs eligible to skip (all coefficients zero). If a useful fraction
    // are skippable, enable the skip-proba feature and emit the proba value. When enabled,
    // each MB gets a 1-bit skip flag in the mode header, and skipped MBs omit their entire
    // coefficient stream — big bit savings at low quality where quantization zeros whole MBs.
    var totalMbs = _mbw * _mbh;
    var nbSkip = 0;
    for (var i = 0; i < totalMbs; ++i) if (_mbSkip[i]) ++nbSkip;
    // libwebp's formula: skip_proba = (total - nb) * 255 / total. Enabled if proba < 250.
    var skipProba = totalMbs > 0 ? (totalMbs - nbSkip) * 255 / totalMbs : 255;
    var useSkipProba = skipProba < 250;
    // When skip-proba is disabled, the decoder reads coefficients for every MB regardless of
    // our _mbSkip flag — so we must NOT short-circuit coefficient emission in that case.
    // Simplest: clear the skip flags if useSkipProba is off.
    if (!useSkipProba) for (var i = 0; i < totalMbs; ++i) _mbSkip[i] = false;
    bw.PutBitUniform(useSkipProba ? 1 : 0);
    if (useSkipProba) bw.PutBits((uint)skipProba, 8);

    // --- Per-MB prediction modes (still in partition 0) ---
    // For I4x4 MBs we need to track the "above" prediction mode grid across rows.
    // `topI4Modes[mbx*4 + i]` records the mode of the 4x4 block immediately above the
    // current MB's column-i sub-block. Used by _ParsePredModeY4's context model.
    var topI4Modes = new byte[_mbw * 4]; // all B_DC_PRED by default
    for (var mby = 0; mby < _mbh; ++mby) {
      var leftI4Modes = new byte[4]; // modes of 4 rightmost 4x4 blocks in previous MB of this row
      for (var mbx = 0; mbx < _mbw; ++mbx) {
        var mbIdx = mby * _mbw + mbx;
        // Skip flag (before mode bits). Decoder consumes this bit right after the optional
        // segment-update bit and uses it to decide whether to parse residuals.
        if (useSkipProba) bw.PutBit(_mbSkip[mbIdx] ? 1 : 0, (byte)skipProba);
        if (_mbType[mbIdx] == 1) {
          // Intra 16x16
          bw.PutBit(1, 145);
          _WriteI16Mode(bw, _mbY16Mode[mbIdx]);
          // I16 mode replaces all 4x4-block "pred mode context" within this MB with a fixed mode.
          // The mapping from I16 mode to the equivalent 4x4 predictor-mode for context purposes:
          //   DC→B_DC, TM→B_TM, VE→B_VE, HE→B_HE
          var equiv = _mbY16Mode[mbIdx] switch {
            Vp8EncPredict.DC_PRED => Vp8EncPredict.B_DC_PRED,
            Vp8EncPredict.TM_PRED => Vp8EncPredict.B_TM_PRED,
            Vp8EncPredict.V_PRED => Vp8EncPredict.B_VE_PRED,
            _ => Vp8EncPredict.B_HE_PRED,
          };
          for (var i = 0; i < 4; ++i) { topI4Modes[mbx * 4 + i] = equiv; leftI4Modes[i] = equiv; }
        } else {
          // Intra 4x4
          bw.PutBit(0, 145);
          for (var by = 0; by < 4; ++by) {
            var left = leftI4Modes[by];
            for (var bx = 0; bx < 4; ++bx) {
              var above = topI4Modes[mbx * 4 + bx];
              var mode = _mbI4Modes[mbIdx * 16 + by * 4 + bx];
              _WriteI4Mode(bw, mode, above, left);
              topI4Modes[mbx * 4 + bx] = mode;
              left = mode;
            }
            leftI4Modes[by] = left;
          }
        }
        _WriteUvMode(bw, _mbUvMode[mbIdx]);
      }
    }

    // --- Coefficient tokens (in coefficient partition), using adaptive probs ---
    _WriteAllCoeffs(coeffs, adaptiveProbs);

    var part0 = bw.Finish();
    var coeffPart = coeffs.Finish();
    return _AssembleVp8Chunk(part0, coeffPart);
  }

  // I4x4 mode encoding: PutI4Mode from libwebp tree_enc.c, traversing the predProb tree.
  // The 9-element probability vector is indexed by (above_mode, left_mode) — same table
  // the decoder uses in Vp8Decoder.Pred._PredProb.
  private static void _WriteI4Mode(Vp8BitWriter bw, byte mode, byte above, byte left) {
    var baseIdx = (above * 10 + left) * 9;
    if (bw.PutBit(mode != Vp8EncPredict.B_DC_PRED ? 1 : 0, Vp8Decoder._PredProb[baseIdx + 0]) != 0) {
      if (bw.PutBit(mode != Vp8EncPredict.B_TM_PRED ? 1 : 0, Vp8Decoder._PredProb[baseIdx + 1]) != 0) {
        if (bw.PutBit(mode != Vp8EncPredict.B_VE_PRED ? 1 : 0, Vp8Decoder._PredProb[baseIdx + 2]) != 0) {
          if (bw.PutBit(mode >= Vp8EncPredict.B_LD_PRED ? 1 : 0, Vp8Decoder._PredProb[baseIdx + 3]) == 0) {
            if (bw.PutBit(mode != Vp8EncPredict.B_HE_PRED ? 1 : 0, Vp8Decoder._PredProb[baseIdx + 4]) != 0) {
              bw.PutBit(mode != Vp8EncPredict.B_RD_PRED ? 1 : 0, Vp8Decoder._PredProb[baseIdx + 5]);
            }
          } else {
            if (bw.PutBit(mode != Vp8EncPredict.B_LD_PRED ? 1 : 0, Vp8Decoder._PredProb[baseIdx + 6]) != 0) {
              if (bw.PutBit(mode != Vp8EncPredict.B_VL_PRED ? 1 : 0, Vp8Decoder._PredProb[baseIdx + 7]) != 0) {
                bw.PutBit(mode != Vp8EncPredict.B_HD_PRED ? 1 : 0, Vp8Decoder._PredProb[baseIdx + 8]);
              }
            }
          }
        }
      }
    }
  }

  // Mode encoding: PutI16Mode from libwebp tree_enc.c.
  // Modes in libwebp ordering: 0=DC, 1=TM, 2=VE, 3=HE (matches Vp8EncPredict constants).
  private static void _WriteI16Mode(Vp8BitWriter bw, byte mode) {
    var isTmOrHe = mode == Vp8EncPredict.TM_PRED || mode == Vp8EncPredict.H_PRED ? 1 : 0;
    if (bw.PutBit(isTmOrHe, I16ProbTmHe) != 0) {
      // TM or HE branch.
      bw.PutBit(mode == Vp8EncPredict.TM_PRED ? 1 : 0, I16ProbTmVsHe);
    } else {
      // DC or VE branch.
      bw.PutBit(mode == Vp8EncPredict.V_PRED ? 1 : 0, I16ProbDcVsVe);
    }
  }

  // UV mode: DC, VE, HE, TM tree.
  private static void _WriteUvMode(Vp8BitWriter bw, byte mode) {
    if (bw.PutBit(mode != Vp8EncPredict.DC_PRED ? 1 : 0, UvProbDc) != 0) {
      if (bw.PutBit(mode != Vp8EncPredict.V_PRED ? 1 : 0, UvProbVe) != 0) {
        bw.PutBit(mode != Vp8EncPredict.H_PRED ? 1 : 0, UvProbHe);
      }
    }
  }

  /// <summary>Walk all MBs' coefficient data and accumulate token stats into <c>_tokenStats</c>.
  /// Uses the same structure as <see cref="_WriteAllCoeffs"/> but calls <see cref="_CountCoeffs"/>
  /// instead of <see cref="_PutCoeffs"/>. Must produce the same non-zero context propagation
  /// as _WriteAllCoeffs so the stats reflect what would actually be emitted.</summary>
  private void _CountAllCoeffs() {
    Array.Clear(_tokenStats, 0, _tokenStats.Length);
    var topNzY = new byte[_mbw * 4];
    var topNzUV = new byte[_mbw * 4];
    var topNzY2 = new byte[_mbw];

    for (var mby = 0; mby < _mbh; ++mby) {
      byte leftNzY0 = 0, leftNzY1 = 0, leftNzY2 = 0, leftNzY3 = 0;
      byte leftNzU0 = 0, leftNzU1 = 0;
      byte leftNzV0 = 0, leftNzV1 = 0;
      byte leftNzY2Dc = 0;

      for (var mbx = 0; mbx < _mbw; ++mbx) {
        var mbIdx = mby * _mbw + mbx;
        var isI16 = _mbType[mbIdx] == 1;

        if (_mbSkip[mbIdx]) {
          if (isI16) { topNzY2[mbx] = 0; leftNzY2Dc = 0; }
          for (var bx = 0; bx < 4; ++bx) topNzY[mbx * 4 + bx] = 0;
          for (var bx = 0; bx < 4; ++bx) topNzUV[mbx * 4 + bx] = 0;
          leftNzY0 = leftNzY1 = leftNzY2 = leftNzY3 = 0;
          leftNzU0 = leftNzU1 = leftNzV0 = leftNzV1 = 0;
          continue;
        }

        if (isI16) {
          var ctxY2 = (byte)(topNzY2[mbx] + leftNzY2Dc);
          var nzY2 = _CountCoeffs(PlaneY2, ctxY2, _mbY2, mbIdx * 16, 0);
          topNzY2[mbx] = nzY2;
          leftNzY2Dc = nzY2;
        }
        var plane = isI16 ? PlaneY1WithY2 : PlaneY1SansY2;
        var firstCoeff = isI16 ? 1 : 0;
        for (var by = 0; by < 4; ++by) {
          var leftNz = by switch { 0 => leftNzY0, 1 => leftNzY1, 2 => leftNzY2, _ => leftNzY3 };
          for (var bx = 0; bx < 4; ++bx) {
            var blockIdx = by * 4 + bx;
            var ctx = (byte)(topNzY[mbx * 4 + bx] + leftNz);
            var coeffOff = mbIdx * 256 + blockIdx * 16;
            var nz = _CountCoeffs(plane, ctx, _mbYAc, coeffOff, firstCoeff);
            topNzY[mbx * 4 + bx] = nz;
            leftNz = nz;
          }
          switch (by) { case 0: leftNzY0 = leftNz; break; case 1: leftNzY1 = leftNz; break; case 2: leftNzY2 = leftNz; break; default: leftNzY3 = leftNz; break; }
        }
        for (var by = 0; by < 2; ++by) {
          var leftNz = by == 0 ? leftNzU0 : leftNzU1;
          for (var bx = 0; bx < 2; ++bx) {
            var blockIdx = by * 2 + bx;
            var ctx = (byte)(topNzUV[mbx * 4 + bx] + leftNz);
            var coeffOff = mbIdx * 128 + blockIdx * 16;
            var nz = _CountCoeffs(PlaneUV, ctx, _mbUv, coeffOff, 0);
            topNzUV[mbx * 4 + bx] = nz;
            leftNz = nz;
          }
          if (by == 0) leftNzU0 = leftNz; else leftNzU1 = leftNz;
        }
        for (var by = 0; by < 2; ++by) {
          var leftNz = by == 0 ? leftNzV0 : leftNzV1;
          for (var bx = 0; bx < 2; ++bx) {
            var blockIdx = by * 2 + bx;
            var ctx = (byte)(topNzUV[mbx * 4 + 2 + bx] + leftNz);
            var coeffOff = mbIdx * 128 + (4 + blockIdx) * 16;
            var nz = _CountCoeffs(PlaneUV, ctx, _mbUv, coeffOff, 0);
            topNzUV[mbx * 4 + 2 + bx] = nz;
            leftNz = nz;
          }
          if (by == 0) leftNzV0 = leftNz; else leftNzV1 = leftNz;
        }
      }
    }
  }

  /// <summary>From collected stats, compute per-frame optimal probabilities. For each prob
  /// position, compare the bit-cost of keeping the default proba vs using an updated one
  /// (which costs an additional 8-bit proba header). Emit the update bits and return the
  /// chosen prob table. Port of libwebp's <c>FinalizeTokenProbas</c>.</summary>
  private byte[] _EmitProbUpdatesAndComputeProbs(Vp8BitWriter bw) {
    var newProbs = new byte[Vp8Decoder.DefaultTokenProb.Length];
    Array.Copy(Vp8Decoder.DefaultTokenProb, newProbs, newProbs.Length);
    for (var t = 0; t < NumPlane; ++t) {
      for (var b = 0; b < NumBands; ++b) {
        for (var c = 0; c < NumCtx; ++c) {
          for (var p = 0; p < NumProbas; ++p) {
            var nb0 = _tokenStats[t, b, c, p, 0];
            var nb1 = _tokenStats[t, b, c, p, 1];
            var total = nb0 + nb1;
            var updateProb = _CoeffUpdateProb[((t * NumBands + b) * NumCtx + c) * NumProbas + p];
            var oldP = (int)Vp8Decoder.DefaultTokenProb[((t * NumBands + b) * NumCtx + c) * NumProbas + p];
            var newP = total == 0 ? oldP : 255 - nb1 * 255 / total;
            if (newP < 1) newP = 1;
            if (newP > 255) newP = 255;
            // Compare bit costs: oldCost = branchCost(nb1, total, oldP) + costOfKeepingFlag
            //                    newCost = branchCost(nb1, total, newP) + costOfUpdateFlag + 8 bits
            var oldCost = _BranchCost(nb1, total, oldP) + _BitCost(0, updateProb);
            var newCost = _BranchCost(nb1, total, newP) + _BitCost(1, updateProb) + 8 * 256;
            var useNew = oldCost > newCost;
            if (useNew) {
              bw.PutBit(1, updateProb);
              bw.PutBits((uint)newP, 8);
              newProbs[((t * NumBands + b) * NumCtx + c) * NumProbas + p] = (byte)newP;
            } else {
              bw.PutBit(0, updateProb);
            }
          }
        }
      }
    }
    return newProbs;
  }

  /// <summary>Cost of coding <paramref name="nb"/> 1-bits and (<paramref name="total"/> - nb)
  /// 0-bits with probability <paramref name="prob"/>, in fixed-point units (256 = 1 bit).</summary>
  private static long _BranchCost(int nb, int total, int prob) {
    if (total == 0) return 0;
    return (long)nb * _BitCost(1, prob) + (long)(total - nb) * _BitCost(0, prob);
  }

  /// <summary>Cost of coding one bit with probability <paramref name="prob"/>, in units of 1/256 bit.
  /// Uses libwebp's VP8BitCost formula: -log2(prob/256) or -log2((256-prob)/256) as appropriate.</summary>
  private static int _BitCost(int bit, int prob) {
    return bit != 0 ? _Log2OverPercent(256 - prob) : _Log2OverPercent(prob);
  }

  /// <summary>Fixed-point -log2(x/256) * 256, clamped. Table-free approximation suitable for
  /// cost comparisons. Returns ~2048 for prob=1 (very expensive) and ~1 for prob=255 (near-free).</summary>
  private static int _Log2OverPercent(int p) {
    if (p <= 0) return 65536; // very expensive
    if (p >= 256) return 0;
    // Use the identity -log2(p/256) = 8 - log2(p). Approximate log2 via
    // log2(p) ≈ log2(roundUpPow2) - correction. For cost-comparison purposes an exact
    // value isn't needed; we use a simple integer approximation.
    var cost = 0;
    var x = p;
    while (x < 128) { x <<= 1; cost += 256; }
    // Linear interpolation between log2 integer boundaries.
    cost += (256 - x) * 2; // rough linear fit
    return cost;
  }

  private void _WriteAllCoeffs(Vp8BitWriter coeffs, byte[] probs) {
    var topNzY = new byte[_mbw * 4];
    var topNzUV = new byte[_mbw * 4];
    var topNzY2 = new byte[_mbw];

    for (var mby = 0; mby < _mbh; ++mby) {
      byte leftNzY0 = 0, leftNzY1 = 0, leftNzY2 = 0, leftNzY3 = 0;
      byte leftNzU0 = 0, leftNzU1 = 0;
      byte leftNzV0 = 0, leftNzV1 = 0;
      byte leftNzY2Dc = 0;

      for (var mbx = 0; mbx < _mbw; ++mbx) {
        var mbIdx = mby * _mbw + mbx;
        var isI16 = _mbType[mbIdx] == 1;

        if (_mbSkip[mbIdx]) {
          // Skipped MB: no coefficient stream; decoder zeros all nz contexts for this MB.
          // Matches decoder's `ResetAfterSkip` + NzMask clearing logic.
          if (isI16) { topNzY2[mbx] = 0; leftNzY2Dc = 0; }
          for (var bx = 0; bx < 4; ++bx) topNzY[mbx * 4 + bx] = 0;
          for (var bx = 0; bx < 4; ++bx) topNzUV[mbx * 4 + bx] = 0;
          leftNzY0 = leftNzY1 = leftNzY2 = leftNzY3 = 0;
          leftNzU0 = leftNzU1 = leftNzV0 = leftNzV1 = 0;
          continue;
        }

        if (isI16) {
          // Y2 DC block (only present for I16 MBs).
          var ctxY2 = (byte)(topNzY2[mbx] + leftNzY2Dc);
          var nzY2 = _PutCoeffs(coeffs, PlaneY2, ctxY2, _mbY2, mbIdx * 16, 0, probs);
          topNzY2[mbx] = nzY2;
          leftNzY2Dc = nzY2;
        }
        // For I4 MBs: do NOT modify topNzY2/leftNzY2Dc. The decoder preserves Y2 non-zero
        // context across I4 MBs (see _ParseResiduals: Y2 block is only read for usePredY16=true).

        // 16 luma AC (I16) or full 4x4 (I4) blocks.
        var plane = isI16 ? PlaneY1WithY2 : PlaneY1SansY2;
        var firstCoeff = isI16 ? 1 : 0;
        for (var by = 0; by < 4; ++by) {
          var leftNz = by switch { 0 => leftNzY0, 1 => leftNzY1, 2 => leftNzY2, _ => leftNzY3 };
          for (var bx = 0; bx < 4; ++bx) {
            var blockIdx = by * 4 + bx;
            var ctx = (byte)(topNzY[mbx * 4 + bx] + leftNz);
            var coeffOff = mbIdx * 256 + blockIdx * 16;
            var nz = _PutCoeffs(coeffs, plane, ctx, _mbYAc, coeffOff, firstCoeff, probs);
            topNzY[mbx * 4 + bx] = nz;
            leftNz = nz;
          }
          switch (by) { case 0: leftNzY0 = leftNz; break; case 1: leftNzY1 = leftNz; break; case 2: leftNzY2 = leftNz; break; default: leftNzY3 = leftNz; break; }
        }

        // U chroma.
        for (var by = 0; by < 2; ++by) {
          var leftNz = by == 0 ? leftNzU0 : leftNzU1;
          for (var bx = 0; bx < 2; ++bx) {
            var blockIdx = by * 2 + bx;
            var ctx = (byte)(topNzUV[mbx * 4 + bx] + leftNz);
            var coeffOff = mbIdx * 128 + blockIdx * 16;
            var nz = _PutCoeffs(coeffs, PlaneUV, ctx, _mbUv, coeffOff, 0, probs);
            topNzUV[mbx * 4 + bx] = nz;
            leftNz = nz;
          }
          if (by == 0) leftNzU0 = leftNz; else leftNzU1 = leftNz;
        }

        // V chroma.
        for (var by = 0; by < 2; ++by) {
          var leftNz = by == 0 ? leftNzV0 : leftNzV1;
          for (var bx = 0; bx < 2; ++bx) {
            var blockIdx = by * 2 + bx;
            var ctx = (byte)(topNzUV[mbx * 4 + 2 + bx] + leftNz);
            var coeffOff = mbIdx * 128 + (4 + blockIdx) * 16;
            var nz = _PutCoeffs(coeffs, PlaneUV, ctx, _mbUv, coeffOff, 0, probs);
            topNzUV[mbx * 4 + 2 + bx] = nz;
            leftNz = nz;
          }
          if (by == 0) leftNzV0 = leftNz; else leftNzV1 = leftNz;
        }
      }
    }
  }

  // Assemble VP8 chunk: 3-byte frame tag + 7-byte keyframe header + partition0 + coeffs.
  private byte[] _AssembleVp8Chunk(byte[] part0, byte[] coeffPart) {
    var size0 = part0.Length;
    var bits = (uint)0 | 0u << 1 | 1u << 4 | (uint)size0 << 5;
    var totalSize = 10 + size0 + coeffPart.Length;
    var output = new byte[totalSize];
    output[0] = (byte)bits;
    output[1] = (byte)(bits >> 8);
    output[2] = (byte)(bits >> 16);
    output[3] = 0x9d; output[4] = 0x01; output[5] = 0x2a;
    output[6] = (byte)(_width & 0xff);
    output[7] = (byte)((_width >> 8) & 0x3f);
    output[8] = (byte)(_height & 0xff);
    output[9] = (byte)((_height >> 8) & 0x3f);
    Buffer.BlockCopy(part0, 0, output, 10, size0);
    Buffer.BlockCopy(coeffPart, 0, output, 10 + size0, coeffPart.Length);
    return output;
  }

  // --- Token tree traversal ---
  // Parameterized on the prob array so it can be driven with DefaultTokenProb (fixed) or the
  // adaptive per-frame probs computed from statistics (probability-update feature).

  private static byte _PutCoeffs(Vp8BitWriter bw, int plane, byte ctx, short[] coeffs, int offset, int firstCoeff, byte[] probs) {
    var n = firstCoeff;
    var last = -1;
    for (var k = 15; k >= firstCoeff; --k) {
      if (coeffs[offset + k] != 0) { last = k; break; }
    }
    var pBase = _TpIdx(plane, _Bands[n], ctx, 0);
    if (last < 0) {
      bw.PutBit(0, probs[pBase + 0]);
      return 0;
    }
    bw.PutBit(1, probs[pBase + 0]);

    while (n < 16) {
      var c = coeffs[offset + n];
      ++n;
      var sign = c < 0 ? 1 : 0;
      var v = sign != 0 ? -c : c;
      if (v == 0) {
        bw.PutBit(0, probs[pBase + 1]);
        pBase = _TpIdx(plane, _Bands[n], 0, 0);
        continue;
      }
      bw.PutBit(1, probs[pBase + 1]);
      if (v == 1) {
        bw.PutBit(0, probs[pBase + 2]);
        pBase = _TpIdx(plane, _Bands[n], 1, 0);
      } else {
        bw.PutBit(1, probs[pBase + 2]);
        if (v <= 4) {
          bw.PutBit(0, probs[pBase + 3]);
          if (v == 2) {
            bw.PutBit(0, probs[pBase + 4]);
          } else {
            bw.PutBit(1, probs[pBase + 4]);
            bw.PutBit(v == 4 ? 1 : 0, probs[pBase + 5]);
          }
        } else if (v <= 10) {
          bw.PutBit(1, probs[pBase + 3]);
          bw.PutBit(0, probs[pBase + 6]);
          if (v <= 6) {
            bw.PutBit(0, probs[pBase + 7]);
            bw.PutBit(v == 6 ? 1 : 0, 159);
          } else {
            bw.PutBit(1, probs[pBase + 7]);
            bw.PutBit(v >= 9 ? 1 : 0, 165);
            bw.PutBit((v & 1) == 0 ? 1 : 0, 145);
          }
        } else {
          bw.PutBit(1, probs[pBase + 3]);
          bw.PutBit(1, probs[pBase + 6]);
          int mask;
          byte[] tab;
          if (v < 3 + (8 << 1)) {
            bw.PutBit(0, probs[pBase + 8]);
            bw.PutBit(0, probs[pBase + 9]);
            v -= 3 + (8 << 0);
            mask = 1 << 2;
            tab = _Cat3;
          } else if (v < 3 + (8 << 2)) {
            bw.PutBit(0, probs[pBase + 8]);
            bw.PutBit(1, probs[pBase + 9]);
            v -= 3 + (8 << 1);
            mask = 1 << 3;
            tab = _Cat4;
          } else if (v < 3 + (8 << 3)) {
            bw.PutBit(1, probs[pBase + 8]);
            bw.PutBit(0, probs[pBase + 10]);
            v -= 3 + (8 << 2);
            mask = 1 << 4;
            tab = _Cat5;
          } else {
            bw.PutBit(1, probs[pBase + 8]);
            bw.PutBit(1, probs[pBase + 10]);
            v -= 3 + (8 << 3);
            mask = 1 << 10;
            tab = _Cat6;
          }
          var tabIdx = 0;
          while (mask != 0) {
            bw.PutBit((v & mask) != 0 ? 1 : 0, tab[tabIdx++]);
            mask >>= 1;
          }
        }
        pBase = _TpIdx(plane, _Bands[n], 2, 0);
      }
      bw.PutBitUniform(sign);
      if (n == 16 || n > last) {
        if (n < 16) bw.PutBit(0, probs[pBase + 0]);
        return 1;
      }
      bw.PutBit(1, probs[pBase + 0]);
    }
    return 1;
  }

  /// <summary>Count-only variant of <see cref="_PutCoeffs"/>: walks the same token tree but,
  /// instead of emitting bits, increments stats for each (prob-index → bit-value) pair that
  /// WOULD be written. Only counts bits at the 11 per-context prob slots (pBase + 0..10) —
  /// category-table and sign bits are not subject to probability update.</summary>
  private byte _CountCoeffs(int plane, byte ctx, short[] coeffs, int offset, int firstCoeff) {
    var n = firstCoeff;
    var last = -1;
    for (var k = 15; k >= firstCoeff; --k) {
      if (coeffs[offset + k] != 0) { last = k; break; }
    }
    var band = _Bands[n];
    if (last < 0) { _tokenStats[plane, band, ctx, 0, 0]++; return 0; }
    _tokenStats[plane, band, ctx, 0, 1]++;

    var curBand = band;
    var curCtx = ctx;
    while (n < 16) {
      var c = coeffs[offset + n];
      ++n;
      var v = c < 0 ? -c : c;
      if (v == 0) {
        _tokenStats[plane, curBand, curCtx, 1, 0]++;
        curBand = _Bands[n];
        curCtx = 0;
        continue;
      }
      _tokenStats[plane, curBand, curCtx, 1, 1]++;
      if (v == 1) {
        _tokenStats[plane, curBand, curCtx, 2, 0]++;
        var newBand = _Bands[n];
        // Sign bit: not counted (uniform, not updatable).
        if (n == 16 || n > last) {
          if (n < 16) _tokenStats[plane, newBand, 1, 0, 0]++;
          return 1;
        }
        _tokenStats[plane, newBand, 1, 0, 1]++;
        curBand = newBand;
        curCtx = 1;
        continue;
      }
      _tokenStats[plane, curBand, curCtx, 2, 1]++;
      if (v <= 4) {
        _tokenStats[plane, curBand, curCtx, 3, 0]++;
        if (v == 2) {
          _tokenStats[plane, curBand, curCtx, 4, 0]++;
        } else {
          _tokenStats[plane, curBand, curCtx, 4, 1]++;
          _tokenStats[plane, curBand, curCtx, 5, v == 4 ? 1 : 0]++;
        }
      } else if (v <= 10) {
        _tokenStats[plane, curBand, curCtx, 3, 1]++;
        _tokenStats[plane, curBand, curCtx, 6, 0]++;
        if (v <= 6) {
          _tokenStats[plane, curBand, curCtx, 7, 0]++;
          // bit at prob=159 (hardcoded): not counted
        } else {
          _tokenStats[plane, curBand, curCtx, 7, 1]++;
          // bits at hardcoded 165, 145: not counted
        }
      } else {
        _tokenStats[plane, curBand, curCtx, 3, 1]++;
        _tokenStats[plane, curBand, curCtx, 6, 1]++;
        if (v < 3 + (8 << 1)) {
          _tokenStats[plane, curBand, curCtx, 8, 0]++;
          _tokenStats[plane, curBand, curCtx, 9, 0]++;
        } else if (v < 3 + (8 << 2)) {
          _tokenStats[plane, curBand, curCtx, 8, 0]++;
          _tokenStats[plane, curBand, curCtx, 9, 1]++;
        } else if (v < 3 + (8 << 3)) {
          _tokenStats[plane, curBand, curCtx, 8, 1]++;
          _tokenStats[plane, curBand, curCtx, 10, 0]++;
        } else {
          _tokenStats[plane, curBand, curCtx, 8, 1]++;
          _tokenStats[plane, curBand, curCtx, 10, 1]++;
        }
        // Category table bits: not counted.
      }
      // Sign bit: not counted (uniform).
      var nextBand = _Bands[n];
      if (n == 16 || n > last) {
        if (n < 16) _tokenStats[plane, nextBand, 2, 0, 0]++;
        return 1;
      }
      _tokenStats[plane, nextBand, 2, 0, 1]++;
      curBand = nextBand;
      curCtx = 2;
    }
    return 1;
  }

  private static int _TpIdx(int plane, int band, int ctx, int i) =>
    ((plane * NumBands + band) * NumCtx + ctx) * NumProbas + i;

  // §13.4 coefficient probability-update probabilities.
  private static readonly byte[] _CoeffUpdateProb = [
    255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,
    176,246,255,255,255,255,255,255,255,255,255,223,241,252,255,255,255,255,255,255,255,255,249,253,253,255,255,255,255,255,255,255,255,
    255,244,252,255,255,255,255,255,255,255,255,234,254,254,255,255,255,255,255,255,255,255,253,255,255,255,255,255,255,255,255,255,255,
    255,246,254,255,255,255,255,255,255,255,255,239,253,254,255,255,255,255,255,255,255,255,254,255,254,255,255,255,255,255,255,255,255,
    255,248,254,255,255,255,255,255,255,255,255,251,255,254,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,
    255,253,254,255,255,255,255,255,255,255,255,251,254,254,255,255,255,255,255,255,255,255,254,255,254,255,255,255,255,255,255,255,255,
    255,254,253,255,254,255,255,255,255,255,255,250,255,254,255,254,255,255,255,255,255,255,254,255,255,255,255,255,255,255,255,255,255,
    255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,

    217,255,255,255,255,255,255,255,255,255,255,225,252,241,253,255,255,254,255,255,255,255,234,250,241,250,253,255,253,254,255,255,255,
    255,254,255,255,255,255,255,255,255,255,255,223,254,254,255,255,255,255,255,255,255,255,238,253,254,254,255,255,255,255,255,255,255,
    255,248,254,255,255,255,255,255,255,255,255,249,254,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,
    255,253,255,255,255,255,255,255,255,255,255,247,254,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,
    255,253,254,255,255,255,255,255,255,255,255,252,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,
    255,254,254,255,255,255,255,255,255,255,255,253,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,
    255,254,253,255,255,255,255,255,255,255,255,250,255,255,255,255,255,255,255,255,255,255,254,255,255,255,255,255,255,255,255,255,255,
    255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,

    186,251,250,255,255,255,255,255,255,255,255,234,251,244,254,255,255,255,255,255,255,255,251,251,243,253,254,255,254,255,255,255,255,
    255,253,254,255,255,255,255,255,255,255,255,236,253,254,255,255,255,255,255,255,255,255,251,253,253,254,254,255,255,255,255,255,255,
    255,254,254,255,255,255,255,255,255,255,255,254,254,254,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,
    255,254,255,255,255,255,255,255,255,255,255,254,254,255,255,255,255,255,255,255,255,255,254,255,255,255,255,255,255,255,255,255,255,
    255,255,255,255,255,255,255,255,255,255,255,254,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,
    255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,
    255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,
    255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,

    248,255,255,255,255,255,255,255,255,255,255,250,254,252,254,255,255,255,255,255,255,255,248,254,249,253,255,255,255,255,255,255,255,
    255,253,253,255,255,255,255,255,255,255,255,246,253,253,255,255,255,255,255,255,255,255,252,254,251,254,254,255,255,255,255,255,255,
    255,254,252,255,255,255,255,255,255,255,255,248,254,253,255,255,255,255,255,255,255,255,253,255,254,254,255,255,255,255,255,255,255,
    255,251,254,255,255,255,255,255,255,255,255,245,251,254,255,255,255,255,255,255,255,255,253,253,254,255,255,255,255,255,255,255,255,
    255,251,253,255,255,255,255,255,255,255,255,252,253,254,255,255,255,255,255,255,255,255,255,254,255,255,255,255,255,255,255,255,255,
    255,252,255,255,255,255,255,255,255,255,255,249,255,254,255,255,255,255,255,255,255,255,255,255,254,255,255,255,255,255,255,255,255,
    255,255,253,255,255,255,255,255,255,255,255,250,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,
    255,255,255,255,255,255,255,255,255,255,255,254,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,255,
  ];
}
