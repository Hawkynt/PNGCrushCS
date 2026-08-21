using System;

namespace FileFormat.Codecs.Mpeg;

/// <summary>
/// Forms the motion-compensated prediction of a rectangle of samples, at half-pixel resolution
/// (ISO/IEC 11172-2, 2.4.4.2 and ISO/IEC 13818-2, 7.6.4).
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
/// <para/>
/// Both the source and the destination are described by a stride and a starting offset rather than
/// by a width, and that is what makes field prediction possible without a second copy of the
/// interpolation. A field of a frame-stored picture is the same samples at twice the stride starting
/// one row in, so predicting the top field of a macroblock from the bottom field of a reference is
/// this same routine with two strides doubled — not a special case, and so not a place where the
/// rounding can quietly come out different.
/// </remarks>
internal static class MpegMotionCompensation {

  /// <summary>
  /// Predicts a rectangle of samples from a reference picture.
  /// </summary>
  /// <param name="destination">Where the samples are written.</param>
  /// <param name="destinationStride">How far apart the destination's rows are.</param>
  /// <param name="destinationStart">The index in <paramref name="destination"/> of its first sample.</param>
  /// <param name="reference">The reference plane.</param>
  /// <param name="referenceStride">How far apart the reference picture's rows are: the plane's width
  /// for a frame, twice that for one field of it.</param>
  /// <param name="referenceStart">The index in <paramref name="reference"/> of its first sample:
  /// zero for a frame or for the top field, one row down for the bottom field.</param>
  /// <param name="referenceWidth">How wide the reference picture is.</param>
  /// <param name="referenceRows">How many rows it has, which for a field is half the plane's.</param>
  /// <param name="blockX">The rectangle's left edge in the reference picture's coordinates.</param>
  /// <param name="blockY">Its top edge.</param>
  /// <param name="width">How wide the rectangle is.</param>
  /// <param name="height">How tall.</param>
  /// <param name="vectorX">The horizontal vector in half-pixel units, already scaled for this plane.</param>
  /// <param name="vectorY">The vertical vector in half-pixel units of this picture's rows.</param>
  /// <returns>
  /// <c>false</c> when the vector points off the reference picture, which neither standard permits.
  /// The refusal is left to the caller rather than thrown here because the caller is the one holding
  /// the macroblock's position and the direction of the prediction, and a message that names neither
  /// is not much better than none.
  /// </returns>
  internal static bool TryPredict(
    Span<int> destination, int destinationStride, int destinationStart,
    byte[] reference, int referenceStride, int referenceStart, int referenceWidth, int referenceRows,
    int blockX, int blockY, int width, int height, int vectorX, int vectorY) {

    // The whole-pixel part is an arithmetic shift and not a division: for a vector of -3 half-pixels
    // the source is one whole pixel to the left plus a half-pixel to the right, which is -2 and a
    // half-step, not -1 and a half-step backwards.
    var wholeX = vectorX >> 1;
    var wholeY = vectorY >> 1;
    var halfX = vectorX - 2 * wholeX;
    var halfY = vectorY - 2 * wholeY;

    var sourceX = blockX + wholeX;
    var sourceY = blockY + wholeY;

    // Half-pixel interpolation reads one sample past the rectangle in each interpolated direction,
    // so the reach is the width plus the half-step and not the width.
    if (sourceX < 0 || sourceY < 0
        || sourceX + width + halfX > referenceWidth || sourceY + height + halfY > referenceRows)
      return false;

    var origin = referenceStart + sourceY * referenceStride + sourceX;

    if (halfX == 0 && halfY == 0) {
      for (var y = 0; y < height; ++y) {
        var row = origin + y * referenceStride;
        var target = destinationStart + y * destinationStride;
        for (var x = 0; x < width; ++x)
          destination[target + x] = reference[row + x];
      }

      return true;
    }

    if (halfY == 0) {
      for (var y = 0; y < height; ++y) {
        var row = origin + y * referenceStride;
        var target = destinationStart + y * destinationStride;
        for (var x = 0; x < width; ++x)
          destination[target + x] = (reference[row + x] + reference[row + x + 1] + 1) >> 1;
      }

      return true;
    }

    if (halfX == 0) {
      for (var y = 0; y < height; ++y) {
        var row = origin + y * referenceStride;
        var below = row + referenceStride;
        var target = destinationStart + y * destinationStride;
        for (var x = 0; x < width; ++x)
          destination[target + x] = (reference[row + x] + reference[below + x] + 1) >> 1;
      }

      return true;
    }

    for (var y = 0; y < height; ++y) {
      var row = origin + y * referenceStride;
      var below = row + referenceStride;
      var target = destinationStart + y * destinationStride;
      for (var x = 0; x < width; ++x)
        destination[target + x] =
          (reference[row + x] + reference[row + x + 1] + reference[below + x] + reference[below + x + 1] + 2) >> 2;
    }

    return true;
  }

  /// <summary>
  /// Averages a forward and a backward prediction, which is what a bidirectionally predicted
  /// macroblock's prediction is.
  /// </summary>
  internal static void Average(Span<int> forward, ReadOnlySpan<int> backward) {
    for (var i = 0; i < forward.Length; ++i)
      forward[i] = (forward[i] + backward[i] + 1) >> 1;
  }

  /// <summary>
  /// Halves a luminance vector into the chrominance one, truncating towards zero.
  /// </summary>
  /// <remarks>
  /// Towards zero, and not the arithmetic shift used for the whole-pixel part of a vector. Both
  /// standards write the two with different operators — <c>/</c> and <c>&gt;&gt;</c> — and they
  /// disagree for every odd negative vector, which is half of all leftward and upward motion.
  /// <para/>
  /// Which components get halved is the chrominance format's business and not this function's: 4:2:0
  /// halves both, 4:2:2 halves the horizontal one only because its chrominance planes are full
  /// height, and 4:4:4 halves neither.
  /// </remarks>
  internal static int ToChroma(int vector) => vector / 2;
}
