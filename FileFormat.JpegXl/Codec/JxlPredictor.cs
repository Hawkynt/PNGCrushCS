using System;

namespace FileFormat.JpegXl.Codec;

/// <summary>
/// JPEG XL's self-correcting predictor with 14 base modes.
/// Used in modular mode to predict pixel values from neighboring pixels,
/// reducing entropy of the residual signal for better compression.
/// Neighbors: N=north, W=west, NW=northwest, NE=northeast, NN=north-north, WW=west-west.
/// </summary>
internal static class JxlPredictor {

  /// <summary>Number of base predictor modes.</summary>
  public const int ModeCount = 14;

  /// <summary>
  /// Predict a pixel value from its neighbors using the specified mode.
  /// </summary>
  /// <param name="mode">Predictor mode (0..13).</param>
  /// <param name="n">North neighbor (above).</param>
  /// <param name="w">West neighbor (left).</param>
  /// <param name="nw">Northwest neighbor (above-left).</param>
  /// <param name="ne">Northeast neighbor (above-right).</param>
  /// <param name="nn">North-north neighbor (two rows above).</param>
  /// <param name="ww">West-west neighbor (two columns left).</param>
  /// <param name="maxVal">Maximum pixel value for clamping.</param>
  /// <returns>Predicted pixel value.</returns>
  public static int Predict(int mode, int n, int w, int nw, int ne, int nn, int ww, int maxVal) =>
    mode switch {
      0 => 0,                                                  // Zero
      1 => w,                                                  // West
      2 => n,                                                  // North
      3 => (w + n) / 2,                                        // Average W+N
      4 => _Select(n, w, nw),                                  // Select (MED-like)
      5 => _ClampGradient(w + n - nw, maxVal),                 // Gradient (ClampedGrad)
      6 => (_ClampGradient(w + n - nw, maxVal) + w) / 2,       // (Gradient + W) / 2
      7 => (_ClampGradient(w + n - nw, maxVal) + n) / 2,       // (Gradient + N) / 2
      8 => (w + ne) / 2,                                       // Average W+NE
      9 => (w + n + ne + nw + 2) / 4,                          // Average 4 neighbors
      10 => (n + nn) / 2,                                      // Average N+NN (vertical)
      11 => _ClampGradient(w + n - nw, maxVal),                // Gradient (same as 5)
      12 => _ClampGradient(ne + n - nn, maxVal),               // Gradient NE
      13 => _ClampGradient(w + n - nw + (ne - nw) / 2, maxVal),// Gradient with NE hint
      _ => 0,
    };

  /// <summary>
  /// Select predictor: chooses W or N based on which side of NW each falls on.
  /// This is similar to the median-edge detector (MED) from LOCO-I / JPEG-LS.
  /// </summary>
  private static int _Select(int n, int w, int nw) {
    var gradN = Math.Abs(n - nw);
    var gradW = Math.Abs(w - nw);
    return gradN < gradW ? w : n;
  }

  /// <summary>Clamp a gradient prediction to [0, maxVal].</summary>
  private static int _ClampGradient(int v, int maxVal) => Math.Clamp(v, 0, maxVal);

  /// <summary>
  /// Weighted self-correcting predictor: combines multiple base predictions
  /// with adaptive weights that are updated based on prediction errors.
  /// The weights are shifted toward predictors that have lower recent errors.
  /// </summary>
  /// <param name="predictions">Array of base predictions from different modes.</param>
  /// <param name="errors">Accumulated absolute errors for each predictor.</param>
  /// <param name="weights">Current weights for each predictor (updated in-place).</param>
  /// <param name="maxVal">Maximum pixel value for clamping.</param>
  /// <returns>Weighted predicted value.</returns>
  public static int WeightedPredict(int[] predictions, int[] errors, int[] weights, int maxVal) {
    ArgumentNullException.ThrowIfNull(predictions);
    ArgumentNullException.ThrowIfNull(errors);
    ArgumentNullException.ThrowIfNull(weights);

    long weighted = 0;
    long totalWeight = 0;

    for (var i = 0; i < predictions.Length; ++i) {
      // Weight is inversely proportional to accumulated error
      // Use shifted inverse: weight = (1 << 24) / (error + 1)
      var w = weights[i];
      if (w <= 0)
        w = 1;
      weighted += (long)predictions[i] * w;
      totalWeight += w;
    }

    if (totalWeight == 0)
      return predictions.Length > 0 ? predictions[0] : 0;

    var result = (int)((weighted + totalWeight / 2) / totalWeight);
    return Math.Clamp(result, 0, maxVal);
  }

  /// <summary>
  /// Update predictor weights based on the actual pixel value.
  /// Reduces the weight of predictors that were further from the truth
  /// and increases the weight of accurate predictors.
  /// </summary>
  /// <param name="predictions">The predictions that were made.</param>
  /// <param name="actual">The actual pixel value.</param>
  /// <param name="errors">Accumulated error array (updated in-place).</param>
  /// <param name="weights">Weight array (updated in-place).</param>
  public static void UpdateWeights(int[] predictions, int actual, int[] errors, int[] weights) {
    ArgumentNullException.ThrowIfNull(predictions);
    ArgumentNullException.ThrowIfNull(errors);
    ArgumentNullException.ThrowIfNull(weights);

    const int errorDecay = 4; // shift right by 4 = multiply by 15/16

    for (var i = 0; i < predictions.Length; ++i) {
      var err = Math.Abs(predictions[i] - actual);
      // Exponential moving average of errors with decay
      errors[i] = errors[i] - (errors[i] >> errorDecay) + err;
      // Weight is inverse of accumulated error
      weights[i] = (1 << 24) / (errors[i] + 1);
    }
  }

  /// <summary>
  /// Select the best predictor mode for a given pixel based on neighbor context.
  /// Uses a simple heuristic: the gradient predictor tends to work well for
  /// smooth gradients, the select predictor for edges.
  /// </summary>
  /// <param name="n">North neighbor.</param>
  /// <param name="w">West neighbor.</param>
  /// <param name="nw">Northwest neighbor.</param>
  /// <returns>Recommended predictor mode index.</returns>
  public static int SelectBestMode(int n, int w, int nw) {
    var gradN = Math.Abs(n - nw);
    var gradW = Math.Abs(w - nw);

    // If both gradients are small, use average
    if (gradN < 4 && gradW < 4)
      return 3; // Average W+N

    // If vertical gradient dominates, predict from west
    if (gradN > gradW * 2)
      return 1; // West

    // If horizontal gradient dominates, predict from north
    if (gradW > gradN * 2)
      return 2; // North

    // Otherwise use gradient (ClampedGrad)
    return 5; // Gradient
  }
}
