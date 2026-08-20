using System;

namespace FileFormat.Codecs.Mpeg1;

/// <summary>
/// Forms the motion-compensated prediction of one 8x8 block, at half-pixel resolution
/// (ISO/IEC 11172-2, 2.4.4.2).
/// </summary>
/// <remarks>
/// Two things here are easy to get subtly wrong and invisible when they are. The first is that a
/// vector is halved twice by different rules: <c>&gt;&gt;1</c> for the whole-pixel part, which rounds
/// a negative vector down, and <c>/2</c> for the chrominance vector, which truncates it towards
/// zero. Using one operator for both puts every chrominance block of every negative vector half a
/// sample out — a colour smear along moving edges that survives every still-frame comparison of the
/// luminance plane.
/// <para/>
/// The second is the rounding of the interpolation, which is upward at exactly a half in both
/// directions. Truncating instead loses about a quarter of a level per predicted picture, which is
/// nothing in one frame and a visible darkening by the end of a long group of pictures — and it
/// looks like drift rather than like a rounding rule.
/// </remarks>
internal static class Mpeg1MotionCompensation {

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
  /// <returns>
  /// <c>false</c> when the vector points off the reference picture, which ISO/IEC 11172-2 does not
  /// permit. The refusal is left to the caller rather than thrown here because the caller is the one
  /// holding the macroblock's position and the direction of the prediction, and a message that names
  /// neither is not much better than none.
  /// </returns>
  internal static bool TryPredict(
    Span<int> prediction, byte[] plane, int planeWidth, int blockX, int blockY, int vectorX, int vectorY) {
    var planeHeight = plane.Length / planeWidth;

    // The whole-pixel part is an arithmetic shift and not a division: for a vector of -3 half-pixels
    // the source is one whole pixel to the left plus a half-pixel to the right, which is -2 and a
    // half-step, not -1 and a half-step backwards.
    var wholeX = vectorX >> 1;
    var wholeY = vectorY >> 1;
    var halfX = vectorX - 2 * wholeX;
    var halfY = vectorY - 2 * wholeY;

    var sourceX = blockX + wholeX;
    var sourceY = blockY + wholeY;

    // Half-pixel interpolation reads one sample past the block in each interpolated direction, so
    // the reach is eight samples plus the half-step and not eight.
    if (sourceX < 0 || sourceY < 0
        || sourceX + 8 + halfX > planeWidth || sourceY + 8 + halfY > planeHeight)
      return false;

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
  /// Averages a forward and a backward prediction, which is what a bidirectionally predicted
  /// macroblock's prediction is.
  /// </summary>
  internal static void Average(Span<int> forward, ReadOnlySpan<int> backward) {
    for (var i = 0; i < 64; ++i)
      forward[i] = (forward[i] + backward[i] + 1) >> 1;
  }

  /// <summary>
  /// Halves a luminance vector into the chrominance one, truncating towards zero.
  /// </summary>
  /// <remarks>
  /// Towards zero, and not the arithmetic shift used for the whole-pixel part of a vector. 11172-2
  /// writes the two with different operators — <c>/</c> and <c>&gt;&gt;</c> — and they disagree for
  /// every odd negative vector, which is half of all leftward and upward motion.
  /// </remarks>
  internal static int ToChroma(int vector) => vector / 2;
}
