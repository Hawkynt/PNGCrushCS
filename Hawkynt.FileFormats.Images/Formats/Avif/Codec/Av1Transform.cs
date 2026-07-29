using System;
using System.Runtime.CompilerServices;

namespace FileFormat.Avif.Codec;

/// <summary>AV1 transform types. Each is a pair of (row transform, column transform).</summary>
internal enum Av1TxType {
  DctDct = 0,
  AdstDct = 1,
  DctAdst = 2,
  AdstAdst = 3,
  FlipAdstDct = 4,
  DctFlipAdst = 5,
  FlipAdstFlipAdst = 6,
  AdstFlipAdst = 7,
  FlipAdstAdst = 8,
  IdentityIdentity = 9,
  IdentityDct = 10,
  DctIdentity = 11,
  IdentityAdst = 12,
  AdstIdentity = 13,
  IdentityFlipAdst = 14,
  FlipAdstIdentity = 15,
}

/// <summary>AV1 transform sizes.</summary>
internal enum Av1TxSize {
  Tx4x4 = 0,
  Tx8x8 = 1,
  Tx16x16 = 2,
  Tx32x32 = 3,
  Tx64x64 = 4,
  Tx4x8 = 5,
  Tx8x4 = 6,
  Tx8x16 = 7,
  Tx16x8 = 8,
  Tx16x32 = 9,
  Tx32x16 = 10,
  Tx32x64 = 11,
  Tx64x32 = 12,
  Tx4x16 = 13,
  Tx16x4 = 14,
  Tx8x32 = 15,
  Tx32x8 = 16,
  Tx16x64 = 17,
  Tx64x16 = 18,
}

/// <summary>Implements inverse transforms for AV1: DCT, ADST, FlipADST, and Identity
/// for sizes 4x4 through 64x64 and all 16 transform type combinations.</summary>
internal static class Av1Transform {

  private const int _ROUND_SHIFT = 12;
  private const int _NEW_SQRT2 = 5793; // sqrt(2) * (1 << 12)
  private const int _NEW_INV_SQRT2 = 2896; // 1/sqrt(2) * (1 << 12)

  /// <summary>Gets width and height for a given TX size.</summary>
  public static (int Width, int Height) GetTxDimensions(Av1TxSize txSize) => txSize switch {
    Av1TxSize.Tx4x4 => (4, 4),
    Av1TxSize.Tx8x8 => (8, 8),
    Av1TxSize.Tx16x16 => (16, 16),
    Av1TxSize.Tx32x32 => (32, 32),
    Av1TxSize.Tx64x64 => (64, 64),
    Av1TxSize.Tx4x8 => (4, 8),
    Av1TxSize.Tx8x4 => (8, 4),
    Av1TxSize.Tx8x16 => (8, 16),
    Av1TxSize.Tx16x8 => (16, 8),
    Av1TxSize.Tx16x32 => (16, 32),
    Av1TxSize.Tx32x16 => (32, 16),
    Av1TxSize.Tx32x64 => (32, 64),
    Av1TxSize.Tx64x32 => (64, 32),
    Av1TxSize.Tx4x16 => (4, 16),
    Av1TxSize.Tx16x4 => (16, 4),
    Av1TxSize.Tx8x32 => (8, 32),
    Av1TxSize.Tx32x8 => (32, 8),
    Av1TxSize.Tx16x64 => (16, 64),
    Av1TxSize.Tx64x16 => (64, 16),
    _ => throw new NotSupportedException($"Unknown TX size: {txSize}"),
  };

  /// <summary>Performs 2D inverse transform on coefficient buffer, adding result to the prediction buffer.</summary>
  public static void InverseTransform2D(
    int[] coeffs,
    short[] output,
    int outputOffset,
    int outputStride,
    Av1TxType txType,
    Av1TxSize txSize,
    int bitDepth
  ) {
    var (w, h) = GetTxDimensions(txSize);
    _GetRowColTypes(txType, out var colType, out var rowType);

    // Working buffer for intermediate values
    var intermediate = new int[w * h];

    // Column transforms first (vertical)
    for (var x = 0; x < w; ++x) {
      var col = new int[h];
      for (var y = 0; y < h; ++y)
        col[y] = coeffs[y * w + x];

      var transformed = _ApplyTransform1D(col, h, colType);
      for (var y = 0; y < h; ++y)
        intermediate[y * w + x] = _RoundShift(transformed[y], _ROUND_SHIFT - 2);
    }

    // Row transforms (horizontal)
    var maxVal = (1 << bitDepth) - 1;
    for (var y = 0; y < h; ++y) {
      var row = new int[w];
      for (var x = 0; x < w; ++x)
        row[x] = intermediate[y * w + x];

      var transformed = _ApplyTransform1D(row, w, rowType);
      for (var x = 0; x < w; ++x) {
        var residual = _RoundShift(transformed[x], _ROUND_SHIFT + 2);
        var idx = outputOffset + y * outputStride + x;
        output[idx] = (short)Math.Clamp(output[idx] + residual, 0, maxVal);
      }
    }
  }

  private static void _GetRowColTypes(Av1TxType txType, out int colType, out int rowType) {
    // 0=DCT, 1=ADST, 2=FlipADST, 3=Identity
    switch (txType) {
      case Av1TxType.DctDct: colType = 0; rowType = 0; break;
      case Av1TxType.AdstDct: colType = 1; rowType = 0; break;
      case Av1TxType.DctAdst: colType = 0; rowType = 1; break;
      case Av1TxType.AdstAdst: colType = 1; rowType = 1; break;
      case Av1TxType.FlipAdstDct: colType = 2; rowType = 0; break;
      case Av1TxType.DctFlipAdst: colType = 0; rowType = 2; break;
      case Av1TxType.FlipAdstFlipAdst: colType = 2; rowType = 2; break;
      case Av1TxType.AdstFlipAdst: colType = 1; rowType = 2; break;
      case Av1TxType.FlipAdstAdst: colType = 2; rowType = 1; break;
      case Av1TxType.IdentityIdentity: colType = 3; rowType = 3; break;
      case Av1TxType.IdentityDct: colType = 3; rowType = 0; break;
      case Av1TxType.DctIdentity: colType = 0; rowType = 3; break;
      case Av1TxType.IdentityAdst: colType = 3; rowType = 1; break;
      case Av1TxType.AdstIdentity: colType = 1; rowType = 3; break;
      case Av1TxType.IdentityFlipAdst: colType = 3; rowType = 2; break;
      case Av1TxType.FlipAdstIdentity: colType = 2; rowType = 3; break;
      default: colType = 0; rowType = 0; break;
    }
  }

  private static int[] _ApplyTransform1D(int[] input, int n, int type) {
    return type switch {
      0 => _InverseDct(input, n),
      1 => _InverseAdst(input, n),
      2 => _InverseFlipAdst(input, n),
      3 => _InverseIdentity(input, n),
      _ => _InverseDct(input, n),
    };
  }

  /// <summary>Inverse DCT for arbitrary sizes.</summary>
  private static int[] _InverseDct(int[] input, int n) {
    if (n == 4) return _InverseDct4(input);
    if (n == 8) return _InverseDct8(input);
    if (n == 16) return _InverseDct16(input);
    return _InverseDctGeneric(input, n);
  }

  private static int[] _InverseDct4(int[] input) {
    var output = new int[4];

    // Stage 1
    var s0 = input[0];
    var s1 = input[2];
    var s2 = input[1];
    var s3 = input[3];

    // Stage 2 - butterfly
    var a = _HalfBtf(s0, _COS_PI_4, s1, _COS_PI_4);
    var b = _HalfBtf(s0, _COS_PI_4, s1, -_COS_PI_4);
    var c = _HalfBtf(s2, _COS_PI_8_6, s3, _COS_PI_8_2);
    var d = _HalfBtf(s2, _COS_PI_8_2, s3, -_COS_PI_8_6);

    // Stage 3 - output
    output[0] = a + c;
    output[1] = b + d;
    output[2] = b - d;
    output[3] = a - c;

    return output;
  }

  private static int[] _InverseDct8(int[] input) {
    // First do 4-point DCT on even indices
    var even = new int[] { input[0], input[2], input[4], input[6] };
    var evenOut = _InverseDct4(even);

    // 4-point odd part
    var s4 = input[1];
    var s5 = input[3];
    var s6 = input[5];
    var s7 = input[7];

    var a4 = _HalfBtf(s4, _COS_PI_16_1, s7, _COS_PI_16_7);
    var a7 = _HalfBtf(s4, _COS_PI_16_7, s7, -_COS_PI_16_1);
    var a5 = _HalfBtf(s5, _COS_PI_16_5, s6, _COS_PI_16_3);
    var a6 = _HalfBtf(s5, _COS_PI_16_3, s6, -_COS_PI_16_5);

    var b4 = a4 + a5;
    var b5 = a4 - a5;
    var b6 = a7 - a6;
    var b7 = a7 + a6;

    var c5 = _HalfBtf(b6, _COS_PI_4, b5, -_COS_PI_4);
    var c6 = _HalfBtf(b6, _COS_PI_4, b5, _COS_PI_4);

    var output = new int[8];
    output[0] = evenOut[0] + b7;
    output[1] = evenOut[1] + c6;
    output[2] = evenOut[2] + c5;
    output[3] = evenOut[3] + b4;
    output[4] = evenOut[3] - b4;
    output[5] = evenOut[2] - c5;
    output[6] = evenOut[1] - c6;
    output[7] = evenOut[0] - b7;

    return output;
  }

  private static int[] _InverseDct16(int[] input) {
    // 8-point DCT on even indices
    var even = new int[8];
    for (var i = 0; i < 8; ++i)
      even[i] = input[i * 2];
    var evenOut = _InverseDct8(even);

    // 8-point odd butterfly
    var odd = new int[8];
    for (var i = 0; i < 8; ++i)
      odd[i] = input[i * 2 + 1];

    var oddOut = _InverseDctOddHalf(odd, 8);

    var output = new int[16];
    for (var i = 0; i < 8; ++i) {
      output[i] = evenOut[i] + oddOut[7 - i];
      output[15 - i] = evenOut[i] - oddOut[7 - i];
    }
    return output;
  }

  private static int[] _InverseDctOddHalf(int[] input, int n) {
    // Simple butterfly for odd-indexed DCT coefficients
    var output = new int[n];
    for (var i = 0; i < n; ++i) {
      var sum = 0;
      for (var j = 0; j < n; ++j) {
        var angle = Math.PI * (2 * i + 1) * (2 * j + 1) / (4.0 * n);
        sum += (int)(input[j] * Math.Cos(angle));
      }
      output[i] = sum;
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
        sum += weight * input[k] * Math.Cos(angle);
      }
      output[i] = (int)Math.Round(sum);
    }
    return output;
  }

  /// <summary>Inverse ADST (Asymmetric Discrete Sine Transform).</summary>
  private static int[] _InverseAdst(int[] input, int n) {
    if (n == 4) return _InverseAdst4(input);
    return _InverseAdstGeneric(input, n);
  }

  private static int[] _InverseAdst4(int[] input) {
    var s0 = input[0];
    var s1 = input[1];
    var s2 = input[2];
    var s3 = input[3];

    // ADST-4 from AV1 spec
    var sinVal1 = 1321; // sin(pi/9) * 2048
    var sinVal2 = 2482; // sin(2*pi/9) * 2048
    var sinVal3 = 3344; // sin(3*pi/9) * 2048 = sin(pi/3) * 2048
    var sinVal4 = 3803; // sin(4*pi/9) * 2048

    var a0 = sinVal1 * s0 + sinVal2 * s1 + sinVal3 * s2 + sinVal4 * s3;
    var a1 = sinVal4 * s0 + sinVal3 * s1 - sinVal1 * s3 - sinVal2 * s2;
    var a2 = sinVal3 * (s0 - s1 + s3);
    var a3 = sinVal2 * s0 - sinVal4 * s1 + sinVal1 * s2 - sinVal3 * s3;

    var output = new int[4];
    output[0] = (a0 + 1024) >> 11;
    output[1] = (a1 + 1024) >> 11;
    output[2] = (a2 + 1024) >> 11;
    output[3] = (a3 + 1024) >> 11;
    return output;
  }

  private static int[] _InverseAdstGeneric(int[] input, int n) {
    var output = new int[n];
    for (var i = 0; i < n; ++i) {
      var sum = 0.0;
      for (var k = 0; k < n; ++k) {
        var angle = Math.PI * (2 * k + 1) * (2 * i + 1) / (4.0 * n);
        sum += input[k] * Math.Sin(angle);
      }
      output[i] = (int)Math.Round(sum * Math.Sqrt(2.0 / n));
    }
    return output;
  }

  /// <summary>Inverse FlipADST: ADST with reversed output.</summary>
  private static int[] _InverseFlipAdst(int[] input, int n) {
    var adst = _InverseAdst(input, n);
    var output = new int[n];
    for (var i = 0; i < n; ++i)
      output[i] = adst[n - 1 - i];
    return output;
  }

  /// <summary>Identity transform (scaled copy).</summary>
  private static int[] _InverseIdentity(int[] input, int n) {
    var output = new int[n];
    var scale = n switch {
      4 => _NEW_SQRT2,
      8 => 2 * _NEW_SQRT2,
      16 => 2 * _NEW_SQRT2,
      32 => 4 * _NEW_SQRT2,
      64 => 4 * _NEW_SQRT2,
      _ => _NEW_SQRT2,
    };

    for (var i = 0; i < n; ++i)
      output[i] = _RoundShift(input[i] * scale, _ROUND_SHIFT);

    return output;
  }

  // Cosine constants (scaled by 1 << 12)
  private const int _COS_PI_4 = 2896;     // cos(pi/4) * (1 << 12)
  private const int _COS_PI_8_2 = 1567;   // cos(3*pi/8) * (1 << 12)
  private const int _COS_PI_8_6 = 3784;   // cos(pi/8) * (1 << 12)
  private const int _COS_PI_16_1 = 4017;  // cos(pi/16) * (1 << 12)
  private const int _COS_PI_16_3 = 3406;  // cos(3*pi/16) * (1 << 12)
  private const int _COS_PI_16_5 = 2276;  // cos(5*pi/16) * (1 << 12)
  private const int _COS_PI_16_7 = 799;   // cos(7*pi/16) * (1 << 12)

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _HalfBtf(int a, int cosA, int b, int cosB)
    => _RoundShift(a * cosA + b * cosB, _ROUND_SHIFT);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _RoundShift(int value, int shift)
    => (value + (1 << (shift - 1))) >> shift;
}
