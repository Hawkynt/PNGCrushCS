using System;

namespace FileFormat.Jpeg2000.Codec;

/// <summary>Dequantization of wavelet coefficients for reversible (5/3) and irreversible (9/7) JPEG 2000 transforms (ITU-T T.800 Annex E).</summary>
internal static class Jp2Dequantizer {

  /// <summary>Apply reversible (lossless 5/3) dequantization in-place. For the reversible path, coefficients are integers and no step size is applied.</summary>
  /// <param name="coeffs">Integer wavelet coefficients from EBCOT decoding.</param>
  /// <param name="guardBits">Number of guard bits (from Sqcd).</param>
  public static void DequantizeReversible(int[,] coeffs, int guardBits) {
    // For reversible (5/3) transform, no actual dequantization is needed.
    // The decoded integer coefficients are used directly.
    // Guard bits only affect the dynamic range available, not the coefficient values.
    _ = guardBits;
  }

  /// <summary>Apply irreversible (lossy 9/7) dequantization, producing float coefficients.</summary>
  /// <param name="coeffs">Integer wavelet coefficients from EBCOT decoding.</param>
  /// <param name="stepSize">Quantization step size delta_b from QCD marker.</param>
  /// <returns>Dequantized floating-point coefficients.</returns>
  public static float[,] DequantizeIrreversible(int[,] coeffs, float stepSize) {
    var height = coeffs.GetLength(0);
    var width = coeffs.GetLength(1);
    var result = new float[height, width];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        result[y, x] = coeffs[y, x] * stepSize;

    return result;
  }

  /// <summary>Compute the quantization step size from the exponent and mantissa in the QCD marker (ITU-T T.800 Equation E-3).</summary>
  /// <param name="exponent">Exponent (epsilon) value from SPqcd.</param>
  /// <param name="mantissa">Mantissa (mu) value from SPqcd.</param>
  /// <returns>Step size delta.</returns>
  public static float ComputeStepSize(int exponent, int mantissa) =>
    (1.0f + mantissa / 2048.0f) * MathF.Pow(2.0f, -(exponent));

  /// <summary>Forward quantization for irreversible transform: quantize float coefficients to integers.</summary>
  /// <param name="coeffs">Float wavelet coefficients.</param>
  /// <param name="stepSize">Quantization step size.</param>
  /// <returns>Quantized integer coefficients.</returns>
  public static int[,] QuantizeIrreversible(float[,] coeffs, float stepSize) {
    var height = coeffs.GetLength(0);
    var width = coeffs.GetLength(1);
    var result = new int[height, width];
    var invStep = 1.0f / stepSize;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var val = coeffs[y, x] * invStep;
        result[y, x] = val >= 0 ? (int)(val + 0.5f) : -(int)(-val + 0.5f);
      }

    return result;
  }
}
