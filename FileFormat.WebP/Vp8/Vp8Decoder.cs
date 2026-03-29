using System;

namespace FileFormat.WebP.Vp8;

/// <summary>VP8 keyframe decoder: decodes VP8 lossy bitstream data to RGB24 byte array.</summary>
internal static class Vp8Decoder {

  // Number of block types for coefficient probabilities
  private const int _NUM_TYPES = 4;    // 0=Y_WITH_Y2, 1=Y_AFTER_Y2, 2=UV, 3=Y2
  private const int _NUM_BANDS = 8;
  private const int _NUM_CTX = 3;
  private const int _NUM_PROBAS = 11;

  // Macroblock prediction mode indices for keyframes
  private const int _B_PRED = 0;       // 4x4 sub-block prediction
  private const int _DC_PRED = 1;
  private const int _V_PRED = 2;
  private const int _H_PRED = 3;
  private const int _TM_PRED = 4;

  // Dequantization factors
  private sealed class DequantFactors {
    public int Y1Dc;
    public int Y1Ac;
    public int Y2Dc;
    public int Y2Ac;
    public int UvDc;
    public int UvAc;
  }

  /// <summary>Decode VP8 keyframe data to RGB24 byte array.</summary>
  public static byte[] Decode(byte[] vp8Data, int width, int height) {
    ArgumentNullException.ThrowIfNull(vp8Data);
    if (vp8Data.Length < 10)
      throw new InvalidOperationException("VP8 data too small for frame header.");

    // Parse the 3-byte uncompressed frame tag
    var frameTag = vp8Data[0] | (vp8Data[1] << 8) | (vp8Data[2] << 16);
    var isKeyframe = (frameTag & 1) == 0;
    if (!isKeyframe)
      throw new InvalidOperationException("VP8 data is not a keyframe.");

    var dataPartition0Size = frameTag >> 5;

    // Skip 10-byte header: 3 byte tag + 3 byte signature (9D 01 2A) + 2 byte width + 2 byte height
    var headerOffset = 10;
    if (headerOffset + dataPartition0Size > vp8Data.Length)
      throw new InvalidOperationException("VP8 partition 0 extends beyond data.");

    // Initialize the bool decoder for partition 0 (frame header + MB modes)
    var br = new Vp8BoolDecoder(vp8Data, headerOffset);

    // Read frame header fields
    var colorSpace = br.ReadBool(128);   // 0 = YCbCr
    var clampingType = br.ReadBool(128); // 0 = clamping required

    // Segmentation
    var segmentationEnabled = br.ReadBool(128) != 0;
    var segmentQuantizers = new int[4];
    var segmentFilterLevels = new int[4];
    var segmentAbsoluteMode = false;
    if (segmentationEnabled) {
      var updateMap = br.ReadBool(128) != 0;
      var updateData = br.ReadBool(128) != 0;
      if (updateData) {
        segmentAbsoluteMode = br.ReadBool(128) != 0;
        for (var i = 0; i < 4; ++i)
          segmentQuantizers[i] = br.ReadBool(128) != 0 ? br.ReadSignedLiteral(7) : 0;
        for (var i = 0; i < 4; ++i)
          segmentFilterLevels[i] = br.ReadBool(128) != 0 ? br.ReadSignedLiteral(6) : 0;
      }

      if (updateMap)
        for (var i = 0; i < 3; ++i)
          _ = br.ReadBool(128) != 0 ? br.ReadLiteral(8) : 255; // segment probabilities
    }

    // Loop filter parameters
    var filterType = br.ReadBool(128);       // 0=simple, 1=normal
    var filterLevel = br.ReadLiteral(6);
    var sharpness = br.ReadLiteral(3);

    // Loop filter adjustments
    var loopFilterAdj = br.ReadBool(128) != 0;
    if (loopFilterAdj) {
      var updateAdj = br.ReadBool(128) != 0;
      if (updateAdj) {
        for (var i = 0; i < 4; ++i)
          if (br.ReadBool(128) != 0)
            _ = br.ReadSignedLiteral(6); // ref frame delta
        for (var i = 0; i < 4; ++i)
          if (br.ReadBool(128) != 0)
            _ = br.ReadSignedLiteral(6); // mode delta
      }
    }

    // Number of data partitions (coefficient data)
    var log2NumPartitions = br.ReadLiteral(2);
    var numPartitions = 1 << log2NumPartitions;

    // Quantization parameters
    var yAcQi = br.ReadLiteral(7);
    var yDcDelta = br.ReadBool(128) != 0 ? br.ReadSignedLiteral(4) : 0;
    var y2DcDelta = br.ReadBool(128) != 0 ? br.ReadSignedLiteral(4) : 0;
    var y2AcDelta = br.ReadBool(128) != 0 ? br.ReadSignedLiteral(4) : 0;
    var uvDcDelta = br.ReadBool(128) != 0 ? br.ReadSignedLiteral(4) : 0;
    var uvAcDelta = br.ReadBool(128) != 0 ? br.ReadSignedLiteral(4) : 0;

    var dqf = new DequantFactors {
      Y1Dc = _DcLookup[_Clamp127(yAcQi + yDcDelta)],
      Y1Ac = _AcLookup[_Clamp127(yAcQi)],
      Y2Dc = _DcLookup[_Clamp127(yAcQi + y2DcDelta)] * 2,
      Y2Ac = _AcLookup[_Clamp127(yAcQi + y2AcDelta)] * 155 / 100,
      UvDc = _DcLookup[_Clamp127(yAcQi + uvDcDelta)],
      UvAc = _AcLookup[_Clamp127(yAcQi + uvAcDelta)]
    };
    if (dqf.Y2Ac < 8)
      dqf.Y2Ac = 8;
    if (dqf.UvDc > 132)
      dqf.UvDc = 132;

    // Read coefficient probability updates
    var coeffProbs = _InitDefaultCoeffProbs();
    for (var t = 0; t < _NUM_TYPES; ++t)
      for (var b = 0; b < _NUM_BANDS; ++b)
        for (var c = 0; c < _NUM_CTX; ++c)
          for (var p = 0; p < _NUM_PROBAS; ++p)
            if (br.ReadBool(_CoeffUpdateProbs[t][b][c][p]) != 0)
              coeffProbs[t][b][c][p] = (byte)br.ReadLiteral(8);

    // Skip coefficient flag
    var mbNoSkipCoeff = br.ReadBool(128) != 0;
    var skipProb = mbNoSkipCoeff ? br.ReadLiteral(8) : 0;

    // Macroblock dimensions
    var mbWidth = (width + 15) >> 4;
    var mbHeight = (height + 15) >> 4;

    // Read macroblock prediction modes from partition 0
    var mbModes = new int[mbWidth * mbHeight];                // 16x16 Y prediction mode
    var mbSubModes = new int[mbWidth * mbHeight * 16];        // 4x4 sub-block modes (only if B_PRED)
    var mbUvModes = new int[mbWidth * mbHeight];              // UV prediction mode
    var mbSkip = new bool[mbWidth * mbHeight];

    for (var mbRow = 0; mbRow < mbHeight; ++mbRow)
      for (var mbCol = 0; mbCol < mbWidth; ++mbCol) {
        var mbIdx = mbRow * mbWidth + mbCol;

        if (mbNoSkipCoeff)
          mbSkip[mbIdx] = br.ReadBool(skipProb) != 0;

        // Read Y prediction mode using keyframe probability table
        var yMode = _ReadKeyframeMbMode(br);
        mbModes[mbIdx] = yMode;

        if (yMode == _B_PRED) {
          // Read 16 sub-block modes
          for (var i = 0; i < 16; ++i) {
            var above = _GetAboveSubMode(mbModes, mbSubModes, mbRow, mbCol, mbWidth, i);
            var left = _GetLeftSubMode(mbModes, mbSubModes, mbRow, mbCol, mbWidth, i);
            mbSubModes[mbIdx * 16 + i] = _ReadKeyframeSubBlockMode(br, above, left);
          }
        }

        // Read UV prediction mode
        mbUvModes[mbIdx] = _ReadKeyframeUvMode(br);
      }

    // Locate data partitions for coefficient data
    var part0End = headerOffset + dataPartition0Size;
    var tokenPartitions = new Vp8BoolDecoder[numPartitions];
    var partOffset = part0End;

    // Read partition sizes (numPartitions-1 sizes, 3 bytes each LE)
    if (numPartitions > 1) {
      var sizeStart = part0End;
      partOffset = sizeStart + 3 * (numPartitions - 1);
      for (var i = 0; i < numPartitions - 1; ++i) {
        var sz = vp8Data[sizeStart + i * 3]
                 | (vp8Data[sizeStart + i * 3 + 1] << 8)
                 | (vp8Data[sizeStart + i * 3 + 2] << 16);
        tokenPartitions[i] = new Vp8BoolDecoder(vp8Data, partOffset);
        partOffset += sz;
      }

      tokenPartitions[numPartitions - 1] = new Vp8BoolDecoder(vp8Data, partOffset);
    } else {
      tokenPartitions[0] = new Vp8BoolDecoder(vp8Data, part0End);
    }

    // Allocate YUV planes (with 1-macroblock border for prediction reference)
    var yStride = mbWidth * 16;
    var uvStride = mbWidth * 8;
    var yPlane = new byte[yStride * mbHeight * 16];
    var uPlane = new byte[uvStride * mbHeight * 8];
    var vPlane = new byte[uvStride * mbHeight * 8];

    // Decode coefficients and reconstruct macroblocks
    var coeffs = new short[16];   // reusable buffer for one 4x4 block
    var y2Coeffs = new short[16]; // Y2 (WHT) block
    var y2Output = new short[16]; // WHT output (16 DC values)

    for (var mbRow = 0; mbRow < mbHeight; ++mbRow) {
      var tokenBr = tokenPartitions[mbRow % numPartitions];

      for (var mbCol = 0; mbCol < mbWidth; ++mbCol) {
        var mbIdx = mbRow * mbWidth + mbCol;
        var yMode = mbModes[mbIdx];
        var uvMode = mbUvModes[mbIdx];

        var mbX = mbCol * 16;
        var mbY = mbRow * 16;
        var uvMbX = mbCol * 8;
        var uvMbY = mbRow * 8;

        // Build above/left reference arrays for Y 16x16
        var yAbove = _GetAboveRow(yPlane, mbX, mbY, yStride, 16);
        var yLeft = _GetLeftCol(yPlane, mbX, mbY, yStride, 16);
        var yTopLeft = _GetTopLeft(yPlane, mbX, mbY, yStride);

        // Build above/left reference arrays for UV 8x8
        var uAbove = _GetAboveRow(uPlane, uvMbX, uvMbY, uvStride, 8);
        var uLeft = _GetLeftCol(uPlane, uvMbX, uvMbY, uvStride, 8);
        var uTopLeft = _GetTopLeft(uPlane, uvMbX, uvMbY, uvStride);
        var vAbove = _GetAboveRow(vPlane, uvMbX, uvMbY, uvStride, 8);
        var vLeft = _GetLeftCol(vPlane, uvMbX, uvMbY, uvStride, 8);
        var vTopLeft = _GetTopLeft(vPlane, uvMbX, uvMbY, uvStride);

        if (yMode != _B_PRED) {
          // 16x16 Y prediction
          var predMode = yMode switch {
            _DC_PRED => Vp8IntraPredictor.DC_PRED,
            _V_PRED => Vp8IntraPredictor.V_PRED,
            _H_PRED => Vp8IntraPredictor.H_PRED,
            _TM_PRED => Vp8IntraPredictor.TM_PRED,
            _ => Vp8IntraPredictor.DC_PRED
          };
          Vp8IntraPredictor.Predict16x16(predMode, yPlane, mbY * yStride + mbX, yStride, yAbove, yLeft, yTopLeft);

          if (!mbSkip[mbIdx]) {
            // Read Y2 (WHT) block
            Array.Clear(y2Coeffs, 0, 16);
            _ReadCoefficients(tokenBr, coeffProbs, 3, y2Coeffs, 0);
            _DequantizeY2(y2Coeffs, dqf);
            Vp8Dct.InverseWht(y2Coeffs, y2Output);

            // Read and apply 16 Y sub-blocks (AC only, DC from WHT)
            for (var i = 0; i < 16; ++i) {
              var subY = mbY + (i >> 2) * 4;
              var subX = mbX + (i & 3) * 4;
              Array.Clear(coeffs, 0, 16);
              _ReadCoefficients(tokenBr, coeffProbs, 1, coeffs, 1); // skip DC (index 0)
              coeffs[0] = y2Output[i]; // DC from WHT
              _DequantizeY(coeffs, dqf, hasDcFromY2: true);
              Vp8Dct.InverseDct4x4(coeffs, yPlane, subY * yStride + subX, yStride);
            }
          } else {
            // Skip: no residuals, Y2 is all zero
          }
        } else {
          // 4x4 sub-block prediction (B_PRED)
          // Build extended above row for 4x4 prediction (needs 8 pixels above for some modes)
          var extAbove = _GetExtendedAboveRow(yPlane, mbX, mbY, yStride);

          for (var i = 0; i < 16; ++i) {
            var subRow = i >> 2;
            var subCol = i & 3;
            var subY = mbY + subRow * 4;
            var subX = mbX + subCol * 4;
            var subMode = mbSubModes[mbIdx * 16 + i];

            // Build per-sub-block above/left references
            var sAbove = _Get4x4Above(yPlane, extAbove, subX, subY, yStride, subCol);
            var sAboveOff = 0;
            var sLeft = _Get4x4Left(yPlane, subX, subY, yStride);
            var sTl = _Get4x4TopLeft(yPlane, subX, subY, yStride, yTopLeft, subRow, subCol);

            Vp8IntraPredictor.Predict4x4(subMode, yPlane, subY * yStride + subX, yStride, sAbove, sAboveOff, sLeft, sTl);

            if (!mbSkip[mbIdx]) {
              Array.Clear(coeffs, 0, 16);
              _ReadCoefficients(tokenBr, coeffProbs, 0, coeffs, 0);
              _DequantizeY(coeffs, dqf, hasDcFromY2: false);
              Vp8Dct.InverseDct4x4(coeffs, yPlane, subY * yStride + subX, yStride);
            }
          }
        }

        // UV prediction
        var uvPredMode = uvMode switch {
          0 => Vp8IntraPredictor.DC_PRED,
          1 => Vp8IntraPredictor.V_PRED,
          2 => Vp8IntraPredictor.H_PRED,
          3 => Vp8IntraPredictor.TM_PRED,
          _ => Vp8IntraPredictor.DC_PRED
        };
        Vp8IntraPredictor.Predict8x8(uvPredMode, uPlane, uvMbY * uvStride + uvMbX, uvStride, uAbove, uLeft, uTopLeft);
        Vp8IntraPredictor.Predict8x8(uvPredMode, vPlane, uvMbY * uvStride + uvMbX, uvStride, vAbove, vLeft, vTopLeft);

        if (!mbSkip[mbIdx]) {
          // Read and apply 4 U + 4 V sub-blocks
          for (var i = 0; i < 4; ++i) {
            var subY = uvMbY + (i >> 1) * 4;
            var subX = uvMbX + (i & 1) * 4;
            Array.Clear(coeffs, 0, 16);
            _ReadCoefficients(tokenBr, coeffProbs, 2, coeffs, 0);
            _DequantizeUv(coeffs, dqf);
            Vp8Dct.InverseDct4x4(coeffs, uPlane, subY * uvStride + subX, uvStride);
          }

          for (var i = 0; i < 4; ++i) {
            var subY = uvMbY + (i >> 1) * 4;
            var subX = uvMbX + (i & 1) * 4;
            Array.Clear(coeffs, 0, 16);
            _ReadCoefficients(tokenBr, coeffProbs, 2, coeffs, 0);
            _DequantizeUv(coeffs, dqf);
            Vp8Dct.InverseDct4x4(coeffs, vPlane, subY * uvStride + subX, uvStride);
          }
        }
      }
    }

    // Apply loop filter
    if (filterLevel > 0) {
      if (filterType == 0)
        Vp8LoopFilter.ApplySimple(yPlane, width, height, yStride, filterLevel, sharpness);
      else
        Vp8LoopFilter.ApplyNormal(yPlane, yStride, uPlane, vPlane, uvStride, width, height, filterLevel, sharpness);
    }

    // Convert YUV 4:2:0 to RGB24
    return _ConvertYuvToRgb24(yPlane, uPlane, vPlane, width, height, yStride, uvStride);
  }

  #region coefficient reading

  // Read one 4x4 block of coefficients using the VP8 token probability tree
  private static void _ReadCoefficients(Vp8BoolDecoder br, byte[][][][] probs, int type, short[] coeffs, int startIndex) {
    var lastNonZero = -1;
    for (var i = startIndex; i < 16; ++i) {
      var band = _Bands[i];
      var ctx = lastNonZero < 0 ? 0 : lastNonZero == 0 ? 1 : 2;
      if (i > startIndex && ctx == 0)
        ctx = 0;

      var p = probs[type][band][ctx];

      // Token tree decode
      if (br.ReadBool(p[0]) == 0)
        // EOB: end of block, remaining coefficients are zero
        return;

      // Not EOB, read more of the tree
      if (br.ReadBool(p[1]) == 0) {
        // DCT_0 (zero coefficient)
        coeffs[_ZigZag[i]] = 0;
        lastNonZero = 0;
        continue;
      }

      // Non-zero coefficient
      int v;
      if (br.ReadBool(p[2]) == 0) {
        // DCT_1
        v = 1;
      } else if (br.ReadBool(p[3]) == 0) {
        // DCT_2
        v = 2;
      } else if (br.ReadBool(p[4]) == 0) {
        if (br.ReadBool(p[5]) == 0)
          v = 3;
        else
          v = 4;
      } else if (br.ReadBool(p[6]) == 0) {
        // Category 1 or 2
        if (br.ReadBool(p[7]) == 0) {
          // CAT1: 5 + 1 extra bit
          v = 5 + br.ReadBool(159);
        } else {
          // CAT2: 7 + 2 extra bits
          v = 7 + br.ReadBool(165) * 2 + br.ReadBool(145);
        }
      } else {
        // Category 3-6
        if (br.ReadBool(p[8]) == 0) {
          if (br.ReadBool(p[9]) == 0) {
            // CAT3: 11 + 3 extra bits
            v = 11;
            v += br.ReadBool(173) * 4;
            v += br.ReadBool(148) * 2;
            v += br.ReadBool(140);
          } else {
            // CAT4: 19 + 4 extra bits
            v = 19;
            v += br.ReadBool(176) * 8;
            v += br.ReadBool(155) * 4;
            v += br.ReadBool(140) * 2;
            v += br.ReadBool(135);
          }
        } else {
          if (br.ReadBool(p[10]) == 0) {
            // CAT5: 35 + 5 extra bits
            v = 35;
            v += br.ReadBool(180) * 16;
            v += br.ReadBool(157) * 8;
            v += br.ReadBool(141) * 4;
            v += br.ReadBool(134) * 2;
            v += br.ReadBool(130);
          } else {
            // CAT6: 67 + 11 extra bits
            v = 67;
            v += br.ReadBool(254) * 1024;
            v += br.ReadBool(254) * 512;
            v += br.ReadBool(243) * 256;
            v += br.ReadBool(230) * 128;
            v += br.ReadBool(196) * 64;
            v += br.ReadBool(177) * 32;
            v += br.ReadBool(153) * 16;
            v += br.ReadBool(140) * 8;
            v += br.ReadBool(133) * 4;
            v += br.ReadBool(130) * 2;
            v += br.ReadBool(129);
          }
        }
      }

      // Read sign bit
      if (br.ReadBool(128) != 0)
        v = -v;

      coeffs[_ZigZag[i]] = (short)v;
      lastNonZero = v != 0 ? (Math.Abs(v) > 1 ? 2 : 1) : 0;
    }
  }

  #endregion

  #region dequantization

  private static void _DequantizeY(short[] coeffs, DequantFactors dqf, bool hasDcFromY2) {
    if (!hasDcFromY2)
      coeffs[0] = (short)(coeffs[0] * dqf.Y1Dc);
    // else DC already set from WHT output (already dequantized via Y2)
    for (var i = 1; i < 16; ++i)
      coeffs[i] = (short)(coeffs[i] * dqf.Y1Ac);
  }

  private static void _DequantizeY2(short[] coeffs, DequantFactors dqf) {
    coeffs[0] = (short)(coeffs[0] * dqf.Y2Dc);
    for (var i = 1; i < 16; ++i)
      coeffs[i] = (short)(coeffs[i] * dqf.Y2Ac);
  }

  private static void _DequantizeUv(short[] coeffs, DequantFactors dqf) {
    coeffs[0] = (short)(coeffs[0] * dqf.UvDc);
    for (var i = 1; i < 16; ++i)
      coeffs[i] = (short)(coeffs[i] * dqf.UvAc);
  }

  #endregion

  #region macroblock mode reading

  // Keyframe Y mode probabilities (from VP8 spec section 11.6.1)
  private static readonly int[] _KfYModeProbs = [145, 156, 163, 128];

  // Keyframe UV mode probabilities
  private static readonly int[] _KfUvModeProbs = [142, 114, 183];

  // Keyframe sub-block mode probabilities [above_mode][left_mode][10 probs]
  private static readonly int[][][] _KfBModeProbs = [
    // above=B_DC_PRED(0)
    [
      [231, 120, 48, 89, 115, 113, 120, 152, 112],  // left=0
      [152, 179, 64, 126, 170, 118, 46, 70, 95],     // left=1
      [175, 69, 143, 80, 85, 82, 72, 155, 103],      // left=2
      [56, 58, 10, 171, 218, 189, 17, 13, 152],      // left=3
      [114, 26, 17, 163, 44, 195, 21, 10, 173],      // left=4
      [121, 24, 80, 195, 26, 62, 44, 64, 85],        // left=5
      [144, 71, 10, 38, 171, 213, 144, 34, 26],      // left=6
      [170, 46, 55, 19, 136, 160, 33, 206, 71],      // left=7
      [63, 20, 8, 114, 114, 208, 12, 9, 226],        // left=8
      [81, 40, 11, 96, 182, 84, 29, 16, 36],         // left=9
    ],
    // above=B_TM_PRED(1)
    [
      [134, 183, 89, 137, 98, 101, 106, 165, 148],
      [72, 187, 100, 130, 157, 111, 32, 75, 80],
      [66, 102, 167, 99, 74, 62, 40, 234, 128],
      [41, 53, 9, 178, 241, 141, 26, 8, 107],
      [74, 43, 26, 146, 73, 166, 49, 23, 157],
      [65, 38, 105, 160, 51, 52, 31, 115, 128],
      [104, 79, 12, 27, 217, 255, 87, 17, 7],
      [87, 68, 71, 44, 114, 51, 15, 186, 23],
      [47, 41, 14, 110, 182, 183, 21, 17, 194],
      [66, 45, 25, 102, 197, 189, 23, 18, 22],
    ],
    // above=B_VE_PRED(2)
    [
      [88, 88, 147, 150, 42, 46, 45, 196, 205],
      [43, 97, 183, 117, 85, 38, 35, 179, 61],
      [39, 53, 200, 87, 26, 21, 43, 232, 171],
      [56, 34, 51, 104, 114, 102, 29, 93, 77],
      [39, 28, 85, 171, 58, 165, 90, 98, 64],
      [34, 22, 116, 206, 23, 34, 43, 166, 73],
      [107, 54, 32, 26, 51, 1, 81, 43, 31],
      [68, 25, 106, 22, 64, 171, 36, 225, 114],
      [34, 19, 21, 102, 132, 188, 16, 76, 124],
      [62, 18, 78, 95, 85, 57, 50, 48, 51],
    ],
    // above=B_HE_PRED(3)
    [
      [193, 101, 35, 159, 215, 111, 89, 46, 111],
      [60, 148, 31, 172, 219, 228, 21, 18, 111],
      [112, 113, 77, 85, 179, 255, 38, 120, 114],
      [40, 42, 1, 196, 245, 209, 10, 25, 109],
      [88, 43, 29, 140, 166, 213, 37, 43, 154],
      [61, 63, 30, 155, 67, 45, 68, 1, 209],
      [100, 80, 8, 43, 154, 1, 51, 26, 71],
      [142, 78, 78, 16, 255, 128, 34, 197, 171],
      [41, 40, 5, 102, 211, 183, 4, 1, 221],
      [51, 50, 17, 168, 209, 192, 23, 25, 82],
    ],
    // above=B_RD_PRED(4)
    [
      [68, 45, 128, 34, 1, 47, 11, 245, 171],
      [62, 17, 19, 70, 146, 85, 55, 62, 70],
      [37, 43, 37, 154, 100, 163, 85, 160, 1],
      [63, 9, 92, 136, 28, 64, 32, 201, 85],
      [75, 15, 9, 9, 64, 255, 184, 119, 16],
      [86, 6, 28, 5, 64, 255, 25, 248, 1],
      [56, 8, 17, 132, 137, 255, 55, 116, 128],
      [58, 15, 20, 82, 135, 57, 26, 121, 40],
      [80, 10, 44, 83, 128, 195, 4, 141, 1],
      [59, 26, 19, 66, 99, 58, 103, 74, 10],
    ],
    // above=B_VR_PRED(5)
    [
      [112, 113, 77, 85, 179, 255, 38, 120, 114],
      [40, 42, 1, 196, 245, 209, 10, 25, 109],
      [88, 43, 29, 140, 166, 213, 37, 43, 154],
      [61, 63, 30, 155, 67, 45, 68, 1, 209],
      [100, 80, 8, 43, 154, 1, 51, 26, 71],
      [142, 78, 78, 16, 255, 128, 34, 197, 171],
      [41, 40, 5, 102, 211, 183, 4, 1, 221],
      [51, 50, 17, 168, 209, 192, 23, 25, 82],
      [60, 148, 31, 172, 219, 228, 21, 18, 111],
      [112, 113, 77, 85, 179, 255, 38, 120, 114],
    ],
    // above=B_LD_PRED(6)
    [
      [224, 124, 74, 58, 103, 106, 96, 47, 67],
      [152, 179, 64, 126, 170, 118, 46, 70, 95],
      [153, 69, 143, 80, 85, 82, 72, 155, 103],
      [56, 58, 10, 171, 218, 189, 17, 13, 152],
      [114, 26, 17, 163, 44, 195, 21, 10, 173],
      [121, 24, 80, 195, 26, 62, 44, 64, 85],
      [144, 71, 10, 38, 171, 213, 144, 34, 26],
      [170, 46, 55, 19, 136, 160, 33, 206, 71],
      [63, 20, 8, 114, 114, 208, 12, 9, 226],
      [81, 40, 11, 96, 182, 84, 29, 16, 36],
    ],
    // above=B_VL_PRED(7)
    [
      [197, 159, 128, 34, 1, 47, 11, 245, 171],
      [62, 17, 19, 70, 146, 85, 55, 62, 70],
      [37, 43, 37, 154, 100, 163, 85, 160, 1],
      [63, 9, 92, 136, 28, 64, 32, 201, 85],
      [75, 15, 9, 9, 64, 255, 184, 119, 16],
      [86, 6, 28, 5, 64, 255, 25, 248, 1],
      [56, 8, 17, 132, 137, 255, 55, 116, 128],
      [58, 15, 20, 82, 135, 57, 26, 121, 40],
      [80, 10, 44, 83, 128, 195, 4, 141, 1],
      [59, 26, 19, 66, 99, 58, 103, 74, 10],
    ],
    // above=B_HD_PRED(8)
    [
      [130, 57, 36, 155, 116, 77, 92, 49, 122],
      [76, 133, 29, 172, 209, 210, 18, 28, 120],
      [100, 78, 116, 94, 117, 131, 62, 147, 100],
      [47, 50, 8, 180, 229, 163, 14, 18, 120],
      [79, 37, 21, 148, 113, 196, 31, 34, 166],
      [65, 47, 47, 167, 57, 44, 58, 8, 183],
      [93, 73, 12, 47, 161, 49, 55, 35, 72],
      [128, 64, 72, 25, 198, 100, 25, 191, 137],
      [43, 39, 6, 112, 204, 186, 6, 4, 213],
      [55, 48, 14, 158, 199, 181, 23, 22, 86],
    ],
    // above=B_HU_PRED(9)
    [
      [156, 72, 46, 149, 158, 113, 93, 53, 116],
      [83, 162, 42, 145, 186, 170, 25, 39, 97],
      [118, 81, 125, 96, 107, 116, 56, 159, 105],
      [48, 47, 5, 185, 235, 176, 14, 14, 133],
      [87, 31, 23, 157, 86, 191, 32, 27, 164],
      [77, 38, 72, 174, 42, 47, 49, 42, 133],
      [111, 74, 11, 38, 171, 173, 96, 34, 37],
      [149, 55, 63, 22, 158, 117, 27, 197, 93],
      [49, 32, 9, 112, 160, 185, 9, 8, 216],
      [63, 43, 15, 117, 191, 124, 28, 20, 49],
    ],
  ];

  private static int _ReadKeyframeMbMode(Vp8BoolDecoder br) {
    // Tree: if(!B) -> B_PRED(0), else if(!B) -> DC_PRED(1), else if(!B) -> V_PRED(2), else if(!B) -> H_PRED(3), else -> TM_PRED(4)
    if (br.ReadBool(_KfYModeProbs[0]) == 0)
      return _B_PRED;
    if (br.ReadBool(_KfYModeProbs[1]) == 0)
      return _DC_PRED;
    if (br.ReadBool(_KfYModeProbs[2]) == 0)
      return _V_PRED;
    return br.ReadBool(_KfYModeProbs[3]) == 0 ? _H_PRED : _TM_PRED;
  }

  private static int _ReadKeyframeUvMode(Vp8BoolDecoder br) {
    if (br.ReadBool(_KfUvModeProbs[0]) == 0)
      return 0; // DC
    if (br.ReadBool(_KfUvModeProbs[1]) == 0)
      return 1; // V
    return br.ReadBool(_KfUvModeProbs[2]) == 0 ? 2 : 3; // H or TM
  }

  private static int _ReadKeyframeSubBlockMode(Vp8BoolDecoder br, int aboveMode, int leftMode) {
    var p = _KfBModeProbs[aboveMode][leftMode];
    // Tree decoding for 10 intra 4x4 modes
    if (br.ReadBool(p[0]) == 0)
      return Vp8IntraPredictor.B_DC_PRED;
    if (br.ReadBool(p[1]) == 0)
      return Vp8IntraPredictor.B_TM_PRED;
    if (br.ReadBool(p[2]) == 0)
      return Vp8IntraPredictor.B_VE_PRED;
    if (br.ReadBool(p[3]) == 0) {
      if (br.ReadBool(p[4]) == 0)
        return Vp8IntraPredictor.B_HE_PRED;
      return br.ReadBool(p[5]) == 0 ? Vp8IntraPredictor.B_RD_PRED : Vp8IntraPredictor.B_VR_PRED;
    }
    if (br.ReadBool(p[6]) == 0)
      return Vp8IntraPredictor.B_LD_PRED;
    return br.ReadBool(p[7]) == 0
      ? Vp8IntraPredictor.B_VL_PRED
      : br.ReadBool(p[8]) == 0
        ? Vp8IntraPredictor.B_HD_PRED
        : Vp8IntraPredictor.B_HU_PRED;
  }

  private static int _GetAboveSubMode(int[] mbModes, int[] mbSubModes, int mbRow, int mbCol, int mbWidth, int subIdx) {
    var subRow = subIdx >> 2;
    var subCol = subIdx & 3;
    if (subRow > 0) {
      // Above sub-block is in the same macroblock
      var mbIdx = mbRow * mbWidth + mbCol;
      return mbSubModes[mbIdx * 16 + (subRow - 1) * 4 + subCol];
    }

    if (mbRow == 0)
      return Vp8IntraPredictor.B_DC_PRED; // Default for top edge

    // Above sub-block is in the macroblock above (bottom row, same column)
    var aboveMbIdx = (mbRow - 1) * mbWidth + mbCol;
    if (mbModes[aboveMbIdx] != _B_PRED)
      return Vp8IntraPredictor.B_DC_PRED;
    return mbSubModes[aboveMbIdx * 16 + 12 + subCol];
  }

  private static int _GetLeftSubMode(int[] mbModes, int[] mbSubModes, int mbRow, int mbCol, int mbWidth, int subIdx) {
    var subRow = subIdx >> 2;
    var subCol = subIdx & 3;
    if (subCol > 0) {
      var mbIdx = mbRow * mbWidth + mbCol;
      return mbSubModes[mbIdx * 16 + subRow * 4 + (subCol - 1)];
    }

    if (mbCol == 0)
      return Vp8IntraPredictor.B_DC_PRED;

    var leftMbIdx = mbRow * mbWidth + (mbCol - 1);
    if (mbModes[leftMbIdx] != _B_PRED)
      return Vp8IntraPredictor.B_DC_PRED;
    return mbSubModes[leftMbIdx * 16 + subRow * 4 + 3];
  }

  #endregion

  #region prediction reference helpers

  private static byte[]? _GetAboveRow(byte[] plane, int mbX, int mbY, int stride, int size) {
    if (mbY == 0)
      return null;
    var row = new byte[size];
    var off = (mbY - 1) * stride + mbX;
    Buffer.BlockCopy(plane, off, row, 0, size);
    return row;
  }

  private static byte[]? _GetLeftCol(byte[] plane, int mbX, int mbY, int stride, int size) {
    if (mbX == 0)
      return null;
    var col = new byte[size];
    for (var i = 0; i < size; ++i)
      col[i] = plane[(mbY + i) * stride + mbX - 1];
    return col;
  }

  private static byte _GetTopLeft(byte[] plane, int mbX, int mbY, int stride) {
    if (mbX == 0 || mbY == 0)
      return 128;
    return plane[(mbY - 1) * stride + mbX - 1];
  }

  private static byte[] _GetExtendedAboveRow(byte[] plane, int mbX, int mbY, int stride) {
    // Returns 24 pixels: 16 from above + up to 8 from above-right
    var row = new byte[24];
    if (mbY == 0) {
      for (var i = 0; i < 24; ++i)
        row[i] = 127;
      return row;
    }

    var off = (mbY - 1) * stride + mbX;
    for (var i = 0; i < 16; ++i)
      row[i] = off + i < plane.Length ? plane[off + i] : (byte)127;
    for (var i = 16; i < 24; ++i)
      row[i] = off + i < plane.Length ? plane[off + i] : row[15];
    return row;
  }

  private static byte[]? _Get4x4Above(byte[] plane, byte[] extAbove, int subX, int subY, int stride, int subCol) {
    if (subY == 0)
      return null;

    // Return 8 pixels above (4 directly above + 4 above-right for diagonal modes)
    var above = new byte[8];
    var off = (subY - 1) * stride + subX;
    for (var i = 0; i < 8; ++i)
      above[i] = off + i < plane.Length ? plane[off + i] : (off + i > 0 ? plane[Math.Min(off + 3, plane.Length - 1)] : (byte)127);
    return above;
  }

  private static byte[]? _Get4x4Left(byte[] plane, int subX, int subY, int stride) {
    if (subX == 0)
      return null;
    var left = new byte[4];
    for (var i = 0; i < 4; ++i)
      left[i] = plane[(subY + i) * stride + subX - 1];
    return left;
  }

  private static byte _Get4x4TopLeft(byte[] plane, int subX, int subY, int stride, byte mbTopLeft, int subRow, int subCol) {
    if (subX == 0 || subY == 0)
      return subX == 0 && subY == 0 ? mbTopLeft : (byte)127;
    return plane[(subY - 1) * stride + subX - 1];
  }

  #endregion

  #region YUV to RGB conversion

  private static byte[] _ConvertYuvToRgb24(byte[] yPlane, byte[] uPlane, byte[] vPlane, int width, int height, int yStride, int uvStride) {
    var rgb = new byte[width * height * 3];

    for (var row = 0; row < height; ++row) {
      var uvRow = row >> 1;
      for (var col = 0; col < width; ++col) {
        var uvCol = col >> 1;
        var y = yPlane[row * yStride + col];
        var u = uPlane[uvRow * uvStride + uvCol];
        var v = vPlane[uvRow * uvStride + uvCol];

        // BT.601 integer approximation:
        // R = clamp((298*(Y-16) + 409*(V-128) + 128) >> 8)
        // G = clamp((298*(Y-16) - 100*(U-128) - 208*(V-128) + 128) >> 8)
        // B = clamp((298*(Y-16) + 516*(U-128) + 128) >> 8)
        var yy = 298 * y - 56992;
        var rgbOff = (row * width + col) * 3;
        rgb[rgbOff + 0] = _ClampByte((yy + 409 * v + 128) >> 8);
        rgb[rgbOff + 1] = _ClampByte((yy - 100 * u - 208 * v + 39424 + 128) >> 8);
        rgb[rgbOff + 2] = _ClampByte((yy + 516 * u - 70688 + 128) >> 8);
      }
    }

    return rgb;
  }

  private static byte _ClampByte(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

  #endregion

  #region lookup tables

  private static int _Clamp127(int v) => v < 0 ? 0 : v > 127 ? 127 : v;

  // VP8 zig-zag scan order for 4x4 blocks
  private static readonly int[] _ZigZag = [
    0, 1, 4, 8, 5, 2, 3, 6, 9, 12, 13, 10, 7, 11, 14, 15
  ];

  // Band index for each coefficient position (0-15)
  private static readonly int[] _Bands = [
    0, 1, 2, 3, 6, 4, 5, 6, 6, 6, 6, 6, 6, 6, 6, 7
  ];

  // DC dequantization lookup table (index 0-127)
  private static readonly int[] _DcLookup = [
      4,   5,   6,   7,   8,   9,  10,  10,
     11,  12,  13,  14,  15,  16,  17,  17,
     18,  19,  20,  20,  21,  21,  22,  22,
     23,  23,  24,  25,  25,  26,  27,  28,
     29,  30,  31,  32,  33,  34,  35,  36,
     37,  37,  38,  39,  40,  41,  42,  43,
     44,  45,  46,  46,  47,  48,  49,  50,
     51,  52,  53,  54,  55,  56,  57,  58,
     59,  60,  61,  62,  63,  64,  65,  66,
     67,  68,  69,  70,  71,  72,  73,  74,
     75,  76,  76,  77,  78,  79,  80,  81,
     82,  83,  84,  85,  86,  87,  88,  89,
     91,  93,  95,  96,  98, 100, 101, 102,
    104, 106, 108, 110, 112, 114, 116, 118,
    122, 124, 126, 128, 130, 132, 134, 136,
    138, 140, 143, 145, 148, 151, 155, 157
  ];

  // AC dequantization lookup table (index 0-127)
  private static readonly int[] _AcLookup = [
      4,   5,   6,   7,   8,   9,  10,  11,
     12,  13,  14,  15,  16,  17,  18,  19,
     20,  21,  22,  23,  24,  25,  26,  27,
     28,  29,  30,  31,  32,  33,  34,  35,
     36,  37,  38,  39,  40,  41,  42,  43,
     44,  45,  46,  47,  48,  49,  50,  51,
     52,  53,  54,  55,  56,  57,  58,  60,
     62,  64,  66,  68,  70,  72,  74,  76,
     78,  80,  82,  84,  86,  88,  90,  92,
     94,  96,  98, 100, 102, 104, 106, 108,
    110, 112, 114, 116, 119, 122, 125, 128,
    131, 134, 137, 140, 143, 146, 149, 152,
    155, 158, 161, 164, 167, 170, 173, 177,
    181, 185, 189, 193, 197, 201, 205, 209,
    213, 217, 221, 225, 229, 234, 239, 245,
    249, 254, 259, 264, 269, 274, 279, 284
  ];

  // Coefficient probability update probabilities [type][band][ctx][proba]
  // From VP8 spec section 13.4
  private static readonly byte[][][][] _CoeffUpdateProbs = _InitCoeffUpdateProbs();

  private static byte[][][][] _InitCoeffUpdateProbs() {
    // These are the probabilities that a coefficient probability is updated
    // Organized as [4 types][8 bands][3 contexts][11 probabilities]
    var p = new byte[_NUM_TYPES][][][];

    // Type 0: Y_WITH_Y2 (luma with Y2 block present, i.e. B_PRED mode)
    p[0] = [
      [
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [176, 246, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [223, 241, 252, 255, 255, 255, 255, 255, 255, 255, 255],
        [249, 253, 253, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 244, 252, 255, 255, 255, 255, 255, 255, 255, 255],
        [234, 254, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [253, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 246, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [239, 253, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [254, 255, 254, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 248, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [251, 255, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 253, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [251, 254, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [254, 255, 254, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 254, 253, 255, 254, 255, 255, 255, 255, 255, 255],
        [250, 255, 254, 255, 254, 255, 255, 255, 255, 255, 255],
        [254, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
    ];

    // Type 1: Y_AFTER_Y2 (luma AC after Y2 DC subtraction)
    p[1] = [
      [
        [217, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [225, 252, 241, 253, 255, 255, 254, 255, 255, 255, 255],
        [234, 250, 241, 250, 253, 255, 253, 254, 255, 255, 255],
      ],
      [
        [255, 254, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [223, 254, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [238, 253, 254, 254, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 248, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [249, 254, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 253, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [247, 254, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 253, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [252, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 254, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [253, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 254, 253, 255, 255, 255, 255, 255, 255, 255, 255],
        [250, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [254, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
    ];

    // Type 2: UV (chroma)
    p[2] = [
      [
        [186, 251, 250, 255, 255, 255, 255, 255, 255, 255, 255],
        [234, 251, 244, 254, 255, 255, 255, 255, 255, 255, 255],
        [251, 251, 243, 253, 254, 255, 254, 255, 255, 255, 255],
      ],
      [
        [255, 253, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [236, 253, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [251, 253, 253, 254, 254, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 254, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [254, 254, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 254, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [254, 254, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [254, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [254, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
    ];

    // Type 3: Y2 (WHT second-order)
    p[3] = [
      [
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 248, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [249, 254, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 253, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [247, 254, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 253, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [252, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 254, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [253, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 254, 253, 255, 255, 255, 255, 255, 255, 255, 255],
        [250, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [254, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
    ];

    return p;
  }

  // Default coefficient probabilities (from VP8 spec section 13.5)
  private static byte[][][][] _InitDefaultCoeffProbs() {
    var p = new byte[_NUM_TYPES][][][];

    // Type 0: Y_WITH_Y2
    p[0] = [
      [
        [128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128],
        [128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128],
        [128, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128],
      ],
      [
        [253, 136, 254, 255, 228, 219, 128, 128, 128, 128, 128],
        [189, 129, 242, 255, 227, 213, 255, 219, 128, 128, 128],
        [106, 126, 227, 252, 214, 209, 255, 255, 128, 128, 128],
      ],
      [
        [  1,  98, 248, 255, 236, 226, 255, 255, 128, 128, 128],
        [181, 133, 238, 254, 221, 234, 255, 154, 128, 128, 128],
        [ 78, 134, 202, 247, 198, 180, 255, 219, 128, 128, 128],
      ],
      [
        [  1, 185, 249, 255, 243, 255, 128, 128, 128, 128, 128],
        [184, 150, 247, 255, 236, 224, 128, 128, 128, 128, 128],
        [ 77, 110, 216, 255, 236, 230, 128, 128, 128, 128, 128],
      ],
      [
        [  1, 101, 251, 255, 241, 255, 128, 128, 128, 128, 128],
        [170, 139, 241, 252, 236, 209, 255, 255, 128, 128, 128],
        [ 37, 116, 196, 243, 228, 255, 255, 255, 128, 128, 128],
      ],
      [
        [  1, 204, 254, 255, 245, 255, 128, 128, 128, 128, 128],
        [207, 160, 250, 255, 238, 128, 128, 128, 128, 128, 128],
        [102, 103, 231, 255, 211, 171, 128, 128, 128, 128, 128],
      ],
      [
        [  1, 152, 252, 255, 240, 255, 128, 128, 128, 128, 128],
        [177, 135, 243, 255, 234, 225, 128, 128, 128, 128, 128],
        [ 80, 129, 211, 255, 194, 224, 128, 128, 128, 128, 128],
      ],
      [
        [  1,   1, 255, 128, 128, 128, 128, 128, 128, 128, 128],
        [246,   1, 255, 128, 128, 128, 128, 128, 128, 128, 128],
        [255, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128],
      ],
    ];

    // Type 1: Y_AFTER_Y2
    p[1] = [
      [
        [198,  35, 237, 223, 193, 187, 162, 160, 145, 155,  62],
        [131,  45, 198, 221, 172, 176, 220, 157, 252, 221,   1],
        [ 68,  47, 146, 208, 149, 167, 221, 162, 255, 223, 128],
      ],
      [
        [  1, 149, 241, 255, 221, 224, 255, 255, 128, 128, 128],
        [184, 141, 234, 253, 222, 220, 255, 199, 128, 128, 128],
        [ 81, 99,  181, 242, 195, 203, 255, 219, 128, 128, 128],
      ],
      [
        [  1, 129, 232, 253, 214, 197, 242, 196, 255, 255, 128],
        [ 99, 121, 210, 250, 201, 198, 255, 202, 128, 128, 128],
        [ 23,  91, 163, 242, 170, 187, 247, 210, 255, 255, 128],
      ],
      [
        [  1, 200, 246, 255, 234, 255, 128, 128, 128, 128, 128],
        [109, 178, 241, 255, 231, 245, 255, 255, 128, 128, 128],
        [ 44, 130, 201, 253, 205, 185, 255, 255, 128, 128, 128],
      ],
      [
        [  1, 132, 239, 251, 219, 209, 255, 165, 128, 128, 128],
        [ 94, 136, 225, 251, 218, 190, 255, 255, 128, 128, 128],
        [ 22, 100, 174, 245, 186, 161, 255, 199, 128, 128, 128],
      ],
      [
        [  1, 182, 249, 255, 232, 235, 128, 128, 128, 128, 128],
        [124, 143, 241, 255, 227, 234, 128, 128, 128, 128, 128],
        [ 35,  77, 181, 251, 193, 211, 255, 205, 128, 128, 128],
      ],
      [
        [  1, 157, 247, 255, 236, 231, 255, 255, 128, 128, 128],
        [121, 141, 235, 255, 225, 227, 255, 255, 128, 128, 128],
        [ 45, 99,  188, 251, 195, 217, 255, 224, 128, 128, 128],
      ],
      [
        [  1,   1, 251, 255, 213, 255, 128, 128, 128, 128, 128],
        [203,   1, 248, 255, 255, 128, 128, 128, 128, 128, 128],
        [137,   1, 177, 255, 224, 255, 128, 128, 128, 128, 128],
      ],
    ];

    // Type 2: UV
    p[2] = [
      [
        [253, 9,   248, 251, 207, 208, 255, 192, 128, 128, 128],
        [175, 13,  224, 243, 193, 185, 249, 198, 255, 255, 128],
        [ 73, 17,  171, 221, 161, 179, 236, 167, 255, 234, 128],
      ],
      [
        [  1,  95, 247, 253, 212, 183, 255, 255, 128, 128, 128],
        [239, 148, 254, 255, 222, 171, 255, 218, 128, 128, 128],
        [132, 138, 247, 253, 219, 198, 255, 228, 128, 128, 128],
      ],
      [
        [  1, 101, 251, 255, 241, 255, 128, 128, 128, 128, 128],
        [194, 169, 254, 255, 227, 255, 128, 128, 128, 128, 128],
        [117, 110, 240, 255, 229, 255, 128, 128, 128, 128, 128],
      ],
      [
        [  1, 152, 252, 255, 240, 255, 128, 128, 128, 128, 128],
        [177, 135, 243, 255, 234, 225, 128, 128, 128, 128, 128],
        [ 80, 129, 211, 255, 194, 224, 128, 128, 128, 128, 128],
      ],
      [
        [  1, 227, 254, 255, 244, 255, 128, 128, 128, 128, 128],
        [202, 166, 253, 255, 235, 255, 128, 128, 128, 128, 128],
        [117, 109, 245, 255, 215, 255, 128, 128, 128, 128, 128],
      ],
      [
        [  1, 237, 253, 255, 246, 255, 128, 128, 128, 128, 128],
        [204, 168, 253, 253, 236, 255, 128, 128, 128, 128, 128],
        [109,  93, 243, 255, 231, 255, 128, 128, 128, 128, 128],
      ],
      [
        [  1, 155, 247, 255, 236, 255, 128, 128, 128, 128, 128],
        [154, 117, 236, 255, 224, 255, 128, 128, 128, 128, 128],
        [ 77, 103, 213, 255, 192, 255, 128, 128, 128, 128, 128],
      ],
      [
        [  1,   1, 255, 128, 128, 128, 128, 128, 128, 128, 128],
        [246,   1, 255, 128, 128, 128, 128, 128, 128, 128, 128],
        [255, 128, 128, 128, 128, 128, 128, 128, 128, 128, 128],
      ],
    ];

    // Type 3: Y2
    p[3] = [
      [
        [255, 253, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 254, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 246, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [239, 253, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [254, 255, 254, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 248, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [251, 255, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 253, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [251, 254, 254, 255, 255, 255, 255, 255, 255, 255, 255],
        [254, 255, 254, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 254, 253, 255, 254, 255, 255, 255, 255, 255, 255],
        [250, 255, 254, 255, 254, 255, 255, 255, 255, 255, 255],
        [254, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
      [
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
        [255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255],
      ],
    ];

    return p;
  }

  #endregion
}
