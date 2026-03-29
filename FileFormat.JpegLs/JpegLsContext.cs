using System;

namespace FileFormat.JpegLs;

/// <summary>
/// Context quantization and statistics for JPEG-LS (ITU-T T.87).
/// Manages the 365 regular contexts plus 2 run-interruption contexts,
/// with A[] (sum of absolute errors), B[] (bias accumulator), C[] (bias correction), and N[] (occurrence count) arrays.
/// </summary>
internal sealed class JpegLsContext {

  /// <summary>Number of regular contexts derived from 3-gradient quantization (9^3/2 + 1 - 1 = 364, 0-based).</summary>
  internal const int RegularContextCount = 365;

  /// <summary>Total contexts: 365 regular + 2 run-interruption.</summary>
  internal const int TotalContextCount = RegularContextCount + 2;

  /// <summary>Context index for run-interruption when |Ra - Rb| &lt;= NEAR.</summary>
  internal const int RunContextIndex = 365;

  /// <summary>Context index for run-interruption when |Ra - Rb| &gt; NEAR.</summary>
  internal const int RunInterruptContextIndex = 366;

  /// <summary>Sum of absolute prediction errors per context.</summary>
  internal readonly int[] A;

  /// <summary>Bias accumulator (sum of signed errors) per context.</summary>
  internal readonly int[] B;

  /// <summary>Bias correction value per context (clamped to [-128, 127]).</summary>
  internal readonly int[] C;

  /// <summary>Occurrence count per context.</summary>
  internal readonly int[] N;

  private readonly int _reset;

  internal JpegLsContext(int maxVal, int reset) {
    _reset = reset;

    A = new int[TotalContextCount];
    B = new int[TotalContextCount];
    C = new int[TotalContextCount];
    N = new int[TotalContextCount];

    var initA = Math.Max(2, (maxVal + 32) / 64);
    for (var i = 0; i < TotalContextCount; ++i) {
      A[i] = initA;
      N[i] = 1;
    }
  }

  /// <summary>
  /// Quantizes three gradient differences into a flat context index (0..364) and a sign flag.
  /// Returns -1 when all quantized gradients are zero (signals run mode).
  /// </summary>
  internal static int QuantizeGradients(int q1, int q2, int q3, out bool negative) {
    if (q1 == 0 && q2 == 0 && q3 == 0) {
      negative = false;
      return -1;
    }

    // Context merging: negate all if the first non-zero gradient is negative
    if (q1 < 0 || (q1 == 0 && q2 < 0) || (q1 == 0 && q2 == 0 && q3 < 0)) {
      q1 = -q1;
      q2 = -q2;
      q3 = -q3;
      negative = true;
    } else
      negative = false;

    return _FlatContextIndex(q1, q2, q3);
  }

  /// <summary>
  /// Quantizes a single gradient value using the provided thresholds (T1, T2, T3) and NEAR parameter.
  /// Returns a value in [-4, 4].
  /// </summary>
  internal static int QuantizeSingleGradient(int d, int t1, int t2, int t3, int near) {
    if (d <= -t3) return -4;
    if (d <= -t2) return -3;
    if (d <= -t1) return -2;
    if (d < -near) return -1;
    if (d <= near) return 0;
    if (d < t1) return 1;
    if (d < t2) return 2;
    if (d < t3) return 3;
    return 4;
  }

  /// <summary>Computes the Golomb parameter k for a given context index: smallest k where N[ctx] &lt;&lt; k &gt;= A[ctx].</summary>
  internal int ComputeK(int ctx) {
    var n = N[ctx];
    var a = A[ctx];
    var k = 0;
    while (n << k < a && k < 24)
      ++k;
    return k;
  }

  /// <summary>Determines whether the error mapping should be inverted for a given context and k value.</summary>
  internal bool IsMapInverted(int k, int ctx) => k == 0 && 2 * B[ctx] <= -N[ctx];

  /// <summary>Updates context state after encoding/decoding an error value, including bias correction and halving.</summary>
  internal void Update(int ctx, int error) {
    A[ctx] += Math.Abs(error);
    B[ctx] += error;

    // Bias correction update (ITU-T T.87 Section A.6.1)
    if (B[ctx] <= -N[ctx]) {
      if (C[ctx] > -128)
        --C[ctx];
      B[ctx] += N[ctx];
      if (B[ctx] <= -N[ctx])
        B[ctx] = -N[ctx] + 1;
    } else if (B[ctx] > 0) {
      if (C[ctx] < 127)
        ++C[ctx];
      B[ctx] -= N[ctx];
      if (B[ctx] > 0)
        B[ctx] = 0;
    }

    ++N[ctx];

    // Halving when N reaches RESET (ITU-T T.87 Section A.6.1)
    if (N[ctx] == _reset) {
      A[ctx] >>= 1;
      if (A[ctx] == 0)
        A[ctx] = 1;
      B[ctx] >>= 1;
      N[ctx] >>= 1;
      if (N[ctx] == 0)
        N[ctx] = 1;
    }
  }

  /// <summary>Maps quantized gradients (q1 in [0..4], q2 in [-4..4], q3 in [-4..4]) to flat context index 0..363.</summary>
  private static int _FlatContextIndex(int q1, int q2, int q3) {
    // q1 > 0: (q1-1)*81 + (q2+4)*9 + (q3+4) => range [0..323]
    if (q1 > 0)
      return (q1 - 1) * 81 + (q2 + 4) * 9 + (q3 + 4);
    // q1 == 0, q2 > 0: 324 + (q2-1)*9 + (q3+4) => range [324..359]
    if (q2 > 0)
      return 324 + (q2 - 1) * 9 + (q3 + 4);
    // q1 == 0, q2 == 0, q3 > 0: 360 + (q3-1) => range [360..363]
    return 360 + (q3 - 1);
  }
}
