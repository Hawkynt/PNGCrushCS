using System;

namespace FileFormat.JpegLs;

/// <summary>
/// Main JPEG-LS (LOCO-I / ITU-T T.87) codec state container.
/// Orchestrates context modeling, prediction, Golomb-Rice coding, and run-length mode
/// by composing <see cref="JpegLsContext"/>, <see cref="JpegLsPredictor"/>,
/// <see cref="JpegLsGolombCoder"/>, and <see cref="JpegLsRunMode"/>.
/// </summary>
internal sealed class JpegLsCodec {

  // ---- JPEG markers ----

  internal const ushort MarkerSoi = 0xFFD8;
  internal const ushort MarkerEoi = 0xFFD9;
  internal const ushort MarkerSof55 = 0xFFF7;  // SOF-55: JPEG-LS frame header
  internal const ushort MarkerSos = 0xFFDA;
  internal const ushort MarkerLse = 0xFFF8;     // LSE: JPEG-LS preset parameters

  // ---- LSE types ----

  internal const byte LsePresetParameters = 1;

  // ---- Spec defaults ----

  internal const int DefaultReset = 64;

  // ---- Codec parameters ----

  internal readonly int MaxVal;
  internal readonly int Near;
  internal readonly int T1;
  internal readonly int T2;
  internal readonly int T3;
  internal readonly int Reset;
  internal readonly int Range;
  internal readonly int QBpp;
  internal readonly int BPP;
  internal readonly int Limit;

  /// <summary>Context statistics (A, B, C, N arrays).</summary>
  internal readonly JpegLsContext Context;

  /// <summary>Run index for Golomb run-length coding (indexes into J table).</summary>
  internal int RunIndex;

  internal JpegLsCodec(int maxVal, int near, int t1, int t2, int t3, int reset) {
    MaxVal = maxVal;
    Near = near;
    T1 = t1;
    T2 = t2;
    T3 = t3;
    Reset = reset;

    Range = near == 0
      ? maxVal + 1
      : (maxVal + 2 * near) / (2 * near + 1) + 1;

    BPP = _CeilLog2(maxVal + 1);
    QBpp = _CeilLog2(Range);
    Limit = 2 * (BPP + Math.Max(8, BPP));

    Context = new(maxVal, reset);
  }

  /// <summary>Creates a codec with default thresholds computed from MAXVAL per ITU-T T.87 Annex C.</summary>
  internal static JpegLsCodec CreateDefault(int maxVal, int near) {
    ComputeDefaultThresholds(maxVal, near, out var t1, out var t2, out var t3);
    return new(maxVal, near, t1, t2, t3, DefaultReset);
  }

  /// <summary>Computes default thresholds per ITU-T T.87 Annex C.</summary>
  internal static void ComputeDefaultThresholds(int maxVal, int near, out int t1, out int t2, out int t3) {
    if (maxVal >= 128) {
      var factor = (maxVal + 128) / 256;
      t1 = Math.Max(2, 3 + factor);
      t2 = Math.Max(3, 7 + 2 * factor);
      t3 = Math.Max(4, 21 + 3 * factor);
    } else {
      var factor = Math.Max(2, (maxVal + 2) / 4);
      t1 = factor;
      t2 = Math.Max(factor + 1, 2 * factor);
      t3 = Math.Max(t2 + 1, 3 * factor);
    }

    if (near > 0) {
      t1 = Math.Max(t1, near + 1);
      t2 = Math.Max(t2, near + 1);
      t3 = Math.Max(t3, near + 1);
    }
  }

  /// <summary>
  /// Quantizes three gradients and returns the context index (0..364) and sign flag.
  /// Returns -1 for run mode (all gradients quantize to zero).
  /// </summary>
  internal int QuantizeContext(int d1, int d2, int d3, out bool negative) {
    var q1 = JpegLsContext.QuantizeSingleGradient(d1, T1, T2, T3, Near);
    var q2 = JpegLsContext.QuantizeSingleGradient(d2, T1, T2, T3, Near);
    var q3 = JpegLsContext.QuantizeSingleGradient(d3, T1, T2, T3, Near);
    return JpegLsContext.QuantizeGradients(q1, q2, q3, out negative);
  }

  /// <summary>Clamps a value to [0, MaxVal].</summary>
  internal int Clamp(int value) => Math.Clamp(value, 0, MaxVal);

  /// <summary>
  /// Encodes a regular-mode sample at position (x, y) to the bit stream.
  /// Computes prediction, error, maps and Golomb-encodes, and updates context state.
  /// </summary>
  internal void EncodeRegular(BitWriter writer, int sample, int a, int b, int c, int ctx, bool negative) {
    var predicted = JpegLsPredictor.PredictCorrected(a, b, c, Context.C[ctx], MaxVal);

    var error = sample - predicted;
    if (negative)
      error = -error;

    error = JpegLsPredictor.QuantizeError(error, Near);
    error = JpegLsPredictor.ReduceError(error, Range);

    var k = Context.ComputeK(ctx);
    var inverted = Context.IsMapInverted(k, ctx);
    var mapped = JpegLsGolombCoder.MapError(error, inverted);

    JpegLsGolombCoder.Encode(writer, mapped, k, Limit, QBpp);

    Context.Update(ctx, error);
  }

  /// <summary>
  /// Decodes a regular-mode sample from the bit stream.
  /// Reads Golomb code, unmaps error, reconstructs sample, and updates context state.
  /// </summary>
  internal int DecodeRegular(BitReader reader, int a, int b, int c, int ctx, bool negative) {
    var predicted = JpegLsPredictor.PredictCorrected(a, b, c, Context.C[ctx], MaxVal);

    var k = Context.ComputeK(ctx);
    var mapped = JpegLsGolombCoder.Decode(reader, k, Limit, QBpp);
    var inverted = Context.IsMapInverted(k, ctx);
    var error = JpegLsGolombCoder.UnmapError(mapped, inverted);

    Context.Update(ctx, error);

    return JpegLsPredictor.Reconstruct(predicted, error, negative, Near, Range, MaxVal);
  }

  // ---- Forwarding members for backward compatibility and tests ----

  /// <summary>Number of regular contexts (365).</summary>
  internal const int RegularContextCount = JpegLsContext.RegularContextCount;

  /// <summary>Total contexts: 365 regular + 2 run-interruption.</summary>
  internal const int TotalContextCount = JpegLsContext.TotalContextCount;

  /// <summary>Context index for run-interruption when |Ra - Rb| &lt;= NEAR.</summary>
  internal const int RunContextIndex = JpegLsContext.RunContextIndex;

  /// <summary>Context index for run-interruption when |Ra - Rb| &gt; NEAR.</summary>
  internal const int RunInterruptContextIndex = JpegLsContext.RunInterruptContextIndex;

  /// <summary>Forwarding accessor: sum of absolute errors per context.</summary>
  internal int[] A => Context.A;

  /// <summary>Forwarding accessor: bias accumulator per context.</summary>
  internal int[] B => Context.B;

  /// <summary>Forwarding accessor: bias correction value per context.</summary>
  internal int[] C => Context.C;

  /// <summary>Forwarding accessor: occurrence count per context.</summary>
  internal int[] N => Context.N;

  /// <summary>MED (Median Edge Detector) predictor. Delegates to <see cref="JpegLsPredictor.Predict"/>.</summary>
  internal static int MedPredict(int a, int b, int c) => JpegLsPredictor.Predict(a, b, c);

  /// <summary>Computes the Golomb parameter k. Delegates to <see cref="JpegLsContext.ComputeK"/>.</summary>
  internal int ComputeK(int ctx) => Context.ComputeK(ctx);

  /// <summary>Updates context state. Delegates to <see cref="JpegLsContext.Update"/>.</summary>
  internal void UpdateContext(int ctx, int error) => Context.Update(ctx, error);

  /// <summary>Determines whether the error mapping should be inverted. Delegates to <see cref="JpegLsContext.IsMapInverted"/>.</summary>
  internal bool IsMapInverted(int k, int ctx) => Context.IsMapInverted(k, ctx);

  /// <summary>Gets the Golomb run length parameter J[rk]. Delegates to <see cref="JpegLsRunMode.GetJ"/>.</summary>
  internal static int GetJ(int runIndex) => JpegLsRunMode.GetJ(runIndex);

  /// <summary>Ceil(log2(n)), returns 0 for n &lt;= 1.</summary>
  internal static int CeilLog2(int n) => _CeilLog2(n);

  private static int _CeilLog2(int n) {
    if (n <= 1)
      return 0;
    var k = 0;
    var v = n - 1;
    while (v > 0) {
      v >>= 1;
      ++k;
    }
    return k;
  }
}
