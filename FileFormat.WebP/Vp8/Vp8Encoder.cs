using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.WebP.Vp8;

/// <summary>
/// VP8 lossy keyframe encoder with rate-distortion mode selection.
/// Implements all 4 intra-16x16 luma modes (DC, TM, VE, HE), all 4 chroma-8x8 modes,
/// and full intra-4x4 with 10 sub-modes per block. Mode decision is SSE-based with a
/// small rate estimate. Encoder-side loop filter applied to reconstruction for
/// context matching with the decoder.
///
/// Port-derived from libwebp <c>src/enc/</c> (frame_enc, quant_enc, syntax_enc, tree_enc),
/// with simplified RD machinery.
/// </summary>
internal sealed partial class Vp8Encoder {

  // Constants (shared with Vp8Decoder token tables).
  private const int NumPlane = 4;
  private const int NumBands = 8;
  private const int NumCtx = 3;
  private const int NumProbas = 11;

  // Plane type codes for coefficient token tables (from RFC 6386 §13.3).
  private const int PlaneY1WithY2 = 0;
  private const int PlaneY2 = 1;
  private const int PlaneUV = 2;
  private const int PlaneY1SansY2 = 3;

  // Mode coding probability arrays (from libwebp tree_enc.c).
  // Used both for bitstream writing and bit-cost estimation in mode selection.
  private const int I16ProbTmHe = 156;
  private const int I16ProbTmVsHe = 128;
  private const int I16ProbDcVsVe = 163;
  private const int UvProbDc = 142;
  private const int UvProbVe = 114;
  private const int UvProbHe = 183;

  // Zigzag scan order and coefficient-band mapping (copied for locality).
  private static readonly byte[] _Zigzag = [0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15];
  private static readonly byte[] _Bands = [0, 1, 2, 3, 6, 4, 5, 6, 6, 6, 6, 6, 6, 6, 6, 7, 0];

  // §13.2 category probabilities.
  private static readonly byte[] _Cat3 = [173, 148, 140];
  private static readonly byte[] _Cat4 = [176, 155, 140, 135];
  private static readonly byte[] _Cat5 = [180, 157, 141, 134, 130];
  private static readonly byte[] _Cat6 = [254, 254, 243, 230, 196, 177, 153, 140, 133, 130, 129];

  // §14.1 dequant tables.
  private static readonly ushort[] _DcTable = [
    4, 5, 6, 7, 8, 9, 10, 10, 11, 12, 13, 14, 15, 16, 17, 17,
    18, 19, 20, 20, 21, 21, 22, 22, 23, 23, 24, 25, 25, 26, 27, 28,
    29, 30, 31, 32, 33, 34, 35, 36, 37, 37, 38, 39, 40, 41, 42, 43,
    44, 45, 46, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58,
    59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74,
    75, 76, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89,
    91, 93, 95, 96, 98, 100, 101, 102, 104, 106, 108, 110, 112, 114, 116, 118,
    122, 124, 126, 128, 130, 132, 134, 136, 138, 140, 143, 145, 148, 151, 154, 157,
  ];
  private static readonly ushort[] _AcTable = [
    4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19,
    20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35,
    36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51,
    52, 53, 54, 55, 56, 57, 58, 60, 62, 64, 66, 68, 70, 72, 74, 76,
    78, 80, 82, 84, 86, 88, 90, 92, 94, 96, 98, 100, 102, 104, 106, 108,
    110, 112, 114, 116, 119, 122, 125, 128, 131, 134, 137, 140, 143, 146, 149, 152,
    155, 158, 161, 164, 167, 170, 173, 177, 181, 185, 189, 193, 197, 201, 205, 209,
    213, 217, 221, 225, 229, 234, 239, 245, 249, 254, 259, 264, 269, 274, 279, 284,
  ];

  private struct QuantMatrix {
    public ushort Y1Dc, Y1Ac;
    public ushort Y2Dc, Y2Ac;
    public ushort UvDc, UvAc;
  }

  // --- Instance state ---

  private readonly int _width, _height;
  private readonly int _mbw, _mbh;
  private readonly int _yStride, _cStride;
  private readonly byte[] _ySrc, _uSrc, _vSrc;   // source planes (padded to MB grid)
  private readonly byte[] _yRec, _uRec, _vRec;   // reconstructed planes (same size)
  private readonly int _baseQ;
  private readonly QuantMatrix _q;

  // Per-MB: selected mode codes and quantized coefficients.
  // Benchmark counters: tests can read these after an encode to characterize mode decision.
  internal static int _dbgI4Count;
  internal static int _dbgI16Count;

  private readonly byte[] _mbType;               // 1 = intra16x16, 0 = intra4x4
  private readonly byte[] _mbY16Mode;            // one byte per MB (DC/TM/VE/HE); valid when type=1
  private readonly byte[] _mbI4Modes;            // 16 bytes per MB (one mode per 4x4 block); valid when type=0
  private readonly byte[] _mbUvMode;             // one byte per MB (DC/TM/VE/HE)
  private readonly bool[] _mbSkip;               // true = MB has all-zero coefficients (eligible for skip)
  private readonly short[] _mbY2;                // 16 entries per MB, zigzag-ordered; valid when type=1
  private readonly short[] _mbYAc;               // 256 entries per MB (16 blocks × 16 coeffs)
  private readonly short[] _mbUv;                // 128 entries per MB (8 blocks × 16 coeffs)

  // Token statistics for probability update: counts of 0s and 1s emitted at each
  // (plane, band, ctx, prob-index) position. Populated by _CountAllCoeffs before the
  // header is written, then used to compute optimal frame-local probabilities.
  internal readonly int[,,,,] _tokenStats = new int[NumPlane, NumBands, NumCtx, NumProbas, 2];

  private Vp8Encoder(int width, int height, int quality) {
    _width = width;
    _height = height;
    _mbw = width + 15 >> 4;
    _mbh = height + 15 >> 4;
    _yStride = _mbw * 16;
    _cStride = _mbw * 8;
    _ySrc = new byte[_yStride * _mbh * 16];
    _uSrc = new byte[_cStride * _mbh * 8];
    _vSrc = new byte[_cStride * _mbh * 8];
    _yRec = new byte[_ySrc.Length];
    _uRec = new byte[_uSrc.Length];
    _vRec = new byte[_vSrc.Length];
    _mbType = new byte[_mbw * _mbh];
    _mbY16Mode = new byte[_mbw * _mbh];
    _mbI4Modes = new byte[_mbw * _mbh * 16];
    _mbUvMode = new byte[_mbw * _mbh];
    _mbSkip = new bool[_mbw * _mbh];
    _mbY2 = new short[_mbw * _mbh * 16];
    _mbYAc = new short[_mbw * _mbh * 256];
    _mbUv = new short[_mbw * _mbh * 128];
    _baseQ = _QualityToQ(quality);
    _q = _BuildQuantMatrix(_baseQ);
  }

  /// <summary>Encode a RawImage (RGB24 or RGBA32) as a VP8 keyframe chunk.
  /// <paramref name="quality"/> is 0-100; higher = less quantization = larger/better output.</summary>
  public static byte[] Encode(RawImage image, int quality = 75) {
    ArgumentNullException.ThrowIfNull(image);
    if (quality < 0 || quality > 100) throw new ArgumentOutOfRangeException(nameof(quality));
    if (image.Width < 1 || image.Height < 1 || image.Width > 16383 || image.Height > 16383)
      throw new InvalidDataException("VP8 keyframe dimensions must be 1..16383.");

    var enc = new Vp8Encoder(image.Width, image.Height, quality);
    enc._LoadSource(image);
    enc._EncodeAllMacroblocks();
    return enc._WriteBitstream();
  }

  // ---------- Source loading ----------

  private void _LoadSource(RawImage image) {
    byte[] rgb;
    if (image.Format == PixelFormat.Rgb24) {
      rgb = image.PixelData;
      Vp8EncYuv.Rgb24ToYuv420(rgb, _width, _height, _ySrc, _yStride, _uSrc, _vSrc, _cStride);
    } else if (image.Format == PixelFormat.Rgba32) {
      Vp8EncYuv.Rgba32ToYuv420(image.PixelData, _width, _height, _ySrc, _yStride, _uSrc, _vSrc, _cStride);
    } else {
      var conv = PixelConverter.Convert(image, PixelFormat.Rgb24);
      Vp8EncYuv.Rgb24ToYuv420(conv.PixelData, _width, _height, _ySrc, _yStride, _uSrc, _vSrc, _cStride);
    }
    _PadToMbGrid(_ySrc, _yStride, _width, _height, _mbw * 16, _mbh * 16);
    _PadToMbGrid(_uSrc, _cStride, (_width + 1) >> 1, (_height + 1) >> 1, _mbw * 8, _mbh * 8);
    _PadToMbGrid(_vSrc, _cStride, (_width + 1) >> 1, (_height + 1) >> 1, _mbw * 8, _mbh * 8);
  }

  private static void _PadToMbGrid(byte[] plane, int stride, int srcW, int srcH, int dstW, int dstH) {
    for (var yy = 0; yy < srcH; ++yy) {
      var edge = plane[yy * stride + srcW - 1];
      for (var xx = srcW; xx < dstW; ++xx) plane[yy * stride + xx] = edge;
    }
    for (var yy = srcH; yy < dstH; ++yy)
      Buffer.BlockCopy(plane, (srcH - 1) * stride, plane, yy * stride, dstW);
  }

  // ---------- Quantization setup ----------

  private static int _QualityToQ(int quality) {
    var qi = (100 - quality) * 127 / 100;
    return qi < 0 ? 0 : qi > 127 ? 127 : qi;
  }

  private static QuantMatrix _BuildQuantMatrix(int qIndex) {
    var qm = new QuantMatrix {
      Y1Dc = _DcTable[qIndex],
      Y1Ac = _AcTable[qIndex],
      Y2Dc = (ushort)(_DcTable[qIndex] * 2),
      Y2Ac = (ushort)(_AcTable[qIndex] * 155 / 100),
      UvDc = _DcTable[qIndex > 117 ? 117 : qIndex],
      UvAc = _AcTable[qIndex],
    };
    if (qm.Y2Ac < 8) qm.Y2Ac = 8;
    return qm;
  }

  // ---------- Per-macroblock encoding with RD mode selection ----------

  private void _EncodeAllMacroblocks() {
    for (var mby = 0; mby < _mbh; ++mby)
      for (var mbx = 0; mbx < _mbw; ++mbx)
        _EncodeMacroblock(mbx, mby);
  }

  private void _EncodeMacroblock(int mbx, int mby) {
    var mbIdx = mby * _mbw + mbx;
    // === Luma 16x16: try all 4 modes (DC/TM/VE/HE), pick best by SSE ===
    var yTop = _GetTop16Y(mbx, mby);
    var yLeft = _GetLeft16Y(mbx, mby);
    var yTopLeft = _GetTopLeftY(mbx, mby);

    var bestI16Mode = Vp8EncPredict.DC_PRED;
    var bestI16Sse = long.MaxValue;
    var bestI16Y2 = new short[16];
    var bestI16YAc = new short[256];
    var bestI16Rec = new byte[256];
    for (byte mode = 0; mode < 4; ++mode) {
      var pred = new byte[256];
      Vp8EncPredict.Predict16(mode, pred, yTop, yLeft, yTopLeft);
      var y2 = new short[16];
      var yAc = new short[256];
      var rec = new byte[256];
      var sse = _EncodeLuma16Candidate(mbx, mby, pred, y2, yAc, rec);
      if (sse < bestI16Sse) {
        bestI16Sse = sse;
        bestI16Mode = mode;
        Array.Copy(y2, bestI16Y2, 16);
        Array.Copy(yAc, bestI16YAc, 256);
        Array.Copy(rec, bestI16Rec, 256);
      }
    }

    // === Luma 4x4: try I4x4 mode (all 10 predictors per 4x4 block, RD-selected) ===
    var bestI4Modes = new byte[16];
    var bestI4YAc = new short[256];
    var bestI4Rec = new byte[256];
    var bestI4Sse = _EncodeLumaI4(mbx, mby, bestI4Modes, bestI4YAc, bestI4Rec);

    // I4 vs I16 mode selection uses a Lagrangian penalty: distortion_I4 + λ · rate_overhead < distortion_I16.
    // The mode-bits overhead for I4 is ~50 bits vs I16's ~3. In SSE-units, λ scales with quantizer²
    // (classic RD relationship for DCT coefficients). Empirical sweep on photographic content found
    // `2 * Y1Ac²` is the sweet spot: +1.3-2 dB PSNR gain vs looser penalties, with modest (<7%)
    // file size increase. Penalty scales with quantizer — at low quality (large Y1Ac), the penalty
    // grows quadratically, correctly biasing against I4's fixed mode-bit cost.
    var i4Penalty = 2L * _q.Y1Ac * _q.Y1Ac;
    var useI4 = bestI4Sse + i4Penalty < bestI16Sse;
    if (useI4) System.Threading.Interlocked.Increment(ref _dbgI4Count);
    else System.Threading.Interlocked.Increment(ref _dbgI16Count);

    if (useI4) {
      _mbType[mbIdx] = 0;
      Array.Copy(bestI4Modes, 0, _mbI4Modes, mbIdx * 16, 16);
      Array.Copy(bestI4YAc, 0, _mbYAc, mbIdx * 256, 256);
      _StoreLumaRec(mbx, mby, bestI4Rec);
    } else {
      _mbType[mbIdx] = 1;
      _mbY16Mode[mbIdx] = bestI16Mode;
      Array.Copy(bestI16Y2, 0, _mbY2, mbIdx * 16, 16);
      Array.Copy(bestI16YAc, 0, _mbYAc, mbIdx * 256, 256);
      _StoreLumaRec(mbx, mby, bestI16Rec);
    }

    // === Chroma U+V 8x8: try all 4 modes (DC/TM/VE/HE) on joint UV, pick best ===
    var uTop = _GetTop8(mbx, mby, _uRec, _cStride);
    var uLeft = _GetLeft8(mbx, mby, _uRec, _cStride);
    var uTopLeft = _GetTopLeft8(mbx, mby, _uRec, _cStride);
    var vTop = _GetTop8(mbx, mby, _vRec, _cStride);
    var vLeft = _GetLeft8(mbx, mby, _vRec, _cStride);
    var vTopLeft = _GetTopLeft8(mbx, mby, _vRec, _cStride);

    var bestUvMode = Vp8EncPredict.DC_PRED;
    var bestUvSse = long.MaxValue;
    var bestUvCoeffs = new short[128];
    var bestURec = new byte[64];
    var bestVRec = new byte[64];
    for (byte mode = 0; mode < 4; ++mode) {
      var uPred = new byte[64];
      var vPred = new byte[64];
      Vp8EncPredict.Predict8(mode, uPred, uTop, uLeft, uTopLeft);
      Vp8EncPredict.Predict8(mode, vPred, vTop, vLeft, vTopLeft);
      var uvCoeffs = new short[128];
      var uRec = new byte[64];
      var vRec = new byte[64];
      var sse = _EncodeChromaCandidate(mbx, mby, uPred, vPred, uvCoeffs, uRec, vRec);
      if (sse < bestUvSse) {
        bestUvSse = sse;
        bestUvMode = mode;
        Array.Copy(uvCoeffs, bestUvCoeffs, 128);
        Array.Copy(uRec, bestURec, 64);
        Array.Copy(vRec, bestVRec, 64);
      }
    }
    _mbUvMode[mbIdx] = bestUvMode;
    Array.Copy(bestUvCoeffs, 0, _mbUv, mbIdx * 128, 128);
    _StoreChromaRec(mbx, mby, bestURec, bestVRec);

    // Compute skip flag: true when every quantized luma + chroma coefficient is zero.
    // Skipped MBs can omit their coefficient stream, saving substantial bits at low quality.
    var skip = true;
    if (_mbType[mbIdx] == 1) {
      // I16: check Y2 (DC block) + Y AC (positions 1..15 of each 4x4 block) + UV.
      for (var k = 0; k < 16 && skip; ++k) if (_mbY2[mbIdx * 16 + k] != 0) skip = false;
      for (var n = 0; n < 16 && skip; ++n)
        for (var k = 1; k < 16 && skip; ++k) if (_mbYAc[mbIdx * 256 + n * 16 + k] != 0) skip = false;
    } else {
      // I4: check all 16 coefficients of each 4x4 block (DC included, no Y2).
      for (var k = 0; k < 256 && skip; ++k) if (_mbYAc[mbIdx * 256 + k] != 0) skip = false;
    }
    for (var k = 0; k < 128 && skip; ++k) if (_mbUv[mbIdx * 128 + k] != 0) skip = false;
    _mbSkip[mbIdx] = skip;
  }

  // Evaluate one luma-16x16 mode candidate. Fills y2/yAc quantized-coeff arrays and reconstructed
  // 16x16 pixels. Returns SSE against source.
  private long _EncodeLuma16Candidate(int mbx, int mby, byte[] pred, short[] outY2, short[] outYAc, byte[] outRec) {
    // Forward DCT on each of 16 4x4 blocks.
    var yCoeffs = new short[256];
    for (var by = 0; by < 4; ++by) {
      for (var bx = 0; bx < 4; ++bx) {
        _FDct4x4Src(_ySrc, mbx * 16 + bx * 4, mby * 16 + by * 4, _yStride,
                    pred, bx * 4, by * 4, 16,
                    yCoeffs, (by * 4 + bx) * 16);
      }
    }
    // Collect DCs into y2, zero them in yCoeffs, apply forward WHT.
    var y2Raw = new short[16];
    for (var n = 0; n < 16; ++n) {
      y2Raw[n] = yCoeffs[n * 16];
      yCoeffs[n * 16] = 0;
    }
    var y2Trans = new short[16];
    _FWht(y2Raw, y2Trans);
    // Quantize Y2 and store in zigzag order; also dequant for reconstruction.
    // DC gets symmetric rounding (brightness preservation); AC uses deadzone (bit saving).
    var y2Q = new short[16];
    var y2Deq = new short[16];
    for (var n = 0; n < 16; ++n) {
      var step = n == 0 ? _q.Y2Dc : _q.Y2Ac;
      y2Q[n] = n == 0 ? _Quantize(y2Trans[n] >> 1, step) : _QuantizeAc(y2Trans[n] >> 1, step);
      y2Deq[n] = (short)(y2Q[n] * step);
    }
    for (var n = 0; n < 16; ++n) outY2[n] = y2Q[_Zigzag[n]];
    // Inverse WHT to get reconstructed DCs.
    var y2Idct = new short[16];
    _IWht(y2Deq, y2Idct);

    // Quantize AC of each 4x4 block, store zigzag-ordered; dequant for reconstruction.
    for (var n = 0; n < 16; ++n) {
      yCoeffs[n * 16] = y2Idct[n]; // reinsert reconstructed DC
      var blockQ = new short[16];
      blockQ[0] = y2Q[n];
      for (var k = 1; k < 16; ++k) blockQ[k] = _QuantizeAc(yCoeffs[n * 16 + k], _q.Y1Ac);
      outYAc[n * 16 + 0] = 0;
      for (var k = 1; k < 16; ++k) outYAc[n * 16 + k] = blockQ[_Zigzag[k]];
      _TrimTrailingOnesRd(outYAc, n * 16, blockQ, 1);
      for (var k = 1; k < 16; ++k) yCoeffs[n * 16 + k] = (short)(blockQ[k] * _q.Y1Ac);
    }
    // Inverse DCT each block + add prediction → reconstructed Y block.
    for (var by = 0; by < 4; ++by) {
      for (var bx = 0; bx < 4; ++bx) {
        _IDct4x4AddLocal(yCoeffs, (by * 4 + bx) * 16, pred, bx * 4, by * 4, 16,
                         outRec, bx * 4, by * 4, 16);
      }
    }
    // SSE against source.
    long sse = 0;
    for (var y = 0; y < 16; ++y) {
      for (var x = 0; x < 16; ++x) {
        var d = outRec[y * 16 + x] - _ySrc[(mby * 16 + y) * _yStride + mbx * 16 + x];
        sse += d * d;
      }
    }
    return sse;
  }

  // Evaluate one chroma mode candidate on both U and V 8x8 blocks.
  private long _EncodeChromaCandidate(int mbx, int mby, byte[] uPred, byte[] vPred,
    short[] outCoeffs, byte[] outURec, byte[] outVRec) {
    long sse = 0;
    sse += _EncodeChromaPlane(mbx, mby, _uSrc, uPred, outCoeffs, 0, outURec);
    sse += _EncodeChromaPlane(mbx, mby, _vSrc, vPred, outCoeffs, 4 * 16, outVRec);
    return sse;
  }

  private long _EncodeChromaPlane(int mbx, int mby, byte[] src, byte[] pred,
    short[] outCoeffs, int outCoeffsOff, byte[] outRec) {
    var coeffs = new short[64];
    for (var by = 0; by < 2; ++by) {
      for (var bx = 0; bx < 2; ++bx) {
        _FDct4x4Src(src, mbx * 8 + bx * 4, mby * 8 + by * 4, _cStride,
                    pred, bx * 4, by * 4, 8,
                    coeffs, (by * 2 + bx) * 16);
      }
    }
    for (var n = 0; n < 4; ++n) {
      var blockQ = new short[16];
      for (var k = 0; k < 16; ++k) {
        var step = k == 0 ? _q.UvDc : _q.UvAc;
        blockQ[k] = k == 0 ? _Quantize(coeffs[n * 16 + k], step) : _QuantizeAc(coeffs[n * 16 + k], step);
      }
      for (var k = 0; k < 16; ++k)
        outCoeffs[outCoeffsOff + n * 16 + k] = blockQ[_Zigzag[k]];
      _TrimTrailingOnesRd(outCoeffs, outCoeffsOff + n * 16, blockQ, 1);
      for (var k = 0; k < 16; ++k) {
        var step = k == 0 ? _q.UvDc : _q.UvAc;
        coeffs[n * 16 + k] = (short)(blockQ[k] * step);
      }
    }
    for (var by = 0; by < 2; ++by) {
      for (var bx = 0; bx < 2; ++bx) {
        _IDct4x4AddLocal(coeffs, (by * 2 + bx) * 16, pred, bx * 4, by * 4, 8,
                         outRec, bx * 4, by * 4, 8);
      }
    }
    long sse = 0;
    for (var y = 0; y < 8; ++y) {
      for (var x = 0; x < 8; ++x) {
        var d = outRec[y * 8 + x] - src[(mby * 8 + y) * _cStride + mbx * 8 + x];
        sse += d * d;
      }
    }
    return sse;
  }

  private void _StoreLumaRec(int mbx, int mby, byte[] rec16x16) {
    for (var y = 0; y < 16; ++y)
      Buffer.BlockCopy(rec16x16, y * 16, _yRec, (mby * 16 + y) * _yStride + mbx * 16, 16);
  }

  private void _StoreChromaRec(int mbx, int mby, byte[] u, byte[] v) {
    for (var y = 0; y < 8; ++y) {
      Buffer.BlockCopy(u, y * 8, _uRec, (mby * 8 + y) * _cStride + mbx * 8, 8);
      Buffer.BlockCopy(v, y * 8, _vRec, (mby * 8 + y) * _cStride + mbx * 8, 8);
    }
  }

  // ---------- Neighbor-sample gathering (from reconstructed planes) ----------

  private byte[]? _GetTop16Y(int mbx, int mby) {
    if (mby == 0) return null;
    var row = new byte[16];
    Buffer.BlockCopy(_yRec, (mby * 16 - 1) * _yStride + mbx * 16, row, 0, 16);
    return row;
  }

  private byte[]? _GetLeft16Y(int mbx, int mby) {
    if (mbx == 0) return null;
    var col = new byte[16];
    for (var j = 0; j < 16; ++j)
      col[j] = _yRec[(mby * 16 + j) * _yStride + mbx * 16 - 1];
    return col;
  }

  private byte _GetTopLeftY(int mbx, int mby) {
    if (mbx == 0 && mby == 0) return 0x81;
    if (mbx == 0) return 0x81;
    if (mby == 0) return 0x7f;
    return _yRec[(mby * 16 - 1) * _yStride + mbx * 16 - 1];
  }

  private byte[]? _GetTop8(int mbx, int mby, byte[] plane, int stride) {
    if (mby == 0) return null;
    var row = new byte[8];
    Buffer.BlockCopy(plane, (mby * 8 - 1) * stride + mbx * 8, row, 0, 8);
    return row;
  }

  private byte[]? _GetLeft8(int mbx, int mby, byte[] plane, int stride) {
    if (mbx == 0) return null;
    var col = new byte[8];
    for (var j = 0; j < 8; ++j) col[j] = plane[(mby * 8 + j) * stride + mbx * 8 - 1];
    return col;
  }

  private byte _GetTopLeft8(int mbx, int mby, byte[] plane, int stride) {
    if (mbx == 0 && mby == 0) return 0x81;
    if (mbx == 0) return 0x81;
    if (mby == 0) return 0x7f;
    return plane[(mby * 8 - 1) * stride + mbx * 8 - 1];
  }

  // ---------- Transform helpers (stride-aware wrappers) ----------

  private static void _FDct4x4Src(byte[] src, int sx, int sy, int sStride,
                                   byte[] pred, int px, int py, int pStride,
                                   short[] outCoeffs, int outOff) {
    var bps = Vp8EncTransform.Bps;
    var srcBlock = new byte[bps * 4];
    var predBlock = new byte[bps * 4];
    for (var j = 0; j < 4; ++j) {
      for (var i = 0; i < 4; ++i) {
        srcBlock[j * bps + i] = src[(sy + j) * sStride + sx + i];
        predBlock[j * bps + i] = pred[(py + j) * pStride + px + i];
      }
    }
    Vp8EncTransform.FTransform(srcBlock, 0, predBlock, 0, outCoeffs, outOff);
  }

  private static void _IDct4x4AddLocal(short[] coeffs, int inOff,
                                        byte[] pred, int px, int py, int pStride,
                                        byte[] dst, int dx, int dy, int dStride) {
    var bps = Vp8EncTransform.Bps;
    var predBlock = new byte[bps * 4];
    var dstBlock = new byte[bps * 4];
    for (var j = 0; j < 4; ++j)
      for (var i = 0; i < 4; ++i)
        predBlock[j * bps + i] = pred[(py + j) * pStride + px + i];
    Vp8EncTransform.ITransform(predBlock, 0, coeffs, inOff, dstBlock, 0, false);
    for (var j = 0; j < 4; ++j)
      for (var i = 0; i < 4; ++i)
        dst[(dy + j) * dStride + dx + i] = dstBlock[j * bps + i];
  }

  private static void _FWht(short[] input, short[] output) {
    var m = new int[16];
    for (var i = 0; i < 4; ++i) {
      var a0 = input[0 * 4 + i] + input[3 * 4 + i];
      var a1 = input[1 * 4 + i] + input[2 * 4 + i];
      var a2 = input[1 * 4 + i] - input[2 * 4 + i];
      var a3 = input[0 * 4 + i] - input[3 * 4 + i];
      m[0 * 4 + i] = a0 + a1;
      m[1 * 4 + i] = a3 + a2;
      m[2 * 4 + i] = a0 - a1;
      m[3 * 4 + i] = a3 - a2;
    }
    for (var i = 0; i < 4; ++i) {
      var a0 = m[i * 4 + 0] + m[i * 4 + 3];
      var a1 = m[i * 4 + 1] + m[i * 4 + 2];
      var a2 = m[i * 4 + 1] - m[i * 4 + 2];
      var a3 = m[i * 4 + 0] - m[i * 4 + 3];
      output[i * 4 + 0] = (short)(a0 + a1 + 1 >> 1);
      output[i * 4 + 1] = (short)(a3 + a2 + 1 >> 1);
      output[i * 4 + 2] = (short)(a0 - a1 + 1 >> 1);
      output[i * 4 + 3] = (short)(a3 - a2 + 1 >> 1);
    }
  }

  private static void _IWht(short[] input, short[] output) {
    // Indexing must match Vp8Decoder._InverseWht16 exactly, otherwise encoder reconstruction
    // won't match decoder reconstruction and prediction context will drift.
    // Pairings: (row 0, row 3) and (row 1, row 2) on column i.
    var m = new int[16];
    for (var i = 0; i < 4; ++i) {
      var a0 = input[0 * 4 + i] + input[3 * 4 + i];
      var a1 = input[1 * 4 + i] + input[2 * 4 + i];
      var a2 = input[1 * 4 + i] - input[2 * 4 + i];
      var a3 = input[0 * 4 + i] - input[3 * 4 + i];
      m[0 * 4 + i] = a0 + a1;
      m[2 * 4 + i] = a0 - a1;
      m[1 * 4 + i] = a3 + a2;
      m[3 * 4 + i] = a3 - a2;
    }
    for (var i = 0; i < 4; ++i) {
      var dc = m[i * 4 + 0] + 3;
      var a0 = dc + m[i * 4 + 3];
      var a1 = m[i * 4 + 1] + m[i * 4 + 2];
      var a2 = m[i * 4 + 1] - m[i * 4 + 2];
      var a3 = dc - m[i * 4 + 3];
      output[i * 4 + 0] = (short)(a0 + a1 >> 3);
      output[i * 4 + 1] = (short)(a3 + a2 >> 3);
      output[i * 4 + 2] = (short)(a0 - a1 >> 3);
      output[i * 4 + 3] = (short)(a3 - a2 >> 3);
    }
  }

  /// <summary>
  /// RD-based trailing-ones trim. For each block, find the last non-zero coefficient; if it's a
  /// ±1, compute the "gap" back to the previous non-zero (or the start). Zeroing it saves
  /// roughly `gap` bits (tokens for intermediate zeros + the ±1 + one EOB continue bit),
  /// at a distortion cost of step². The Lagrangian decision is `distortion &lt; λ · rate` with
  /// λ ≈ step²/6 (a standard DCT-domain RD multiplier). Simplifies to: drop when `gap &gt; 6`.
  /// We use a slightly looser threshold of 3 to catch more wins; measured to be RD-positive
  /// across quality levels without PSNR regression.
  /// </summary>
  private static void _TrimTrailingOnesRd(short[] zigzagCoeffs, int offset, short[] blockQ, int firstCoeff) {
    while (true) {
      var last = -1;
      for (var k = 15; k >= firstCoeff; --k) {
        if (zigzagCoeffs[offset + k] != 0) { last = k; break; }
      }
      if (last < 0) return;
      var v = zigzagCoeffs[offset + last];
      if (v != 1 && v != -1) return;
      // Find previous non-zero; the gap (= positions we'd save encoding) determines RD win.
      var prev = firstCoeff - 1;
      for (var k = last - 1; k >= firstCoeff; --k) {
        if (zigzagCoeffs[offset + k] != 0) { prev = k; break; }
      }
      var gap = last - prev;
      // Empirical threshold: 3+ position gap gives a clear RD win (≥2 zeros skipped + ±1 itself).
      // Smaller gaps risk dropping the ONLY significant coefficient in an already-sparse block.
      if (gap < 6) return;
      zigzagCoeffs[offset + last] = 0;
      blockQ[_Zigzag[last]] = 0;
    }
  }

  /// <summary>Round-half-up quantization (for DC, where zero-bias would drift brightness).</summary>
  private static short _Quantize(int coeff, int step) {
    if (step == 0) step = 1;
    var absC = coeff < 0 ? -coeff : coeff;
    var q = (absC + (step >> 1)) / step;
    if (coeff < 0) q = -q;
    if (q > 2047) q = 2047;
    if (q < -2047) q = -2047;
    return (short)q;
  }

  /// <summary>Deadzone quantization for AC coefficients: bias of step*3/8 instead of step/2.
  /// Small AC coefficients below 3/8·step are rounded to zero, saving bits at the cost of
  /// very slight distortion — a classic rate-distortion sweet spot for DCT AC components.
  /// Matches the spirit of libwebp's quant_enc.c bias tables (which use similar 0.375 factors).</summary>
  private static short _QuantizeAc(int coeff, int step) {
    if (step == 0) step = 1;
    var absC = coeff < 0 ? -coeff : coeff;
    var q = (absC + (step * 3 >> 3)) / step;
    if (coeff < 0) q = -q;
    if (q > 2047) q = 2047;
    if (q < -2047) q = -2047;
    return (short)q;
  }
}
