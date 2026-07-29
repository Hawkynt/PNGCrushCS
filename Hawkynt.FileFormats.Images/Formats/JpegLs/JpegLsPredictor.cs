using System;

namespace FileFormat.JpegLs;

/// <summary>
/// MED (Median Edge Detector) predictor with context-based bias correction for JPEG-LS (ITU-T T.87).
/// Uses the local causal template: a (left), b (above), c (above-left), d (above-right).
/// </summary>
internal static class JpegLsPredictor {

  /// <summary>
  /// Computes the MED prediction from three neighbor samples.
  /// <list type="bullet">
  /// <item><description>If c &gt;= max(a, b) then min(a, b) (horizontal/vertical edge).</description></item>
  /// <item><description>If c &lt;= min(a, b) then max(a, b) (horizontal/vertical edge).</description></item>
  /// <item><description>Otherwise a + b - c (plane predictor).</description></item>
  /// </list>
  /// </summary>
  /// <param name="a">Left neighbor sample (Ra).</param>
  /// <param name="b">Above neighbor sample (Rb).</param>
  /// <param name="c">Above-left neighbor sample (Rc).</param>
  /// <returns>Predicted sample value (unclamped).</returns>
  internal static int Predict(int a, int b, int c) {
    if (c >= Math.Max(a, b))
      return Math.Min(a, b);
    if (c <= Math.Min(a, b))
      return Math.Max(a, b);
    return a + b - c;
  }

  /// <summary>
  /// Computes the corrected prediction by adding the context bias correction value C[Q] and clamping to [0, maxVal].
  /// </summary>
  /// <param name="a">Left neighbor sample (Ra).</param>
  /// <param name="b">Above neighbor sample (Rb).</param>
  /// <param name="c">Above-left neighbor sample (Rc).</param>
  /// <param name="biasCorrection">Context bias correction value C[Q].</param>
  /// <param name="maxVal">Maximum sample value.</param>
  /// <returns>Bias-corrected and clamped predicted sample value.</returns>
  internal static int PredictCorrected(int a, int b, int c, int biasCorrection, int maxVal)
    => Math.Clamp(Predict(a, b, c) + biasCorrection, 0, maxVal);

  /// <summary>
  /// Retrieves the four local causal template neighbors (a, b, c, d) for position (x, y) from a sample buffer.
  /// Out-of-bounds neighbors default to zero.
  /// </summary>
  /// <param name="samples">Flat sample buffer of size width * height.</param>
  /// <param name="width">Row stride in samples.</param>
  /// <param name="height">Number of rows.</param>
  /// <param name="x">Column index.</param>
  /// <param name="y">Row index.</param>
  /// <param name="a">Output: left neighbor (or 0 at left edge).</param>
  /// <param name="b">Output: above neighbor (or 0 at top edge).</param>
  /// <param name="c">Output: above-left neighbor (or 0 at edges).</param>
  /// <param name="d">Output: above-right neighbor (or 0 at edges).</param>
  internal static void GetNeighbors(int[] samples, int width, int height, int x, int y, out int a, out int b, out int c, out int d) {
    var idx = y * width + x;
    a = x > 0 ? samples[idx - 1] : 0;
    b = y > 0 ? samples[idx - width] : 0;
    c = (x > 0 && y > 0) ? samples[idx - width - 1] : 0;
    d = (x < width - 1 && y > 0) ? samples[idx - width + 1] : 0;
  }

  /// <summary>
  /// Computes the three gradient differences from the local causal template.
  /// d1 = d - b (horizontal gradient of above row)
  /// d2 = b - c (vertical gradient)
  /// d3 = c - a (anti-diagonal gradient)
  /// </summary>
  internal static void ComputeGradients(int a, int b, int c, int d, out int d1, out int d2, out int d3) {
    d1 = d - b;
    d2 = b - c;
    d3 = c - a;
  }

  /// <summary>
  /// Performs error quantization for near-lossless mode. For lossless (near=0) this is a no-op.
  /// Quantizes error to the nearest multiple of (2*NEAR+1).
  /// </summary>
  /// <param name="error">Signed prediction error.</param>
  /// <param name="near">Near-lossless parameter (0 for lossless).</param>
  /// <returns>Quantized error value.</returns>
  internal static int QuantizeError(int error, int near) {
    if (near == 0)
      return error;

    var quantStep = 2 * near + 1;
    if (error > 0)
      return (error + near) / quantStep;
    return -((-error + near) / quantStep);
  }

  /// <summary>
  /// Reduces the error modulo the RANGE to keep it within [-RANGE/2, RANGE/2).
  /// </summary>
  /// <param name="error">Signed prediction error (possibly quantized).</param>
  /// <param name="range">Error range: MAXVAL+1 for lossless, or (MAXVAL + 2*NEAR)/(2*NEAR+1) + 1.</param>
  /// <returns>Modulo-reduced error.</returns>
  internal static int ReduceError(int error, int range) {
    if (error < 0)
      error += range;
    if (error >= (range + 1) / 2)
      error -= range;
    return error;
  }

  /// <summary>
  /// Reconstructs the sample value from the predicted value, dequantized error, and clamping parameters.
  /// Used by both encoder (for reference reconstruction) and decoder.
  /// </summary>
  /// <param name="predicted">Predicted sample (after bias correction).</param>
  /// <param name="error">Signed error (before dequantization).</param>
  /// <param name="negative">Whether the context sign was negated.</param>
  /// <param name="near">Near-lossless parameter.</param>
  /// <param name="range">Error value range.</param>
  /// <param name="maxVal">Maximum sample value.</param>
  /// <returns>Reconstructed and clamped sample value.</returns>
  internal static int Reconstruct(int predicted, int error, bool negative, int near, int range, int maxVal) {
    if (negative)
      error = -error;
    if (near > 0)
      error *= 2 * near + 1;

    var reconstructed = predicted + error;
    if (reconstructed < 0)
      reconstructed += range;
    else if (reconstructed > maxVal)
      reconstructed -= range;

    return Math.Clamp(reconstructed, 0, maxVal);
  }
}
