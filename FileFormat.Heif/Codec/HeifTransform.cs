using System;
using System.Runtime.CompilerServices;

namespace FileFormat.Heif.Codec;

/// <summary>HEVC inverse transforms: 4x4, 8x8, 16x16, and 32x32 DCT-II and 4x4 DST-VII.
/// Implements the integer butterfly-based transforms from ITU-T H.265 section 8.6.4.</summary>
internal static class HeifTransform {

  // Transform matrix coefficients for HEVC (ITU-T H.265 Table 8-4)
  // 4-point DCT-II
  private static readonly int[,] _DCT4 = {
    { 64,  64,  64,  64 },
    { 83,  36, -36, -83 },
    { 64, -64, -64,  64 },
    { 36, -83,  83, -36 },
  };

  // 4-point DST-VII (for 4x4 luma residuals)
  private static readonly int[,] _DST4 = {
    { 29,  55,  74,  84 },
    { 74,  74,   0, -74 },
    { 84, -29, -74,  55 },
    { 55, -84,  74, -29 },
  };

  // 8-point DCT-II partial (odd part only; even part reuses 4-point DCT)
  private static readonly int[] _DCT8_ODD = [89, 75, 50, 18];

  // 16-point DCT-II odd part
  private static readonly int[] _DCT16_ODD = [90, 87, 80, 70, 57, 43, 25, 9];

  /// <summary>Performs 2D inverse transform, adding the result to the prediction buffer.</summary>
  public static void InverseTransform2D(
    int[] coeffs,
    short[] output,
    int outputOffset,
    int outputStride,
    int n, // transform size (4, 8, 16, 32)
    int bitDepth,
    bool useDst // true for 4x4 luma intra (DST instead of DCT)
  ) {
    var shift1 = 7;
    var shift2 = 20 - bitDepth;
    var maxVal = (1 << bitDepth) - 1;

    // Working buffer
    var intermediate = new int[n * n];

    // Column transforms (vertical)
    for (var x = 0; x < n; ++x) {
      var col = new int[n];
      for (var y = 0; y < n; ++y)
        col[y] = coeffs[y * n + x];

      var transformed = useDst && n == 4 ? _InverseDst4(col) : _InverseDct(col, n);
      for (var y = 0; y < n; ++y)
        intermediate[y * n + x] = (transformed[y] + (1 << (shift1 - 1))) >> shift1;
    }

    // Row transforms (horizontal)
    for (var y = 0; y < n; ++y) {
      var row = new int[n];
      for (var x = 0; x < n; ++x)
        row[x] = intermediate[y * n + x];

      var transformed = useDst && n == 4 ? _InverseDst4(row) : _InverseDct(row, n);
      for (var x = 0; x < n; ++x) {
        var residual = (transformed[x] + (1 << (shift2 - 1))) >> shift2;
        var idx = outputOffset + y * outputStride + x;
        output[idx] = (short)Math.Clamp(output[idx] + residual, 0, maxVal);
      }
    }
  }

  private static int[] _InverseDst4(int[] input) {
    var output = new int[4];
    for (var i = 0; i < 4; ++i) {
      var sum = 0;
      for (var j = 0; j < 4; ++j)
        sum += _DST4[j, i] * input[j];
      output[i] = sum;
    }
    return output;
  }

  private static int[] _InverseDct(int[] input, int n) {
    return n switch {
      4 => _InverseDct4(input),
      8 => _InverseDct8(input),
      16 => _InverseDct16(input),
      32 => _InverseDct32(input),
      _ => _InverseDctGeneric(input, n),
    };
  }

  private static int[] _InverseDct4(int[] input) {
    var output = new int[4];
    for (var i = 0; i < 4; ++i) {
      var sum = 0;
      for (var j = 0; j < 4; ++j)
        sum += _DCT4[j, i] * input[j];
      output[i] = sum;
    }
    return output;
  }

  private static int[] _InverseDct8(int[] input) {
    // Even part: 4-point DCT on even-indexed coefficients
    var even = new int[] { input[0], input[2], input[4], input[6] };
    var evenOut = _InverseDct4(even);

    // Odd part: 4-point butterfly
    var e = input[1];
    var f = input[3];
    var g = input[5];
    var h = input[7];

    var o0 = 89 * e + 75 * f + 50 * g + 18 * h;
    var o1 = 75 * e - 18 * f - 89 * g - 50 * h;
    var o2 = 50 * e - 89 * f + 18 * g + 75 * h;
    var o3 = 18 * e - 50 * f + 75 * g - 89 * h;

    var output = new int[8];
    output[0] = evenOut[0] + o0;
    output[1] = evenOut[1] + o1;
    output[2] = evenOut[2] + o2;
    output[3] = evenOut[3] + o3;
    output[4] = evenOut[3] - o3;
    output[5] = evenOut[2] - o2;
    output[6] = evenOut[1] - o1;
    output[7] = evenOut[0] - o0;

    return output;
  }

  private static int[] _InverseDct16(int[] input) {
    // Even part: 8-point DCT
    var even = new int[8];
    for (var i = 0; i < 8; ++i)
      even[i] = input[i * 2];
    var evenOut = _InverseDct8(even);

    // Odd part
    var oddCoeffs = new int[8];
    for (var i = 0; i < 8; ++i)
      oddCoeffs[i] = input[i * 2 + 1];

    // 16-point odd butterfly coefficients (from H.265 spec)
    var c = new int[] { 90, 87, 80, 70, 57, 43, 25, 9 };
    var oddOut = new int[8];
    for (var i = 0; i < 8; ++i) {
      var sum = 0;
      for (var j = 0; j < 8; ++j) {
        var sign = ((i + j) & 1) == 0 ? 1 : -1;
        var cIdx = j < 4 ? j : 7 - j;
        sum += c[cIdx] * oddCoeffs[j] * sign;
      }
      oddOut[i] = sum;
    }

    var output = new int[16];
    for (var i = 0; i < 8; ++i) {
      output[i] = evenOut[i] + oddOut[i];
      output[15 - i] = evenOut[i] - oddOut[i];
    }
    return output;
  }

  private static int[] _InverseDct32(int[] input) {
    // Even part: 16-point DCT
    var even = new int[16];
    for (var i = 0; i < 16; ++i)
      even[i] = input[i * 2];
    var evenOut = _InverseDct16(even);

    // Odd part: generic matrix multiply
    var oddCoeffs = new int[16];
    for (var i = 0; i < 16; ++i)
      oddCoeffs[i] = input[i * 2 + 1];

    var oddOut = _InverseDctOddGeneric(oddCoeffs, 16);

    var output = new int[32];
    for (var i = 0; i < 16; ++i) {
      output[i] = evenOut[i] + oddOut[i];
      output[31 - i] = evenOut[i] - oddOut[i];
    }
    return output;
  }

  private static int[] _InverseDctOddGeneric(int[] input, int n) {
    var output = new int[n];
    for (var i = 0; i < n; ++i) {
      var sum = 0.0;
      for (var j = 0; j < n; ++j) {
        var angle = Math.PI * (2 * i + 1) * (2 * j + 1) / (4.0 * n);
        sum += input[j] * Math.Cos(angle);
      }
      output[i] = (int)Math.Round(sum);
    }
    return output;
  }

  private static int[] _InverseDctGeneric(int[] input, int n) {
    var output = new int[n];
    for (var i = 0; i < n; ++i) {
      var sum = 0.0;
      for (var k = 0; k < n; ++k) {
        var angle = Math.PI * (2 * i + 1) * k / (2.0 * n);
        var weight = k == 0 ? 1.0 / Math.Sqrt(n) : Math.Sqrt(2.0 / n);
        sum += weight * input[k] * Math.Cos(angle) * 64;
      }
      output[i] = (int)Math.Round(sum);
    }
    return output;
  }
}
