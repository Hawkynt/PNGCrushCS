using System;

namespace FileFormat.Jpeg;

/// <summary>8x8 FDCT and IDCT using AAN algorithm with integer fixed-point arithmetic.</summary>
internal static class JpegDct {

  // AAN scale factors: cos(k*pi/16) * sqrt(2) for k=0..7, scaled to fixed-point
  // Using 13-bit fixed-point (multiply by 8192)
  private const int FIX_0_298631336 = 2446;
  private const int FIX_0_390180644 = 3196;
  private const int FIX_0_541196100 = 4433;
  private const int FIX_0_765366865 = 6270;
  private const int FIX_0_899976223 = 7373;
  private const int FIX_1_175875602 = 9633;
  private const int FIX_1_501321110 = 12299;
  private const int FIX_1_847759065 = 15137;
  private const int FIX_1_961570560 = 16069;
  private const int FIX_2_053119869 = 16819;
  private const int FIX_2_562915447 = 20995;
  private const int FIX_3_072711026 = 25172;

  private const int CONST_BITS = 13;
  private const int PASS1_BITS = 2;

  /// <summary>Forward DCT: 8x8 spatial block → 8x8 frequency coefficients.</summary>
  public static void ForwardDct(int[] block) {
    // Pass 1: process rows
    for (var i = 0; i < 64; i += 8) {
      var tmp0 = block[i + 0] + block[i + 7];
      var tmp7 = block[i + 0] - block[i + 7];
      var tmp1 = block[i + 1] + block[i + 6];
      var tmp6 = block[i + 1] - block[i + 6];
      var tmp2 = block[i + 2] + block[i + 5];
      var tmp5 = block[i + 2] - block[i + 5];
      var tmp3 = block[i + 3] + block[i + 4];
      var tmp4 = block[i + 3] - block[i + 4];

      // Even part
      var tmp10 = tmp0 + tmp3;
      var tmp13 = tmp0 - tmp3;
      var tmp11 = tmp1 + tmp2;
      var tmp12 = tmp1 - tmp2;

      block[i + 0] = (tmp10 + tmp11) << PASS1_BITS;
      block[i + 4] = (tmp10 - tmp11) << PASS1_BITS;

      var z1 = (tmp12 + tmp13) * FIX_0_541196100;
      block[i + 2] = (z1 + tmp13 * FIX_0_765366865 + (1 << (CONST_BITS - PASS1_BITS - 1))) >> (CONST_BITS - PASS1_BITS);
      block[i + 6] = (z1 - tmp12 * FIX_1_847759065 + (1 << (CONST_BITS - PASS1_BITS - 1))) >> (CONST_BITS - PASS1_BITS);

      // Odd part (libjpeg jfdctint.c formulation)
      // Variable mapping: tmp7=elem[0]-elem[7], tmp6=elem[1]-elem[6], tmp5=elem[2]-elem[5], tmp4=elem[3]-elem[4]
      var z1o = tmp7 + tmp4;
      var z2o = tmp6 + tmp5;
      var z3o = tmp7 + tmp5;
      var z4o = tmp6 + tmp4;
      var z5o = (z3o + z4o) * FIX_1_175875602;

      var p0 = tmp7 * FIX_0_298631336;
      var p1 = tmp6 * FIX_2_053119869;
      var p2 = tmp5 * FIX_3_072711026;
      var p3 = tmp4 * FIX_1_501321110;
      z1o *= -FIX_0_899976223;
      z2o *= -FIX_2_562915447;
      z3o = -z3o * FIX_1_961570560 + z5o;
      z4o = -z4o * FIX_0_390180644 + z5o;

      var oddRound = 1 << (CONST_BITS - PASS1_BITS - 1);
      block[i + 7] = (p0 + z1o + z3o + oddRound) >> (CONST_BITS - PASS1_BITS);
      block[i + 5] = (p1 + z2o + z4o + oddRound) >> (CONST_BITS - PASS1_BITS);
      block[i + 3] = (p2 + z2o + z3o + oddRound) >> (CONST_BITS - PASS1_BITS);
      block[i + 1] = (p3 + z1o + z4o + oddRound) >> (CONST_BITS - PASS1_BITS);
    }

    // Pass 2: process columns
    for (var i = 0; i < 8; ++i) {
      var tmp0 = block[i] + block[i + 56];
      var tmp7 = block[i] - block[i + 56];
      var tmp1 = block[i + 8] + block[i + 48];
      var tmp6 = block[i + 8] - block[i + 48];
      var tmp2 = block[i + 16] + block[i + 40];
      var tmp5 = block[i + 16] - block[i + 40];
      var tmp3 = block[i + 24] + block[i + 32];
      var tmp4 = block[i + 24] - block[i + 32];

      var tmp10 = tmp0 + tmp3;
      var tmp13 = tmp0 - tmp3;
      var tmp11 = tmp1 + tmp2;
      var tmp12 = tmp1 - tmp2;

      block[i] = (tmp10 + tmp11 + (1 << (PASS1_BITS - 1))) >> PASS1_BITS;
      block[i + 32] = (tmp10 - tmp11 + (1 << (PASS1_BITS - 1))) >> PASS1_BITS;

      var z1 = (tmp12 + tmp13) * FIX_0_541196100;
      block[i + 16] = (z1 + tmp13 * FIX_0_765366865 + (1 << (CONST_BITS + PASS1_BITS - 1))) >> (CONST_BITS + PASS1_BITS);
      block[i + 48] = (z1 - tmp12 * FIX_1_847759065 + (1 << (CONST_BITS + PASS1_BITS - 1))) >> (CONST_BITS + PASS1_BITS);

      // Odd part (libjpeg jfdctint.c formulation)
      var z1c = tmp7 + tmp4;
      var z2c = tmp6 + tmp5;
      var z3c = tmp7 + tmp5;
      var z4c = tmp6 + tmp4;
      var z5c = (z3c + z4c) * FIX_1_175875602;

      var p0c = tmp7 * FIX_0_298631336;
      var p1c = tmp6 * FIX_2_053119869;
      var p2c = tmp5 * FIX_3_072711026;
      var p3c = tmp4 * FIX_1_501321110;
      z1c *= -FIX_0_899976223;
      z2c *= -FIX_2_562915447;
      z3c = -z3c * FIX_1_961570560 + z5c;
      z4c = -z4c * FIX_0_390180644 + z5c;

      var colOddRound = 1 << (CONST_BITS + PASS1_BITS - 1);
      block[i + 56] = (p0c + z1c + z3c + colOddRound) >> (CONST_BITS + PASS1_BITS);
      block[i + 40] = (p1c + z2c + z4c + colOddRound) >> (CONST_BITS + PASS1_BITS);
      block[i + 24] = (p2c + z2c + z3c + colOddRound) >> (CONST_BITS + PASS1_BITS);
      block[i + 8] = (p3c + z1c + z4c + colOddRound) >> (CONST_BITS + PASS1_BITS);
    }
  }

  /// <summary>Inverse DCT: 8x8 frequency coefficients → 8x8 spatial block. Output clamped to [0,255].</summary>
  public static void InverseDct(short[] coefficients, int[] quantTable, byte[] output, int outputOffset, int outputStride) {
    var workspace = new int[64];

    // Dequantize and reorder from zigzag to natural (row-major) order
    for (var i = 0; i < 64; ++i)
      workspace[JpegZigZag.Order[i]] = coefficients[i] * quantTable[i];

    // Pass 1: process columns from workspace
    for (var col = 0; col < 8; ++col) {
      // Shortcut: if all AC terms are zero, the IDCT is trivial
      if (workspace[col + 8] == 0 && workspace[col + 16] == 0 &&
          workspace[col + 24] == 0 && workspace[col + 32] == 0 &&
          workspace[col + 40] == 0 && workspace[col + 48] == 0 &&
          workspace[col + 56] == 0) {
        var dc = workspace[col] << PASS1_BITS;
        for (var row = 0; row < 8; ++row)
          workspace[col + row * 8] = dc;
        continue;
      }

      var z2 = workspace[col + 16];
      var z3 = workspace[col + 48];
      var z1 = (z2 + z3) * FIX_0_541196100;
      var tmp2 = z1 - z3 * FIX_1_847759065;
      var tmp3 = z1 + z2 * FIX_0_765366865;

      z2 = workspace[col];
      z3 = workspace[col + 32];
      var tmp0 = (z2 + z3) << CONST_BITS;
      var tmp1 = (z2 - z3) << CONST_BITS;

      var tmp10 = tmp0 + tmp3;
      var tmp13 = tmp0 - tmp3;
      var tmp11 = tmp1 + tmp2;
      var tmp12 = tmp1 - tmp2;

      // Odd part
      tmp0 = workspace[col + 56];
      tmp1 = workspace[col + 40];
      tmp2 = workspace[col + 24];
      tmp3 = workspace[col + 8];

      z1 = tmp0 + tmp3;
      z2 = tmp1 + tmp2;
      z3 = tmp0 + tmp2;
      var z4 = tmp1 + tmp3;
      var z5 = (z3 + z4) * FIX_1_175875602;

      tmp0 = tmp0 * FIX_0_298631336;
      tmp1 = tmp1 * FIX_2_053119869;
      tmp2 = tmp2 * FIX_3_072711026;
      tmp3 = tmp3 * FIX_1_501321110;
      z1 = -z1 * FIX_0_899976223;
      z2 = -z2 * FIX_2_562915447;
      z3 = -z3 * FIX_1_961570560;
      z4 = -z4 * FIX_0_390180644;

      z3 += z5;
      z4 += z5;

      tmp0 += z1 + z3;
      tmp1 += z2 + z4;
      tmp2 += z2 + z3;
      tmp3 += z1 + z4;

      var shiftBits = CONST_BITS - PASS1_BITS;
      var round = 1 << (shiftBits - 1);

      workspace[col] = (tmp10 + tmp3 + round) >> shiftBits;
      workspace[col + 56] = (tmp10 - tmp3 + round) >> shiftBits;
      workspace[col + 8] = (tmp11 + tmp2 + round) >> shiftBits;
      workspace[col + 48] = (tmp11 - tmp2 + round) >> shiftBits;
      workspace[col + 16] = (tmp12 + tmp1 + round) >> shiftBits;
      workspace[col + 40] = (tmp12 - tmp1 + round) >> shiftBits;
      workspace[col + 24] = (tmp13 + tmp0 + round) >> shiftBits;
      workspace[col + 32] = (tmp13 - tmp0 + round) >> shiftBits;
    }

    // Pass 2: process rows from workspace, output clamped [0,255]
    for (var row = 0; row < 8; ++row) {
      var rowBase = row * 8;
      var outRow = outputOffset + row * outputStride;

      // Shortcut for all-zero AC row
      if (workspace[rowBase + 1] == 0 && workspace[rowBase + 2] == 0 &&
          workspace[rowBase + 3] == 0 && workspace[rowBase + 4] == 0 &&
          workspace[rowBase + 5] == 0 && workspace[rowBase + 6] == 0 &&
          workspace[rowBase + 7] == 0) {
        var val = _Clamp((workspace[rowBase] + (1 << (PASS1_BITS + 2))) >> (PASS1_BITS + 3));
        var clamped = (byte)(val + 128);
        for (var col = 0; col < 8; ++col)
          output[outRow + col] = clamped;
        continue;
      }

      var z2 = workspace[rowBase + 2];
      var z3 = workspace[rowBase + 6];
      var z1 = (z2 + z3) * FIX_0_541196100;
      var tmp2 = z1 - z3 * FIX_1_847759065;
      var tmp3 = z1 + z2 * FIX_0_765366865;

      z2 = workspace[rowBase];
      z3 = workspace[rowBase + 4];
      var tmp0 = (z2 + z3) << CONST_BITS;
      var tmp1 = (z2 - z3) << CONST_BITS;

      var tmp10 = tmp0 + tmp3;
      var tmp13 = tmp0 - tmp3;
      var tmp11 = tmp1 + tmp2;
      var tmp12 = tmp1 - tmp2;

      // Odd part
      tmp0 = workspace[rowBase + 7];
      tmp1 = workspace[rowBase + 5];
      tmp2 = workspace[rowBase + 3];
      tmp3 = workspace[rowBase + 1];

      z1 = tmp0 + tmp3;
      z2 = tmp1 + tmp2;
      z3 = tmp0 + tmp2;
      var z4 = tmp1 + tmp3;
      var z5 = (z3 + z4) * FIX_1_175875602;

      tmp0 = tmp0 * FIX_0_298631336;
      tmp1 = tmp1 * FIX_2_053119869;
      tmp2 = tmp2 * FIX_3_072711026;
      tmp3 = tmp3 * FIX_1_501321110;
      z1 = -z1 * FIX_0_899976223;
      z2 = -z2 * FIX_2_562915447;
      z3 = -z3 * FIX_1_961570560;
      z4 = -z4 * FIX_0_390180644;

      z3 += z5;
      z4 += z5;

      tmp0 += z1 + z3;
      tmp1 += z2 + z4;
      tmp2 += z2 + z3;
      tmp3 += z1 + z4;

      var shiftBits2 = CONST_BITS + PASS1_BITS + 3;
      var round2 = 1 << (shiftBits2 - 1);

      output[outRow + 0] = _ClampAndShift(tmp10 + tmp3, round2, shiftBits2);
      output[outRow + 7] = _ClampAndShift(tmp10 - tmp3, round2, shiftBits2);
      output[outRow + 1] = _ClampAndShift(tmp11 + tmp2, round2, shiftBits2);
      output[outRow + 6] = _ClampAndShift(tmp11 - tmp2, round2, shiftBits2);
      output[outRow + 2] = _ClampAndShift(tmp12 + tmp1, round2, shiftBits2);
      output[outRow + 5] = _ClampAndShift(tmp12 - tmp1, round2, shiftBits2);
      output[outRow + 3] = _ClampAndShift(tmp13 + tmp0, round2, shiftBits2);
      output[outRow + 4] = _ClampAndShift(tmp13 - tmp0, round2, shiftBits2);
    }
  }

  private static byte _ClampAndShift(int value, int round, int shift) {
    var result = (value + round) >> shift;
    return (byte)Math.Clamp(result + 128, 0, 255);
  }

  private static int _Clamp(int value) => Math.Clamp(value, -128, 127);

  /// <summary>Forward DCT for encoding: takes pixel block (level-shifted by -128), outputs quantized coefficients.</summary>
  public static void ForwardDctQuantize(byte[] pixels, int pixelOffset, int pixelStride, int[] quantTable, short[] coefficients) {
    var block = new int[64];

    // Level shift and copy pixels into block
    for (var row = 0; row < 8; ++row)
      for (var col = 0; col < 8; ++col)
        block[row * 8 + col] = pixels[pixelOffset + row * pixelStride + col] - 128;

    ForwardDct(block);

    // Quantize
    for (var i = 0; i < 64; ++i) {
      var q = quantTable[i];
      // Use rounding division
      if (block[i] >= 0)
        coefficients[i] = (short)((block[i] + (q >> 1)) / q);
      else
        coefficients[i] = (short)(-((-block[i] + (q >> 1)) / q));
    }
  }
}
