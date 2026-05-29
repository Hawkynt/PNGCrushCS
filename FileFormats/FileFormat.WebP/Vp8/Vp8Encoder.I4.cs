using System;

namespace FileFormat.WebP.Vp8;

// Intra-4x4 luma encoding. For each of 16 4x4 luma blocks in raster order within the MB,
// evaluates all 10 prediction modes (RFC 6386 §12.3), picks the lowest-SSE one, then
// performs FDCT → quantize → dequant → IDCT → reconstruct so subsequent blocks within
// the same MB have valid neighbor samples. No Y2 transform (each block has its own DC).
// Coefficients are stored in zigzag order with DC at position 0 (firstCoeff=0 scheme,
// encoded as PlaneY1SansY2 in the bitstream).
internal sealed partial class Vp8Encoder {

  /// <summary>Try the I4x4 mode: evaluate 10 predictors per 4x4 block, pick per-block winner.
  /// Returns total SSE of reconstructed MB vs source.</summary>
  private long _EncodeLumaI4(int mbx, int mby, byte[] outModes, short[] outYAc256, byte[] outRec16x16) {
    // Local reconstruction buffer for this MB: 16×16 with padding for neighbor access.
    // Layout: row 0 = top row (17 bytes wide for top-right extension of rightmost column of blocks).
    // Rows 1..16 = the MB itself, with col 0 holding the left column from neighbor MBs.
    // Conceptually: rec[y, x] where y ∈ [-1, 16), x ∈ [-1, 23).
    // Physical layout: a 17×24 byte buffer with rec[y+1, x+1].
    const int rowStride = 24;
    const int rowCount = 17;
    var pad = new byte[rowStride * rowCount];
    Array.Fill(pad, (byte)127);
    _FillI4Boundary(pad, mbx, mby);

    long totalSse = 0;
    var coeffs4 = new short[16];

    for (var by = 0; by < 4; ++by) {
      for (var bx = 0; bx < 4; ++bx) {
        // Gather neighbor samples for this 4x4 block from the padded local buffer.
        var top = new byte[8];
        var left = new byte[4];
        var topLeft = pad[_PadIdx(by * 4 - 1, bx * 4 - 1, rowStride)];
        for (var i = 0; i < 8; ++i) top[i] = pad[_PadIdx(by * 4 - 1, bx * 4 + i, rowStride)];
        for (var j = 0; j < 4; ++j) left[j] = pad[_PadIdx(by * 4 + j, bx * 4 - 1, rowStride)];

        // Try all 10 modes; keep the best by SSE.
        var bestMode = Vp8EncPredict.B_DC_PRED;
        var bestSse = long.MaxValue;
        var bestPred = new byte[16];
        var bestRec = new byte[16];
        var bestCoeffs = new short[16];
        for (byte mode = 0; mode < 10; ++mode) {
          var pred = new byte[16];
          Vp8EncPredict.Predict4(mode, pred, top, left, topLeft);
          var rec = new byte[16];
          var coeffsTry = new short[16];
          var sse = _EncodeI4Block(mbx, mby, bx, by, pred, coeffsTry, rec);
          if (sse < bestSse) {
            bestSse = sse;
            bestMode = mode;
            Array.Copy(pred, bestPred, 16);
            Array.Copy(rec, bestRec, 16);
            Array.Copy(coeffsTry, bestCoeffs, 16);
          }
        }

        // Commit winner: store mode and zigzag-ordered quantized coefficients.
        outModes[by * 4 + bx] = bestMode;
        for (var k = 0; k < 16; ++k)
          outYAc256[(by * 4 + bx) * 16 + k] = bestCoeffs[k];
        totalSse += bestSse;

        // Write reconstructed block into padded buffer for subsequent blocks' neighbors.
        for (var j = 0; j < 4; ++j)
          for (var i = 0; i < 4; ++i)
            pad[_PadIdx(by * 4 + j, bx * 4 + i, rowStride)] = bestRec[j * 4 + i];

        // Copy to final output MB reconstruction.
        for (var j = 0; j < 4; ++j)
          for (var i = 0; i < 4; ++i)
            outRec16x16[(by * 4 + j) * 16 + bx * 4 + i] = bestRec[j * 4 + i];
      }
    }
    return totalSse;
  }

  /// <summary>Encode one 4x4 block with a given prediction: FDCT, quantize (zigzag),
  /// dequant, IDCT, reconstruct. Returns SSE vs source.</summary>
  private long _EncodeI4Block(int mbx, int mby, int bx, int by, byte[] pred,
    short[] outCoeffsZigzag, byte[] outRec) {
    var bps = Vp8EncTransform.Bps;
    var srcBlock = new byte[bps * 4];
    var predBlock = new byte[bps * 4];
    var srcX = mbx * 16 + bx * 4;
    var srcY = mby * 16 + by * 4;
    for (var j = 0; j < 4; ++j) {
      for (var i = 0; i < 4; ++i) {
        srcBlock[j * bps + i] = _ySrc[(srcY + j) * _yStride + srcX + i];
        predBlock[j * bps + i] = pred[j * 4 + i];
      }
    }
    var raw = new short[16];
    Vp8EncTransform.FTransform(srcBlock, 0, predBlock, 0, raw, 0);

    // Quantize: DC with Y1Dc (symmetric), AC with Y1Ac (deadzone for bit savings).
    var blockQ = new short[16];
    for (var k = 0; k < 16; ++k) {
      var step = k == 0 ? _q.Y1Dc : _q.Y1Ac;
      blockQ[k] = k == 0 ? _Quantize(raw[k], step) : _QuantizeAc(raw[k], step);
    }
    // Store in zigzag order (including DC at position 0).
    for (var k = 0; k < 16; ++k)
      outCoeffsZigzag[k] = blockQ[_Zigzag[k]];
    // RD-based trailing ±1 trim (preserve DC at zigzag[0] by passing firstCoeff=1).
    _TrimTrailingOnesRd(outCoeffsZigzag, 0, blockQ, 1);

    // Dequantize in raw positions for reconstruction.
    var deq = new short[16];
    for (var k = 0; k < 16; ++k) {
      var step = k == 0 ? _q.Y1Dc : _q.Y1Ac;
      deq[k] = (short)(blockQ[k] * step);
    }
    // IDCT + add prediction.
    var dstBlock = new byte[bps * 4];
    Vp8EncTransform.ITransform(predBlock, 0, deq, 0, dstBlock, 0, false);
    // Unpack from BPS-stride to tight 4x4.
    for (var j = 0; j < 4; ++j)
      for (var i = 0; i < 4; ++i)
        outRec[j * 4 + i] = dstBlock[j * bps + i];

    // SSE vs source.
    long sse = 0;
    for (var j = 0; j < 4; ++j) {
      for (var i = 0; i < 4; ++i) {
        var d = outRec[j * 4 + i] - _ySrc[(srcY + j) * _yStride + srcX + i];
        sse += d * d;
      }
    }
    return sse;
  }

  /// <summary>Fill the boundary (top row + left column) of the padded I4x4 work buffer
  /// from the persistent reconstruction in <see cref="_yRec"/>, falling back to spec
  /// defaults (127 for top-absent, 129 for left-absent) at image edges.</summary>
  private void _FillI4Boundary(byte[] pad, int mbx, int mby) {
    const int rowStride = 24;

    // Top row (row 0 of pad): 17 samples covering x ∈ [-1, 16).
    // For topRight extension at x ∈ [16, 24): pull from MB at (mbx+1, mby-1) if it exists.
    if (mby > 0) {
      for (var i = 0; i < 16; ++i)
        pad[_PadIdx(-1, i, rowStride)] = _yRec[(mby * 16 - 1) * _yStride + mbx * 16 + i];
      for (var i = 16; i < 24; ++i) {
        var srcX = mbx * 16 + i;
        if (srcX < _yStride)
          pad[_PadIdx(-1, i, rowStride)] = _yRec[(mby * 16 - 1) * _yStride + srcX];
        else
          pad[_PadIdx(-1, i, rowStride)] = _yRec[(mby * 16 - 1) * _yStride + _yStride - 1];
      }
    } else {
      for (var i = -1; i < 23; ++i) pad[_PadIdx(-1, i, rowStride)] = 127;
    }

    // Top-left corner (row 0 col 0 of pad, representing x=-1 y=-1).
    if (mbx > 0 && mby > 0)
      pad[_PadIdx(-1, -1, rowStride)] = _yRec[(mby * 16 - 1) * _yStride + mbx * 16 - 1];
    else if (mbx > 0) // no top, left exists → still 127 per spec (top absent wins)
      pad[_PadIdx(-1, -1, rowStride)] = 127;
    else if (mby > 0) // no left, top exists → 129 per spec (left absent wins)
      pad[_PadIdx(-1, -1, rowStride)] = 129;
    else
      pad[_PadIdx(-1, -1, rowStride)] = 127;

    // Left column (col 0 of rows 1..16): 16 samples from MB to the left.
    if (mbx > 0) {
      for (var j = 0; j < 16; ++j)
        pad[_PadIdx(j, -1, rowStride)] = _yRec[(mby * 16 + j) * _yStride + mbx * 16 - 1];
    } else {
      for (var j = 0; j < 16; ++j) pad[_PadIdx(j, -1, rowStride)] = 129;
    }

    // Replicate top-right extension (x=16..19) to internal rows that the rightmost
    // 4x4 blocks' top-row reads. Decoder does the equivalent in _PrepareYBR (§14.4 via
    // ybr[4/8/12][24..27] fills). Without this, blocks (bx=3, by>0) predict with
    // uninitialized samples for LD/VL modes, breaking encoder/decoder reconstruction parity.
    for (var y = 3; y < 16; y += 4) {
      for (var i = 16; i < 20; ++i)
        pad[_PadIdx(y, i, rowStride)] = pad[_PadIdx(-1, i, rowStride)];
    }
  }

  private static int _PadIdx(int y, int x, int rowStride) => (y + 1) * rowStride + (x + 1);
}
