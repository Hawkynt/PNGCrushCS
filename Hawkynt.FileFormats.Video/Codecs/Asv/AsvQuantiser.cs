using System;
using FileFormat.Codecs.Mpeg;

namespace FileFormat.Codecs.Asv;

/// <summary>
/// The dequantisation of asv1.txt 3.5 and 3.6 read backwards: what a block's coded direct-current
/// field and coefficient levels have to be for the decoder to reconstruct the coefficients wanted.
/// </summary>
/// <remarks>
/// The decoder's arithmetic is <c>c' = (level * floor(D * q[i] / QP)) >> 4</c> with <c>D</c> sixty-four
/// for ASV1 and a hundred and twenty-eight for ASV2, <c>q</c> ISO/IEC 11172-2's own intra matrix and
/// <c>QP</c> the stream's one quantisation parameter. That shift is an arithmetic one and so rounds
/// down, on both signs, which puts the reconstruction half a step below where the plain quotient
/// would place it. Rather than correct for that with a bias — which is right on average and wrong at
/// both ends, and which turns a coefficient of nought into a coefficient of one wherever the step is
/// finer than sixteen — this evaluates the decoder's own arithmetic for the two levels that can be
/// best and keeps whichever reconstructs closer, preferring the smaller magnitude on a tie because it
/// is the cheaper code. The reconstruction is monotone in the level, so no third candidate can win.
/// <para/>
/// A level is limited to what the coding can state: both codecs escape to an eight-bit two's
/// complement value, so nothing outside <c>-128 .. 127</c> is sayable and a coefficient asking for
/// more is clamped rather than wrapped — wrapping would turn the loudest coefficient in a block into
/// its own negation, which is a defect that looks like corruption rather than like coarse
/// quantisation.
/// </remarks>
internal static class AsvQuantiser {

  /// <summary>The widest level either codec's escape can state.</summary>
  private const int _MAX_LEVEL = 127;

  /// <summary>The narrowest.</summary>
  private const int _MIN_LEVEL = -128;

  /// <summary>
  /// Builds <c>floor(D * q[i] / QP)</c> for every raster position — the same factors the decoders
  /// build, from the same matrix, so the two sides cannot drift apart.
  /// </summary>
  internal static int[] DequantFactors(int scale, int quantiser) {
    var factors = new int[64];
    for (var i = 0; i < 64; ++i)
      factors[i] = scale * MpegQuantisation.DefaultIntraMatrix[i] / quantiser;

    return factors;
  }

  /// <summary>The eight-bit direct-current field for a block, clause 3.5's <c>c00' = 8 * c00</c>.</summary>
  internal static int DirectCurrent(double coefficient) {
    var value = (int)Math.Floor(coefficient / 8d + 0.5d);
    return value < 0 ? 0 : value > 255 ? 255 : value;
  }

  /// <summary>
  /// Quantises the sixty-three alternating-current coefficients into the levels a block codes, leaving
  /// position zero at nought because clause 3.3 states it is always carried by the separate
  /// direct-current field instead.
  /// </summary>
  internal static void Levels(scoped ReadOnlySpan<double> coefficients, ReadOnlySpan<int> dequantFactors, scoped Span<int> levels) {
    levels[0] = 0;

    for (var i = 1; i < 64; ++i) {
      var factor = dequantFactors[i];
      if (factor <= 0) {
        levels[i] = 0;
        continue;
      }

      var coefficient = coefficients[i];
      var lower = (int)Math.Floor(coefficient * 16d / factor);
      var level = _Closer(coefficient, lower, factor);
      levels[i] = level < _MIN_LEVEL ? _MIN_LEVEL : level > _MAX_LEVEL ? _MAX_LEVEL : level;
    }
  }

  /// <summary>Which of two neighbouring levels the decoder would reconstruct closer to the coefficient.</summary>
  private static int _Closer(double coefficient, int lower, int factor) {
    var lowerError = Math.Abs(((lower * factor) >> 4) - coefficient);
    var upperError = Math.Abs((((lower + 1) * factor) >> 4) - coefficient);

    if (lowerError < upperError)
      return lower;

    if (upperError < lowerError)
      return lower + 1;

    return Math.Abs(lower) <= Math.Abs(lower + 1) ? lower : lower + 1;
  }
}
