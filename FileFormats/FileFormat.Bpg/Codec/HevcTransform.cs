using System;

namespace FileFormat.Bpg.Codec;

/// <summary>Inverse DCT/DST transforms for HEVC: 4x4 DST-VII (intra 4x4), 4x4/8x8/16x16/32x32 DCT-II.</summary>
internal static class HevcTransform {

  // 4x4 DST-VII matrix (for intra 4x4 luma blocks)
  private static readonly int[,] _Dst4 = {
    { 29, 55, 74, 84 },
    { 74, 74,  0,-74 },
    { 84,-29,-74, 55 },
    { 55,-84, 74,-29 },
  };

  // 4x4 DCT-II matrix
  private static readonly int[,] _Dct4 = {
    { 64, 64, 64, 64 },
    { 83, 36,-36,-83 },
    { 64,-64,-64, 64 },
    { 36,-83, 83,-36 },
  };

  // 8x8 DCT-II matrix (even rows = 4x4 DCT-II, odd rows from this table)
  private static readonly int[] _Dct8Even = [ 64, 64, 64, 64, 83, 36, -36, -83, 64, -64, -64, 64, 36, -83, 83, -36 ];
  private static readonly int[] _Dct8Odd = [ 89, 75, 50, 18, 75, -18, -89, -50, 50, -89, 18, 75, 18, -50, 75, -89 ];

  // 16x16 partial factoring constants
  private static readonly int[] _G16 = [
    90, 87, 80, 70, 57, 43, 25, 9,
    87, 57, 9, -43, -80, -90, -70, -25,
    80, 9, -70, -87, -25, 57, 90, 43,
    70, -43, -87, 9, 90, 25, -80, -57,
    57, -80, -25, 90, -9, -87, 43, 70,
    43, -90, 57, 25, -87, 70, 9, -80,
    25, -70, 90, -80, 43, 9, -57, 87,
    9, -25, 43, -57, 70, -80, 87, -90,
  ];

  // 32x32 extended DCT constants
  private static readonly int[] _G32 = [
    90, 90, 88, 85, 82, 78, 73, 67, 61, 54, 46, 38, 31, 22, 13, 4,
    90, 82, 67, 46, 22, -4, -31, -54, -73, -85, -90, -88, -78, -61, -38, -13,
    88, 67, 31, -13, -54, -82, -90, -78, -46, -4, 38, 73, 90, 85, 61, 22,
    85, 46, -13, -67, -90, -73, -22, 38, 82, 88, 54, -4, -61, -90, -78, -31,
    82, 22, -54, -90, -61, 13, 78, 85, 31, -46, -90, -67, 4, 73, 88, 38,
    78, -4, -82, -73, 13, 85, 67, -22, -88, -61, 31, 90, 54, -38, -90, -46,
    73, -31, -90, -22, 78, 67, -38, -90, -13, 82, 61, -46, -88, -4, 85, 54,
    67, -54, -78, 38, 85, -22, -90, 4, 90, 13, -88, -31, 82, 46, -73, -61,
    61, -73, -46, 82, 31, -88, -13, 90, -4, -90, 22, 85, -38, -78, 54, 67,
    54, -85, -4, 88, -46, -61, 82, 13, -90, 38, 67, -78, -22, 90, -31, -73,
    46, -90, 38, 54, -90, 31, 61, -88, 22, 67, -85, 13, 73, -82, 4, 78,
    38, -88, 73, -4, -67, 90, -46, -31, 85, -78, 13, 61, -90, 54, 22, -82,
    31, -78, 90, -61, 4, 54, -88, 82, -38, -22, 73, -90, 67, -13, -46, 85,
    22, -61, 85, -90, 73, -38, -4, 46, -78, 90, -82, 54, -13, -31, 67, -88,
    13, -38, 61, -78, 88, -90, 85, -73, 54, -31, 4, 22, -46, 67, -82, 90,
    4, -13, 22, -31, 38, -46, 54, -61, 67, -73, 78, -82, 85, -88, 90, -90,
  ];

  /// <summary>Performs inverse transform on a block of residual coefficients.</summary>
  /// <param name="coeffs">Input quantized coefficients (size*size, row-major).</param>
  /// <param name="residuals">Output residual samples (size*size, row-major).</param>
  /// <param name="size">Transform size (4, 8, 16, or 32).</param>
  /// <param name="isDst">Whether to use DST (true for 4x4 intra luma).</param>
  /// <param name="bitDepth">Bit depth.</param>
  public static void InverseTransform(int[] coeffs, int[] residuals, int size, bool isDst, int bitDepth) {
    var shift1 = 7;
    var shift2 = 20 - bitDepth;

    var temp = new int[size * size];

    // First pass: inverse transform on columns -> temp
    _InverseTransformPass(coeffs, temp, size, isDst, shift1, isColumn: true);

    // Second pass: inverse transform on rows -> residuals
    _InverseTransformPass(temp, residuals, size, isDst, shift2, isColumn: false);
  }

  private static void _InverseTransformPass(int[] src, int[] dst, int size, bool isDst, int shift, bool isColumn) {
    var round = 1 << (shift - 1);

    for (var i = 0; i < size; ++i) {
      for (var j = 0; j < size; ++j) {
        var sum = 0;
        for (var k = 0; k < size; ++k) {
          int srcVal, matrixVal;
          if (isColumn) {
            srcVal = src[k * size + i]; // k-th row, i-th col
            matrixVal = _GetTransformCoeff(size, isDst, j, k);
          } else {
            srcVal = src[i * size + k]; // i-th row, k-th col
            matrixVal = _GetTransformCoeff(size, isDst, j, k);
          }
          sum += srcVal * matrixVal;
        }

        var val = (sum + round) >> shift;

        if (isColumn)
          dst[j * size + i] = val;
        else
          dst[i * size + j] = val;
      }
    }
  }

  private static int _GetTransformCoeff(int size, bool isDst, int row, int col) => size switch {
    4 when isDst => _Dst4[row, col],
    4 => _Dct4[row, col],
    8 => _GetDct8Coeff(row, col),
    16 => _GetDct16Coeff(row, col),
    32 => _GetDct32Coeff(row, col),
    _ => throw new NotSupportedException($"Transform size {size} not supported."),
  };

  private static int _GetDct8Coeff(int row, int col) {
    // Even rows use 4x4 DCT coefficients (with column mapping)
    if ((row & 1) == 0) {
      var evenRow = row >> 1;
      var evenCol = col;
      // Map 8-point even to 4-point: columns 0,2,4,6 map to 0,1,2,3
      return _Dct8Even[evenRow * 4 + (evenCol < 4 ? evenCol : 7 - evenCol)] *
             (evenCol >= 4 && (evenRow & 1) != 0 ? -1 : evenCol >= 4 && (evenRow == 2) ? -1 : 1);
    }
    // Odd rows use the odd basis
    var oddRow = row >> 1;
    return _Dct8Odd[oddRow * 4 + col] * 1;
  }

  // Full 8x8 DCT-II matrix for direct lookup (more reliable than factored)
  private static readonly int[,] _FullDct8 = {
    { 64, 64, 64, 64, 64, 64, 64, 64 },
    { 89, 75, 50, 18,-18,-50,-75,-89 },
    { 83, 36,-36,-83,-83,-36, 36, 83 },
    { 75,-18,-89,-50, 50, 89, 18,-75 },
    { 64,-64,-64, 64, 64,-64,-64, 64 },
    { 50,-89, 18, 75,-75,-18, 89,-50 },
    { 36,-83, 83,-36,-36, 83,-83, 36 },
    { 18,-50, 75,-89, 89,-75, 50,-18 },
  };

  // Full 16x16 DCT-II matrix
  private static readonly int[,] _FullDct16 = new int[16, 16];
  private static readonly int[,] _FullDct32 = new int[32, 32];

  static HevcTransform() {
    // Build full 16x16 matrix
    for (var r = 0; r < 16; ++r)
      for (var c = 0; c < 16; ++c)
        if ((r & 1) == 0)
          _FullDct16[r, c] = _GetDct8CoeffDirect(r >> 1, c < 8 ? c : 15 - c) * ((c >= 8 && _IsNegate16(r >> 1, c)) ? -1 : 1);
        else
          _FullDct16[r, c] = _G16[(r >> 1) * 8 + (c < 8 ? c : 15 - c)] * (c >= 8 ? (((r >> 1) & 1) == 0 ? -1 : 1) : 1);

    // Override with direct computation for correctness
    _BuildFullDctMatrix(_FullDct16, 16);
    _BuildFullDctMatrix(_FullDct32, 32);
  }

  private static void _BuildFullDctMatrix(int[,] matrix, int n) {
    // Use the HEVC-specified integer cosine transform basis
    // T[k][n] = C_k * cos( pi*(2n+1)*k / (2N) ) scaled and rounded
    // For HEVC, use the known coefficient tables directly
    if (n == 16) {
      // Even rows: derived from 8x8 DCT
      for (var r = 0; r < 8; ++r)
        for (var c = 0; c < 16; ++c) {
          var halfC = c < 8 ? c : 15 - c;
          var baseVal = _FullDct8[r, halfC];
          var negate = c >= 8 && (r % 2 != 0 || (r == 2 || r == 6));
          // Simpler: use butterfly property
          if (c < 8)
            matrix[r * 2, c] = baseVal;
          else
            matrix[r * 2, c] = ((r & 1) == 0) ? baseVal : -baseVal;
        }

      // Odd rows: from _G16
      for (var r = 0; r < 8; ++r)
        for (var c = 0; c < 16; ++c) {
          var halfC = c < 8 ? c : 15 - c;
          matrix[r * 2 + 1, c] = _G16[r * 8 + halfC] * (c >= 8 ? -1 : 1);
        }

      // Actually, let's just use a direct known matrix from the HEVC spec
      _FillDct16Known(matrix);
    }

    if (n == 32)
      _FillDct32Known(matrix);
  }

  private static void _FillDct16Known(int[,] m) {
    int[][] rows = [
      [ 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64, 64],
      [ 90, 87, 80, 70, 57, 43, 25,  9, -9,-25,-43,-57,-70,-80,-87,-90],
      [ 89, 75, 50, 18,-18,-50,-75,-89,-89,-75,-50,-18, 18, 50, 75, 89],
      [ 87, 57,  9,-43,-80,-90,-70,-25, 25, 70, 90, 80, 43, -9,-57,-87],
      [ 83, 36,-36,-83,-83,-36, 36, 83, 83, 36,-36,-83,-83,-36, 36, 83],
      [ 80,  9,-70,-87,-25, 57, 90, 43,-43,-90,-57, 25, 87, 70, -9,-80],
      [ 75,-18,-89,-50, 50, 89, 18,-75,-75, 18, 89, 50,-50,-89,-18, 75],
      [ 70,-43,-87,  9, 90, 25,-80,-57, 57, 80,-25,-90, -9, 87, 43,-70],
      [ 64,-64,-64, 64, 64,-64,-64, 64, 64,-64,-64, 64, 64,-64,-64, 64],
      [ 57,-80,-25, 90, -9,-87, 43, 70,-70,-43, 87,  9,-90, 25, 80,-57],
      [ 50,-89, 18, 75,-75,-18, 89,-50,-50, 89,-18,-75, 75, 18,-89, 50],
      [ 43,-90, 57, 25,-87, 70,  9,-80, 80, -9,-70, 87,-25,-57, 90,-43],
      [ 36,-83, 83,-36,-36, 83,-83, 36, 36,-83, 83,-36,-36, 83,-83, 36],
      [ 25,-70, 90,-80, 43,  9,-57, 87,-87, 57, -9,-43, 80,-90, 70,-25],
      [ 18,-50, 75,-89, 89,-75, 50,-18,-18, 50,-75, 89,-89, 75,-50, 18],
      [  9,-25, 43,-57, 70,-80, 87,-90, 90,-87, 80,-70, 57,-43, 25, -9],
    ];
    for (var r = 0; r < 16; ++r)
      for (var c = 0; c < 16; ++c)
        m[r, c] = rows[r][c];
  }

  private static void _FillDct32Known(int[,] m) {
    // Build 32x32 DCT using the standard HEVC integer DCT construction:
    // Even rows [0,2,4,...,30] come from the 16x16 DCT
    // Odd rows [1,3,5,...,31] come from _G32
    for (var r = 0; r < 16; ++r)
      for (var c = 0; c < 32; ++c) {
        var halfC = c < 16 ? c : 31 - c;
        m[r * 2, c] = _FullDct16[r, halfC] * (c >= 16 && (r % 2 != 0) ? -1 : c >= 16 && (r % 2 == 0) ? 1 : 1);
      }

    // Fix even rows with butterfly
    for (var r = 0; r < 16; ++r)
      for (var c = 16; c < 32; ++c)
        m[r * 2, c] = (r % 2 == 0) ? _FullDct16[r, 31 - c] : -_FullDct16[r, 31 - c];

    // Odd rows from _G32
    for (var r = 0; r < 16; ++r)
      for (var c = 0; c < 32; ++c) {
        var halfC = c < 16 ? c : 31 - c;
        m[r * 2 + 1, c] = _G32[r * 16 + halfC] * (c >= 16 ? -1 : 1);
      }
  }

  // Override the factored 8x8 lookup with direct matrix access
  private static int _GetDct8CoeffDirect(int row, int col) => _FullDct8[row, col];

  private static int _GetDct16Coeff(int row, int col) => _FullDct16[row, col];
  private static int _GetDct32Coeff(int row, int col) => _FullDct32[row, col];

  private static bool _IsNegate16(int evenRow, int col) =>
    // Butterfly negation pattern for 16-point from 8-point
    (evenRow & 1) != 0;
}
