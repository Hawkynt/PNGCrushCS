using System;

namespace FileFormat.Codecs.H263;

/// <summary>Forms an H.263 motion-compensated 8x8 prediction at half-pixel resolution.</summary>
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

  /// <summary>Derives a chrominance-vector component from a luminance vector (Table 18/H.263).</summary>
  internal static int ToChroma(int vector) {
    var magnitude = vector < 0 ? -vector : vector;
    var rounded = 2 * (magnitude >> 2) + ((magnitude & 3) != 0 ? 1 : 0);
    return vector < 0 ? -rounded : rounded;
  }
}
