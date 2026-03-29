using System;

namespace FileFormat.Ecw.Codec;

/// <summary>
/// Wavelet coefficient quantizer with per-subband step sizes.
/// Each decomposition level and subband (LL, LH, HL, HH) gets an independent
/// quantization step derived from a base step size scaled by the subband's energy norm.
/// </summary>
internal static class EcwQuantizer {

  /// <summary>Subband types within a wavelet decomposition level.</summary>
  internal enum SubBand {
    LL,
    LH,
    HL,
    HH,
  }

  /// <summary>
  /// Energy normalization weights for CDF 9/7 subbands.
  /// These approximate the L2 norm of the analysis basis functions.
  /// Higher-frequency subbands receive larger quantization steps (coarser quantization).
  /// </summary>
  private static readonly double[] _SubBandWeights = [
    1.0,   // LL - lowest frequency, finest quantization
    1.0,   // LH - horizontal detail
    1.0,   // HL - vertical detail
    1.5,   // HH - diagonal detail, coarsest quantization
  ];

  /// <summary>Level-dependent scaling: higher decomposition levels (coarser resolution) get finer quantization.</summary>
  private const double _LEVEL_DECAY = 0.7;

  /// <summary>
  /// Computes the quantization step size for a given subband at a given decomposition level.
  /// </summary>
  /// <param name="baseStep">Base quantization step (controls overall quality; larger = more compression, more loss).</param>
  /// <param name="level">Decomposition level (0 = finest, increasing = coarser).</param>
  /// <param name="subBand">Which subband within the level.</param>
  /// <returns>The step size for uniform scalar quantization.</returns>
  public static double GetStepSize(double baseStep, int level, SubBand subBand) {
    var weight = _SubBandWeights[(int)subBand];
    var levelScale = Math.Pow(_LEVEL_DECAY, level);
    return Math.Max(baseStep * weight * levelScale, 0.5);
  }

  /// <summary>
  /// Quantizes a rectangular subband region in-place.
  /// Applies uniform scalar quantization: coefficient = round(coefficient / step).
  /// </summary>
  /// <param name="data">The full coefficient array (row-major with given stride).</param>
  /// <param name="stride">Row stride of the coefficient array.</param>
  /// <param name="startX">X origin of the subband region.</param>
  /// <param name="startY">Y origin of the subband region.</param>
  /// <param name="subWidth">Width of the subband region.</param>
  /// <param name="subHeight">Height of the subband region.</param>
  /// <param name="step">Quantization step size.</param>
  /// <param name="quantized">Output integer array (same layout as data) receiving quantized values.</param>
  public static void Quantize(
    double[] data, int stride,
    int startX, int startY, int subWidth, int subHeight,
    double step,
    int[] quantized
  ) {
    var invStep = 1.0 / step;
    for (var y = startY; y < startY + subHeight; ++y)
    for (var x = startX; x < startX + subWidth; ++x) {
      var idx = y * stride + x;
      if (idx < data.Length && idx < quantized.Length)
        quantized[idx] = (int)Math.Round(data[idx] * invStep);
    }
  }

  /// <summary>
  /// Dequantizes a rectangular subband region, writing floating-point results back.
  /// Applies: coefficient = quantized * step.
  /// </summary>
  /// <param name="quantized">Quantized integer coefficient array.</param>
  /// <param name="stride">Row stride.</param>
  /// <param name="startX">X origin of the subband region.</param>
  /// <param name="startY">Y origin of the subband region.</param>
  /// <param name="subWidth">Width of the subband region.</param>
  /// <param name="subHeight">Height of the subband region.</param>
  /// <param name="step">Quantization step size (must match the step used during quantization).</param>
  /// <param name="data">Output double array receiving dequantized values.</param>
  public static void Dequantize(
    int[] quantized, int stride,
    int startX, int startY, int subWidth, int subHeight,
    double step,
    double[] data
  ) {
    for (var y = startY; y < startY + subHeight; ++y)
    for (var x = startX; x < startX + subWidth; ++x) {
      var idx = y * stride + x;
      if (idx < quantized.Length && idx < data.Length)
        data[idx] = quantized[idx] * step;
    }
  }

  /// <summary>
  /// Quantizes all subbands of a wavelet-transformed band.
  /// Iterates over each decomposition level and its LH, HL, HH subbands, plus the final LL.
  /// </summary>
  /// <param name="data">Wavelet coefficients (double, row-major, padded width as stride).</param>
  /// <param name="stride">Row stride (padded width).</param>
  /// <param name="width">Padded image width.</param>
  /// <param name="height">Padded image height.</param>
  /// <param name="levels">Number of decomposition levels.</param>
  /// <param name="baseStep">Base quantization step size.</param>
  /// <returns>Quantized integer coefficients in the same layout.</returns>
  public static int[] QuantizeAllSubBands(double[] data, int stride, int width, int height, int levels, double baseStep) {
    var quantized = new int[data.Length];

    for (var level = 0; level < levels; ++level) {
      var levelW = width >> level;
      var levelH = height >> level;
      if (levelW <= 1 || levelH <= 1)
        break;

      var halfW = levelW / 2;
      var halfH = levelH / 2;

      // LH subband (top-right quadrant at this level)
      var lhStep = GetStepSize(baseStep, level, SubBand.LH);
      Quantize(data, stride, halfW, 0, halfW, halfH, lhStep, quantized);

      // HL subband (bottom-left quadrant at this level)
      var hlStep = GetStepSize(baseStep, level, SubBand.HL);
      Quantize(data, stride, 0, halfH, halfW, halfH, hlStep, quantized);

      // HH subband (bottom-right quadrant at this level)
      var hhStep = GetStepSize(baseStep, level, SubBand.HH);
      Quantize(data, stride, halfW, halfH, halfW, halfH, hhStep, quantized);
    }

    // LL subband at coarsest level
    var dcW = Math.Max(width >> levels, 1);
    var dcH = Math.Max(height >> levels, 1);
    var llStep = GetStepSize(baseStep, levels, SubBand.LL);
    Quantize(data, stride, 0, 0, dcW, dcH, llStep, quantized);

    return quantized;
  }

  /// <summary>
  /// Dequantizes all subbands back to floating-point coefficients.
  /// </summary>
  public static double[] DequantizeAllSubBands(int[] quantized, int stride, int width, int height, int levels, double baseStep) {
    var data = new double[quantized.Length];

    for (var level = 0; level < levels; ++level) {
      var levelW = width >> level;
      var levelH = height >> level;
      if (levelW <= 1 || levelH <= 1)
        break;

      var halfW = levelW / 2;
      var halfH = levelH / 2;

      var lhStep = GetStepSize(baseStep, level, SubBand.LH);
      Dequantize(quantized, stride, halfW, 0, halfW, halfH, lhStep, data);

      var hlStep = GetStepSize(baseStep, level, SubBand.HL);
      Dequantize(quantized, stride, 0, halfH, halfW, halfH, hlStep, data);

      var hhStep = GetStepSize(baseStep, level, SubBand.HH);
      Dequantize(quantized, stride, halfW, halfH, halfW, halfH, hhStep, data);
    }

    var dcW = Math.Max(width >> levels, 1);
    var dcH = Math.Max(height >> levels, 1);
    var llStep = GetStepSize(baseStep, levels, SubBand.LL);
    Dequantize(quantized, stride, 0, 0, dcW, dcH, llStep, data);

    return data;
  }
}
