using System;

namespace FileFormat.Codecs.H263;

/// <summary>
/// Forms the motion-compensated prediction of one 8x8 block at half-pixel resolution (ITU-T H.263,
/// clause 6.1.2).
/// </summary>
/// <remarks>
/// The interpolation is bilinear and rounds upward at exactly a half in both directions — the
/// <c>+1</c> and <c>+2</c> of Figure 12. Truncating instead loses about a quarter of a level per
/// predicted picture, which is nothing in one frame and a visible darkening by the end of a long run
/// of them, and it looks like drift rather than like a rounding rule.
/// <para/>
/// A vector that points outside the reference is refused rather than clamped. Baseline H.263 requires
/// every sample a vector reaches to lie inside the coded picture (clause 6.1.1); the mode that lifts
/// that requirement is Annex D, which this decoder refuses at the picture header, so a vector that
/// reaches outside here is a bitstream this decoder has misread and not a picture to be invented.
/// </remarks>
internal static class H263MotionCompensation {

  /// <summary>
  /// Predicts one 8x8 block from a reference plane.
  /// </summary>
  /// <param name="prediction">Sixty-four samples, written in raster order.</param>
  /// <param name="plane">The reference plane.</param>
  /// <param name="planeWidth">Its width; its height follows from its length.</param>
  /// <param name="blockX">The block's left edge in the plane.</param>
  /// <param name="blockY">The block's top edge in the plane.</param>
  /// <param name="vectorX">The horizontal vector in half-pixel units, already scaled for this plane.</param>
  /// <param name="vectorY">The vertical vector in half-pixel units.</param>
  /// <param name="clampToEdge">
  /// Whether a vector reaching outside the reference reads the edge sample instead of being refused,
  /// which is the Unrestricted Motion Vector rule of ITU-T H.263 Annex D.1.
  /// </param>
  /// <returns><c>false</c> when the vector reads outside the reference picture and may not.</returns>
  internal static bool TryPredict(
    Span<int> prediction, byte[] plane, int planeWidth, int blockX, int blockY, int vectorX, int vectorY,
    bool clampToEdge) {
    var planeHeight = plane.Length / planeWidth;

    // The whole-pixel part is an arithmetic shift and not a division: a vector of -3 half-pixels is
    // one whole pixel to the left plus a half-pixel to the right, which is -2 and a half-step forward
    // rather than -1 and a half-step backward.
    var wholeX = vectorX >> 1;
    var wholeY = vectorY >> 1;
    var halfX = vectorX - 2 * wholeX;
    var halfY = vectorY - 2 * wholeY;

    var sourceX = blockX + wholeX;
    var sourceY = blockY + wholeY;

    // Half-pixel interpolation reads one sample past the block in each interpolated direction, so the
    // reach is eight samples plus the half-step and not eight.
    var reachesOutside = sourceX < 0 || sourceY < 0
                         || sourceX + 8 + halfX > planeWidth || sourceY + 8 + halfY > planeHeight;

    if (reachesOutside) {
      if (!clampToEdge)
        return false;

      _PredictFromEdge(prediction, plane, planeWidth, planeHeight, sourceX, sourceY, halfX, halfY);
      return true;
    }

    if (halfX == 0 && halfY == 0) {
      for (var y = 0; y < 8; ++y) {
        var row = (sourceY + y) * planeWidth + sourceX;
        for (var x = 0; x < 8; ++x)
          prediction[y * 8 + x] = plane[row + x];
      }

      return true;
    }

    if (halfY == 0) {
      for (var y = 0; y < 8; ++y) {
        var row = (sourceY + y) * planeWidth + sourceX;
        for (var x = 0; x < 8; ++x)
          prediction[y * 8 + x] = (plane[row + x] + plane[row + x + 1] + 1) >> 1;
      }

      return true;
    }

    if (halfX == 0) {
      for (var y = 0; y < 8; ++y) {
        var row = (sourceY + y) * planeWidth + sourceX;
        var below = row + planeWidth;
        for (var x = 0; x < 8; ++x)
          prediction[y * 8 + x] = (plane[row + x] + plane[below + x] + 1) >> 1;
      }

      return true;
    }

    for (var y = 0; y < 8; ++y) {
      var row = (sourceY + y) * planeWidth + sourceX;
      var below = row + planeWidth;
      for (var x = 0; x < 8; ++x)
        prediction[y * 8 + x] =
          (plane[row + x] + plane[row + x + 1] + plane[below + x] + plane[below + x + 1] + 2) >> 2;
    }

    return true;
  }

  /// <summary>
  /// Predicts a block whose vector reaches outside the reference, by reading the edge sample in place
  /// of the one that is not there (ITU-T H.263, Annex D.1).
  /// </summary>
  /// <remarks>
  /// The clamp is on the sample coordinate and not on the vector, and it is applied to each component
  /// on its own — a vector that leaves the picture at the left but stays inside it vertically is
  /// clamped horizontally only. Clamping the vector instead would move the whole block back inside
  /// and shift the half-pixel phase with it, which is a different prediction and not the one the
  /// encoder made.
  /// <para/>
  /// The clamp happens before the interpolation and on the whole-pixel grid, which is what makes a
  /// half-pixel position just outside the edge come out as the edge sample repeated rather than as
  /// the average of the edge with something that was never coded.
  /// </remarks>
  private static void _PredictFromEdge(
    Span<int> prediction, byte[] plane, int planeWidth, int planeHeight, int sourceX, int sourceY,
    int halfX, int halfY) {
    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x) {
        var a = _At(plane, planeWidth, planeHeight, sourceX + x, sourceY + y);
        if (halfX == 0 && halfY == 0) {
          prediction[y * 8 + x] = a;
          continue;
        }

        if (halfY == 0) {
          prediction[y * 8 + x] = (a + _At(plane, planeWidth, planeHeight, sourceX + x + 1, sourceY + y) + 1) >> 1;
          continue;
        }

        if (halfX == 0) {
          prediction[y * 8 + x] = (a + _At(plane, planeWidth, planeHeight, sourceX + x, sourceY + y + 1) + 1) >> 1;
          continue;
        }

        prediction[y * 8 + x] =
          (a
           + _At(plane, planeWidth, planeHeight, sourceX + x + 1, sourceY + y)
           + _At(plane, planeWidth, planeHeight, sourceX + x, sourceY + y + 1)
           + _At(plane, planeWidth, planeHeight, sourceX + x + 1, sourceY + y + 1) + 2) >> 2;
      }
  }

  private static int _At(byte[] plane, int planeWidth, int planeHeight, int x, int y) {
    x = x < 0 ? 0 : x >= planeWidth ? planeWidth - 1 : x;
    y = y < 0 ? 0 : y >= planeHeight ? planeHeight - 1 : y;
    return plane[y * planeWidth + x];
  }

  /// <summary>
  /// Derives one component of the chrominance vector from the macroblock's luminance vector (ITU-T
  /// H.263, clause 6.1.1 and Table 18).
  /// </summary>
  /// <remarks>
  /// Halving a half-pixel luminance vector leaves a quarter-pixel chrominance one, and the
  /// chrominance planes are only interpolated to halves — so Table 18 says what to do with the two
  /// quarter positions: both of them, a quarter and three quarters, become a half. Not the nearest
  /// half, which would send three quarters up to the next whole pixel; every quarter position becomes
  /// the half of the pixel it is inside.
  /// <para/>
  /// Written over the magnitude and signed afterwards, so that a vector and its negation stay mirror
  /// images. Taking the integer part of a negative quarter-pixel vector by rounding downward instead
  /// would put leftward and upward motion half a chrominance sample out of step with rightward and
  /// downward, which is a colour smear along one pair of edges of every moving object and not along
  /// the other.
  /// </remarks>
  /// <param name="vector">The luminance vector component, in half-pixel units of the luminance plane.</param>
  /// <returns>The chrominance vector component, in half-pixel units of the chrominance plane.</returns>
  internal static int ToChroma(int vector) {
    var magnitude = vector < 0 ? -vector : vector;
    var rounded = 2 * (magnitude >> 2) + ((magnitude & 3) != 0 ? 1 : 0);
    return vector < 0 ? -rounded : rounded;
  }
}
