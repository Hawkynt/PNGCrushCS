using System;

namespace FileFormat.Codecs.Mpeg4;

/// <summary>
/// Forms the motion-compensated prediction of one 8x8 block (ISO/IEC 14496-2, 7.6.2).
/// </summary>
/// <remarks>
/// A vector is allowed to point outside the picture, and clause 7.6.4 says what happens when it does:
/// each sample the vector reaches for is clamped, on its own and per component, to the last one
/// inside the coded area. Doing that literally means two comparisons per sample read, on every sample
/// of every predicted block, almost all of which are nowhere near an edge — so the reference pictures
/// carry a padded border of repeated edge samples and the ordinary path reads straight through it.
/// The clamping path below is what handles a vector that reaches past even the border, which the
/// widest motion code allows and no encoder produces.
/// <para/>
/// The rounding of the interpolation is not fixed. Each predicted picture states a rounding type and
/// an encoder alternates it, so that the bias of the interpolation does not accumulate in one
/// direction through a long run of predicted pictures; a decoder that fixed it at either value drifts
/// brighter or darker across a group of pictures, slowly enough to look like the film rather than
/// like a fault.
/// </remarks>
internal static class Mpeg4MotionCompensation {

  /// <summary>
  /// Table 7-6 of ISO/IEC 14496-2: a sixteenth-of-a-sample position, in half-samples.
  /// </summary>
  /// <remarks>
  /// The rounding is towards the half rather than towards the nearest: a sixteenth and an eighth of a
  /// sample round down to nothing, three sixteenths through thirteen round to a half, and only the
  /// last two round up to a whole sample. Rounding to the nearest instead would move every
  /// chrominance vector between three and five sixteenths of a sample onto the other side, which is a
  /// colour smear along one pair of edges of a moving object and not the other.
  /// </remarks>
  private static readonly int[] _SixteenthToHalf = [0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2];

  /// <summary>
  /// Predicts one 8x8 block from a reference plane at half-sample resolution.
  /// </summary>
  /// <param name="prediction">Sixty-four samples, written in raster order.</param>
  /// <param name="plane">The padded reference plane.</param>
  /// <param name="stride">Its row stride, borders included.</param>
  /// <param name="origin">Where sample (0, 0) of the picture sits in the plane.</param>
  /// <param name="border">How far the padded border reaches outside the picture.</param>
  /// <param name="width">The picture's width in this plane.</param>
  /// <param name="height">Its height.</param>
  /// <param name="blockX">The block's left edge in the picture.</param>
  /// <param name="blockY">The block's top edge in the picture.</param>
  /// <param name="vectorX">The horizontal vector in half-sample units.</param>
  /// <param name="vectorY">The vertical vector in half-sample units.</param>
  /// <param name="rounding">The picture's rounding type: zero rounds a half upward, one downward.</param>
  internal static void PredictHalfSample(
    Span<int> prediction, byte[] plane, int stride, int origin, int border, int width, int height,
    int blockX, int blockY, int vectorX, int vectorY, int rounding) {
    // The whole-sample part is an arithmetic shift and not a division: a vector of -3 half-samples is
    // one whole sample to the left plus a half-sample to the right, which is -2 and a half-step
    // forward rather than -1 and a half-step backward.
    var wholeX = vectorX >> 1;
    var wholeY = vectorY >> 1;
    var halfX = vectorX - 2 * wholeX;
    var halfY = vectorY - 2 * wholeY;

    var sourceX = blockX + wholeX;
    var sourceY = blockY + wholeY;

    if (sourceX < -border || sourceY < -border
        || sourceX + 9 > width + border || sourceY + 9 > height + border) {
      _PredictClamped(prediction, plane, stride, origin, width, height, sourceX, sourceY, halfX, halfY, rounding);
      return;
    }

    var source = origin + sourceY * stride + sourceX;

    if (halfX == 0 && halfY == 0) {
      for (var y = 0; y < 8; ++y) {
        var row = source + y * stride;
        for (var x = 0; x < 8; ++x)
          prediction[y * 8 + x] = plane[row + x];
      }

      return;
    }

    if (halfY == 0) {
      for (var y = 0; y < 8; ++y) {
        var row = source + y * stride;
        for (var x = 0; x < 8; ++x)
          prediction[y * 8 + x] = (plane[row + x] + plane[row + x + 1] + 1 - rounding) >> 1;
      }

      return;
    }

    if (halfX == 0) {
      for (var y = 0; y < 8; ++y) {
        var row = source + y * stride;
        var below = row + stride;
        for (var x = 0; x < 8; ++x)
          prediction[y * 8 + x] = (plane[row + x] + plane[below + x] + 1 - rounding) >> 1;
      }

      return;
    }

    for (var y = 0; y < 8; ++y) {
      var row = source + y * stride;
      var below = row + stride;
      for (var x = 0; x < 8; ++x)
        prediction[y * 8 + x] =
          (plane[row + x] + plane[row + x + 1] + plane[below + x] + plane[below + x + 1] + 2 - rounding) >> 2;
    }
  }

  /// <summary>
  /// The same prediction for a vector that reaches past the padded border, clamping each sample as
  /// clause 7.6.4 states it.
  /// </summary>
  private static void _PredictClamped(
    Span<int> prediction, byte[] plane, int stride, int origin, int width, int height,
    int sourceX, int sourceY, int halfX, int halfY, int rounding) {
    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x) {
        var a = _At(plane, stride, origin, width, height, sourceX + x, sourceY + y);

        if (halfX == 0 && halfY == 0) {
          prediction[y * 8 + x] = a;
          continue;
        }

        if (halfY == 0) {
          prediction[y * 8 + x] =
            (a + _At(plane, stride, origin, width, height, sourceX + x + 1, sourceY + y) + 1 - rounding) >> 1;
          continue;
        }

        if (halfX == 0) {
          prediction[y * 8 + x] =
            (a + _At(plane, stride, origin, width, height, sourceX + x, sourceY + y + 1) + 1 - rounding) >> 1;
          continue;
        }

        prediction[y * 8 + x] =
          (a
           + _At(plane, stride, origin, width, height, sourceX + x + 1, sourceY + y)
           + _At(plane, stride, origin, width, height, sourceX + x, sourceY + y + 1)
           + _At(plane, stride, origin, width, height, sourceX + x + 1, sourceY + y + 1) + 2 - rounding) >> 2;
      }
  }

  private static int _At(byte[] plane, int stride, int origin, int width, int height, int x, int y) {
    x = x < 0 ? 0 : x >= width ? width - 1 : x;
    y = y < 0 ? 0 : y >= height ? height - 1 : y;
    return plane[origin + y * stride + x];
  }

  /// <summary>
  /// Averages a forward and a backward prediction, which is what a bidirectionally predicted block's
  /// prediction is.
  /// </summary>
  internal static void Average(Span<int> forward, ReadOnlySpan<int> backward) {
    for (var i = 0; i < 64; ++i)
      forward[i] = (forward[i] + backward[i] + 1) >> 1;
  }

  /// <summary>
  /// Derives one component of the chrominance vector from a macroblock's four luminance vectors
  /// (ISO/IEC 14496-2, 7.6.5 and Table 7-6).
  /// </summary>
  /// <remarks>
  /// The standard states this over the sum of the four rather than over one of them, so that a
  /// macroblock carrying four vectors and one carrying the same vector four times are handled by the
  /// same rule. The sum stands at sixteen times the chrominance vector in whole samples — halved for
  /// the plane being half the resolution, halved again because the luminance vectors are in half
  /// samples, and quartered because there are four of them.
  /// <para/>
  /// Written over the magnitude and signed afterwards, so that a vector and its negation stay mirror
  /// images. Taking the integer part of a negative vector by rounding downward instead would put
  /// leftward and upward motion half a chrominance sample out of step with rightward and downward,
  /// which is a colour smear along one pair of edges of every moving object and not along the other.
  /// </remarks>
  /// <param name="sumOfFour">The four luminance vector components added together, in half-sample units.</param>
  /// <returns>The chrominance vector component, in half-sample units of the chrominance plane.</returns>
  internal static int ToChroma(int sumOfFour) {
    var magnitude = sumOfFour < 0 ? -sumOfFour : sumOfFour;
    var rounded = 2 * (magnitude >> 4) + _SixteenthToHalf[magnitude & 15];
    return sumOfFour < 0 ? -rounded : rounded;
  }
}
