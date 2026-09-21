using System;

namespace FileFormat.Codecs.H263;

/// <summary>
/// Forms H.263 motion-compensated predictions at half-pixel resolution.
/// </summary>
internal static class H263MotionCompensation {

  /// <summary>
  /// Predicts one 8x8 block from a reference plane.
  /// </summary>
  /// <param name="roundingControl">
  /// RCONTROL from H.263 6.1.2. Baseline pictures use zero; PLUSPTYPE P-pictures may use RTYPE=1.
  /// </param>
  internal static bool TryPredict(
    Span<int> prediction,
    byte[] plane,
    int planeWidth,
    int blockX,
    int blockY,
    int vectorX,
    int vectorY,
    bool clampToEdge,
    int roundingControl = 0) {
    if (roundingControl is < 0 or > 1)
      throw new ArgumentOutOfRangeException(nameof(roundingControl));

    var planeHeight = plane.Length / planeWidth;

    // The whole-pixel part is an arithmetic shift and not a division: a vector of -3 half-pixels is
    // one whole pixel to the left plus a half-pixel to the right.
    var wholeX = vectorX >> 1;
    var wholeY = vectorY >> 1;
    var halfX = vectorX - 2 * wholeX;
    var halfY = vectorY - 2 * wholeY;
    var sourceX = blockX + wholeX;
    var sourceY = blockY + wholeY;

    var reachesOutside = sourceX < 0 || sourceY < 0
                         || sourceX + 8 + halfX > planeWidth
                         || sourceY + 8 + halfY > planeHeight;

    if (reachesOutside) {
      if (!clampToEdge)
        return false;

      _PredictFromEdge(
        prediction, plane, planeWidth, planeHeight, sourceX, sourceY, halfX, halfY, roundingControl);
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
          prediction[y * 8 + x] = (plane[row + x] + plane[row + x + 1] + 1 - roundingControl) >> 1;
      }
      return true;
    }

    if (halfX == 0) {
      for (var y = 0; y < 8; ++y) {
        var row = (sourceY + y) * planeWidth + sourceX;
        var below = row + planeWidth;
        for (var x = 0; x < 8; ++x)
          prediction[y * 8 + x] = (plane[row + x] + plane[below + x] + 1 - roundingControl) >> 1;
      }
      return true;
    }

    for (var y = 0; y < 8; ++y) {
      var row = (sourceY + y) * planeWidth + sourceX;
      var below = row + planeWidth;
      for (var x = 0; x < 8; ++x)
        prediction[y * 8 + x] =
          (plane[row + x] + plane[row + x + 1] + plane[below + x] + plane[below + x + 1]
           + 2 - roundingControl) >> 2;
    }

    return true;
  }

  /// <summary>
  /// Forms Annex F's overlapped prediction for one 8x8 luminance block.
  /// </summary>
  /// <remarks>
  /// Annex F weights the prediction made by the block's own vector together with one vertical and one
  /// horizontal neighbour. The active vertical neighbour is the top one in the top half and the
  /// bottom one in the bottom half; likewise the active horizontal neighbour changes from left to
  /// right at the block centre. The three integer weights sum to eight for every sample, and the
  /// normative rounding term is four.
  /// </remarks>
  internal static bool TryPredictOverlapped(
    Span<int> prediction, byte[] plane, int planeWidth, int blockX, int blockY,
    int currentX, int currentY,
    int topX, int topY, int leftX, int leftY, int rightX, int rightY, int bottomX, int bottomY,
    bool clampToEdge) {
    Span<int> current = stackalloc int[64];
    Span<int> vertical = stackalloc int[64];
    Span<int> horizontal = stackalloc int[64];

    if (!TryPredict(current, plane, planeWidth, blockX, blockY, currentX, currentY, clampToEdge))
      return false;

    for (var y = 0; y < 8; ++y) {
      var useTop = y < 4;
      var verticalX = useTop ? topX : bottomX;
      var verticalY = useTop ? topY : bottomY;
      Span<int> rowPrediction = stackalloc int[64];
      if (!TryPredict(rowPrediction, plane, planeWidth, blockX, blockY, verticalX, verticalY, clampToEdge))
        return false;
      rowPrediction.CopyTo(vertical);
      break;
    }

    // Only two complete predictions are needed for each axis. Building both halves explicitly keeps
    // the filter independent of any implementation-specific scratch-buffer layout.
    Span<int> top = stackalloc int[64];
    Span<int> bottom = stackalloc int[64];
    Span<int> left = stackalloc int[64];
    Span<int> right = stackalloc int[64];
    if (!TryPredict(top, plane, planeWidth, blockX, blockY, topX, topY, clampToEdge)
        || !TryPredict(bottom, plane, planeWidth, blockX, blockY, bottomX, bottomY, clampToEdge)
        || !TryPredict(left, plane, planeWidth, blockX, blockY, leftX, leftY, clampToEdge)
        || !TryPredict(right, plane, planeWidth, blockX, blockY, rightX, rightY, clampToEdge))
      return false;

    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x) {
        var at = y * 8 + x;
        var verticalSample = y < 4 ? top[at] : bottom[at];
        var horizontalSample = x < 4 ? left[at] : right[at];
        prediction[at] = (
          _CurrentWeight[at] * current[at]
          + _VerticalWeight[at] * verticalSample
          + _HorizontalWeight[at] * horizontalSample
          + 4) >> 3;
      }

    return true;
  }

  // Annex F, Figure F.2/F.3 weighting matrices. These values are normative data, not an
  // implementation-derived optimisation.
  private static readonly byte[] _CurrentWeight = [
    4,5,5,5,5,5,5,4,
    5,5,5,5,5,5,5,5,
    5,5,6,6,6,6,5,5,
    5,5,6,6,6,6,5,5,
    5,5,6,6,6,6,5,5,
    5,5,6,6,6,6,5,5,
    5,5,5,5,5,5,5,5,
    4,5,5,5,5,5,5,4,
  ];

  private static readonly byte[] _VerticalWeight = [
    2,2,2,2,2,2,2,2,
    1,1,2,2,2,2,1,1,
    1,1,1,1,1,1,1,1,
    1,1,1,1,1,1,1,1,
    1,1,1,1,1,1,1,1,
    1,1,1,1,1,1,1,1,
    1,1,2,2,2,2,1,1,
    2,2,2,2,2,2,2,2,
  ];

  private static readonly byte[] _HorizontalWeight = [
    2,1,1,1,1,1,1,2,
    2,2,1,1,1,1,2,2,
    2,2,1,1,1,1,2,2,
    2,2,1,1,1,1,2,2,
    2,2,1,1,1,1,2,2,
    2,2,1,1,1,1,2,2,
    2,2,1,1,1,1,2,2,
    2,1,1,1,1,1,1,2,
  ];

  private static void _PredictFromEdge(
    Span<int> prediction,
    byte[] plane,
    int planeWidth,
    int planeHeight,
    int sourceX,
    int sourceY,
    int halfX,
    int halfY,
    int roundingControl) {
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x) {
      var a = _At(plane, planeWidth, planeHeight, sourceX + x, sourceY + y);
      if (halfX == 0 && halfY == 0) {
        prediction[y * 8 + x] = a;
        continue;
      }

      if (halfY == 0) {
        prediction[y * 8 + x] =
          (a + _At(plane, planeWidth, planeHeight, sourceX + x + 1, sourceY + y)
           + 1 - roundingControl) >> 1;
        continue;
      }

      if (halfX == 0) {
        prediction[y * 8 + x] =
          (a + _At(plane, planeWidth, planeHeight, sourceX + x, sourceY + y + 1)
           + 1 - roundingControl) >> 1;
        continue;
      }

      prediction[y * 8 + x] =
        (a
         + _At(plane, planeWidth, planeHeight, sourceX + x + 1, sourceY + y)
         + _At(plane, planeWidth, planeHeight, sourceX + x, sourceY + y + 1)
         + _At(plane, planeWidth, planeHeight, sourceX + x + 1, sourceY + y + 1)
         + 2 - roundingControl) >> 2;
    }
  }

  private static int _At(byte[] plane, int planeWidth, int planeHeight, int x, int y) {
    x = x < 0 ? 0 : x >= planeWidth ? planeWidth - 1 : x;
    y = y < 0 ? 0 : y >= planeHeight ? planeHeight - 1 : y;
    return plane[y * planeWidth + x];
  }

  /// <summary>
  /// Derives one component of the chrominance vector from a single luminance vector.
  /// </summary>
  internal static int ToChroma(int vector) {
    var magnitude = vector < 0 ? -vector : vector;
    var rounded = 2 * (magnitude >> 2) + ((magnitude & 3) != 0 ? 1 : 0);
    return vector < 0 ? -rounded : rounded;
  }

  /// <summary>
  /// Derives an Annex F chrominance vector component from the sum of four luminance components.
  /// </summary>
  /// <remarks>
  /// The four half-pixel luminance vectors sum at sixteenth-pixel chroma resolution. Table F.1 maps
  /// that value onto the nearest half-pixel chroma position with its specified tie behaviour.
  /// </remarks>
  internal static int FourVectorChroma(int sum) {
    ReadOnlySpan<byte> round = [0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 1, 1];
    return (sum >> 3) + round[sum & 0xF];
  }
}
