using System;

namespace FileFormat.Codecs.MagicYuv;

/// <summary>The three ways a MagicYUV slice predicts a sample, as its frames number them.</summary>
internal enum MagicYuvPredictor {
  Left = 1,
  Gradient = 2,
  Median = 3,
}

/// <summary>Turns MagicYUV residuals back into samples.</summary>
internal static class MagicYuvPrediction {
  /// <summary>Reconstructs one 8-bit plane slice in-place.</summary>
  internal static void Apply(
    Span<byte> plane,
    int width,
    int firstRow,
    int lastRow,
    MagicYuvPredictor predictor,
    bool interlaced = false
  ) {
    var fieldStride = interlaced ? 2 : 1;
    for (var y = firstRow; y < lastRow; ++y) {
      var row = y * width;
      var hasTop = y - firstRow >= fieldStride;
      for (var x = 0; x < width; ++x) {
        var at = row + x;
        byte predicted;

        if (!hasTop)
          predicted = x == 0 ? (byte)0 : plane[at - 1];
        else if (x == 0)
          predicted = plane[at - fieldStride * width];
        else {
          var left = plane[at - 1];
          var above = plane[at - fieldStride * width];
          var aboveLeft = plane[at - fieldStride * width - 1];
          var gradient = (byte)(left + above - aboveLeft);
          predicted = predictor switch {
            MagicYuvPredictor.Left => left,
            MagicYuvPredictor.Gradient => gradient,
            _ => _Median(left, above, gradient),
          };
        }

        plane[at] = (byte)(predicted + plane[at]);
      }
    }
  }

  /// <summary>Reconstructs a 10/12/14-bit plane slice in-place.</summary>
  /// <remarks>
  /// Deep MagicYUV uses the normal JPEG-LS median edge predictor; unlike the historical 8-bit path,
  /// the gradient entering the median is not first wrapped to the sample depth. FFmpeg and OxideAV
  /// independently agree on this distinction.
  /// </remarks>
  internal static void Apply(
    Span<ushort> plane,
    int width,
    int firstRow,
    int lastRow,
    MagicYuvPredictor predictor,
    int mask,
    bool interlaced = false
  ) {
    var fieldStride = interlaced ? 2 : 1;
    for (var y = firstRow; y < lastRow; ++y) {
      var row = y * width;
      var hasTop = y - firstRow >= fieldStride;
      for (var x = 0; x < width; ++x) {
        var at = row + x;
        int predicted;

        if (!hasTop)
          predicted = x == 0 ? 0 : plane[at - 1];
        else if (x == 0)
          predicted = plane[at - fieldStride * width];
        else {
          var left = (int)plane[at - 1];
          var above = (int)plane[at - fieldStride * width];
          var aboveLeft = (int)plane[at - fieldStride * width - 1];
          predicted = predictor switch {
            MagicYuvPredictor.Left => left,
            MagicYuvPredictor.Gradient => left + above - aboveLeft,
            _ => _MedianDeep(left, above, aboveLeft),
          };
        }

        plane[at] = (ushort)((predicted + plane[at]) & mask);
      }
    }
  }

  private static byte _Median(byte a, byte b, byte c) {
    if (a > b)
      (a, b) = (b, a);

    return c < a ? a : c > b ? b : c;
  }

  private static int _MedianDeep(int left, int above, int aboveLeft) {
    var low = Math.Min(left, above);
    var high = Math.Max(left, above);
    if (aboveLeft >= high)
      return low;
    if (aboveLeft <= low)
      return high;
    return left + above - aboveLeft;
  }
}
