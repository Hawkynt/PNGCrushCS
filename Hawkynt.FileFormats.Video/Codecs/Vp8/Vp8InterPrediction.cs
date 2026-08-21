using System;

namespace FileFormat.Codecs.Vp8;

/// <summary>
/// Builds the prediction for an inter-coded macroblock out of a reference frame (RFC 6386, 18).
/// </summary>
/// <remarks>
/// A motion vector is in eighths of a pixel. Its whole part moves the origin of the block being read
/// and its fractional part chooses a pair of one-dimensional filters — horizontal first over nine
/// rows, then vertical over the six of those rows each output row needs. The intermediate values are
/// rounded and clipped to eight bits between the passes, which is not an implementation detail: a
/// decoder that carried more precision through the middle would produce a different, slightly
/// better, and wrong picture.
/// <para/>
/// Reads past the edge of the reference take the nearest sample inside it. Other decoders arrange
/// the same thing by keeping a band of replicated samples around each reference frame and clamping
/// the vector so a block cannot reach past it; clamping the coordinates instead gives identical
/// samples, works for a vector of any size, and needs no band.
/// <para/>
/// Every block predicted here is 4x4, including the sixteen that make up a whole-macroblock vector's
/// luma. Both filters are separable and every output sample depends only on the reference, so
/// predicting a 16x16 block at once and predicting sixteen 4x4 blocks with the same vector give the
/// same samples.
/// </remarks>
internal static class Vp8InterPrediction {

  /// <summary>The six-tap filters, indexed by eighth-pixel displacement (RFC 6386, 18.3).</summary>
  /// <remarks>
  /// The odd rows have zero at both ends and are four-tap filters wearing six-tap clothing. Only
  /// chroma ever reaches them: a luma vector is doubled when it is read, so its displacement is
  /// always even.
  /// </remarks>
  private static readonly short[] _SixTap = [
    0, 0, 128, 0, 0, 0,
    0, -6, 123, 12, -1, 0,
    2, -11, 108, 36, -8, 1,
    0, -9, 93, 50, -6, 0,
    3, -16, 77, 77, -16, 3,
    0, -6, 50, 93, -9, 0,
    1, -8, 36, 108, -11, 2,
    0, -1, 12, 123, -6, 0,
  ];

  /// <summary>The two-tap filters the simpler profiles use, in the same six-tap layout (RFC 6386, 18.3).</summary>
  private static readonly short[] _Bilinear = [
    0, 0, 128, 0, 0, 0,
    0, 0, 112, 16, 0, 0,
    0, 0, 96, 32, 0, 0,
    0, 0, 80, 48, 0, 0,
    0, 0, 64, 64, 0, 0,
    0, 0, 48, 80, 0, 0,
    0, 0, 32, 96, 0, 0,
    0, 0, 16, 112, 0, 0,
  ];

  /// <summary>
  /// Predicts one 4x4 block from a reference plane and writes it into the target plane.
  /// </summary>
  /// <param name="reference">The plane being predicted from.</param>
  /// <param name="target">The plane being written, which is a different frame's.</param>
  /// <param name="stride">The row length, which both planes share.</param>
  /// <param name="planeHeight">How many rows the planes have, for clamping reads to the last one.</param>
  /// <param name="x">The block's column in the target plane.</param>
  /// <param name="y">The block's row in the target plane.</param>
  /// <param name="motionVector">The displacement, in eighths of a sample of this plane.</param>
  /// <param name="bicubic">Whether to use the six-tap filters rather than the two-tap ones.</param>
  internal static void PredictBlock(
    byte[] reference, byte[] target, int stride, int planeHeight,
    int x, int y, Vp8MotionVector motionVector, bool bicubic) {
    var horizontalFraction = motionVector.Column & 7;
    var verticalFraction = motionVector.Row & 7;
    var sourceX = x + (motionVector.Column >> 3);
    var sourceY = y + (motionVector.Row >> 3);

    var destination = y * stride + x;

    if (horizontalFraction == 0 && verticalFraction == 0) {
      for (var row = 0; row < 4; ++row)
        for (var column = 0; column < 4; ++column)
          target[destination + row * stride + column] = _Sample(reference, stride, planeHeight, sourceX + column, sourceY + row);

      return;
    }

    ReadOnlySpan<short> filters = bicubic ? _SixTap : _Bilinear;

    if (verticalFraction == 0) {
      var taps = filters.Slice(horizontalFraction * 6, 6);
      for (var row = 0; row < 4; ++row)
        for (var column = 0; column < 4; ++column)
          target[destination + row * stride + column] =
            _FilterHorizontally(reference, stride, planeHeight, sourceX + column, sourceY + row, taps);

      return;
    }

    var verticalTaps = filters.Slice(verticalFraction * 6, 6);

    if (horizontalFraction == 0) {
      for (var row = 0; row < 4; ++row)
        for (var column = 0; column < 4; ++column) {
          var sum = 64;
          for (var tap = 0; tap < 6; ++tap)
            sum += verticalTaps[tap] * _Sample(reference, stride, planeHeight, sourceX + column, sourceY + row + tap - 2);

          target[destination + row * stride + column] = _Clamp(sum >> 7);
        }

      return;
    }

    // Nine rows of horizontal results, the first two of them above the block, because each of the
    // four output rows is a six-tap combination centred two rows further down.
    Span<byte> intermediate = stackalloc byte[9 * 4];
    var horizontalTaps = filters.Slice(horizontalFraction * 6, 6);
    for (var row = 0; row < 9; ++row)
      for (var column = 0; column < 4; ++column)
        intermediate[row * 4 + column] =
          _FilterHorizontally(reference, stride, planeHeight, sourceX + column, sourceY + row - 2, horizontalTaps);

    for (var row = 0; row < 4; ++row)
      for (var column = 0; column < 4; ++column) {
        var sum = 64;
        for (var tap = 0; tap < 6; ++tap)
          sum += verticalTaps[tap] * intermediate[(row + tap) * 4 + column];

        target[destination + row * stride + column] = _Clamp(sum >> 7);
      }
  }

  private static byte _FilterHorizontally(
    byte[] plane, int stride, int planeHeight, int x, int y, ReadOnlySpan<short> taps) {
    var sum = 64;
    for (var tap = 0; tap < 6; ++tap)
      sum += taps[tap] * _Sample(plane, stride, planeHeight, x + tap - 2, y);

    return _Clamp(sum >> 7);
  }

  /// <summary>One sample of a reference plane, with positions outside it taking the nearest inside.</summary>
  private static byte _Sample(byte[] plane, int stride, int planeHeight, int x, int y) {
    if (x < 0)
      x = 0;
    else if (x >= stride)
      x = stride - 1;

    if (y < 0)
      y = 0;
    else if (y >= planeHeight)
      y = planeHeight - 1;

    return plane[y * stride + x];
  }

  /// <summary>
  /// The chroma vector matching one whole-macroblock luma vector: half of it, rounded away from zero
  /// (RFC 6386, 18.1).
  /// </summary>
  private static int _Halve(int component) => component >= 0 ? (component + 1) / 2 : -((-component + 1) / 2);

  /// <summary>The chroma vector for a whole-macroblock luma vector.</summary>
  internal static Vp8MotionVector ChromaVector(Vp8MotionVector luma, bool wholePixelsOnly) {
    var result = new Vp8MotionVector(_Halve(luma.Row), _Halve(luma.Column));
    return wholePixelsOnly ? result.Truncated() : result;
  }

  /// <summary>
  /// The chroma vector for one quarter of a split macroblock: the average of the four luma vectors
  /// covering the same picture, rounded away from zero (RFC 6386, 18.1).
  /// </summary>
  /// <param name="motionVectors">The macroblock's sixteen subblock vectors.</param>
  /// <param name="first">The top-left of the four subblocks — 0, 2, 8 or 10.</param>
  internal static Vp8MotionVector ChromaVector(
    ReadOnlySpan<Vp8MotionVector> motionVectors, int first, bool wholePixelsOnly) {
    var row = motionVectors[first].Row + motionVectors[first + 1].Row
              + motionVectors[first + 4].Row + motionVectors[first + 5].Row;
    var column = motionVectors[first].Column + motionVectors[first + 1].Column
                 + motionVectors[first + 4].Column + motionVectors[first + 5].Column;

    var result = new Vp8MotionVector(_Average(row), _Average(column));
    return wholePixelsOnly ? result.Truncated() : result;
  }

  /// <summary>A sum of four vector components divided by eight, rounded away from zero.</summary>
  /// <remarks>
  /// By eight and not four, because a chroma sample covers twice the picture a luma sample does. The
  /// rounding is away from zero in both directions so that a rightward displacement and the leftward
  /// one of the same size land the same distance from where they started.
  /// </remarks>
  private static int _Average(int sum) => sum >= 0 ? (sum + 4) / 8 : -((-sum + 4) / 8);

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
