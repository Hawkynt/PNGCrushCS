using System;

namespace FileFormat.Codecs.H264;

/// <summary>
/// Fetches a block from a reference picture at quarter-sample resolution — ITU-T H.264, clause
/// 8.4.2.2.
/// </summary>
/// <remarks>
/// H.264's motion vectors point between samples, and what lies between them is defined rather than
/// approximated. The half-sample positions come from a six-tap filter with weights 1, −5, 20, 20, −5,
/// 1 — a low-pass with some sharpening in it, which is why a moving picture coded at half-sample
/// vectors does not go soft the way one interpolated bilinearly does. The quarter-sample positions
/// are then the average of the two nearest full or half samples, rounded up.
/// <para/>
/// The centre position <c>j</c> is the one to be careful with. It is the six-tap applied in both
/// directions, and the standard computes it from the <em>unrounded</em> intermediates of the first
/// pass, dividing once by 1024 at the end rather than twice by 32 (equations 8-245 to 8-247).
/// Rounding between the passes gives a value that is off by a fraction of a level on most samples,
/// which is invisible in one frame and accumulates through a chain of predicted ones.
/// <para/>
/// A vector may point outside the reference picture, and that is ordinary rather than an error: the
/// standard clamps the sample coordinates to the picture (equations 8-239 and 8-240), so the edge row
/// and column extend outwards forever. A decoder that refused such a vector would refuse most
/// streams with movement at a picture edge.
/// </remarks>
internal static class H264MotionCompensation {

  /// <summary>
  /// Predicts a luma block of <paramref name="width"/> by <paramref name="height"/> samples.
  /// </summary>
  /// <param name="reference">The reference picture's luma plane.</param>
  /// <param name="planeWidth">Its width, which is the coded width and not the displayed one.</param>
  /// <param name="planeHeight">Its height.</param>
  /// <param name="x">The block's left edge in the current picture, in full samples.</param>
  /// <param name="y">Its top edge.</param>
  /// <param name="mvX">The horizontal motion vector component, in quarter samples.</param>
  /// <param name="mvY">The vertical component.</param>
  /// <param name="pred">Receives the predicted samples in raster order, <paramref name="width"/> to a row.</param>
  internal static void PredictLuma(
    byte[] reference, int planeWidth, int planeHeight,
    int x, int y, int mvX, int mvY, int width, int height, Span<byte> pred) {
    var xInt = x + (mvX >> 2);
    var yInt = y + (mvY >> 2);
    var xFrac = mvX & 3;
    var yFrac = mvY & 3;

    // The whole-sample case is most of the samples of most pictures and needs none of the filtering.
    if (xFrac == 0 && yFrac == 0) {
      for (var row = 0; row < height; ++row)
        for (var column = 0; column < width; ++column)
          pred[row * width + column] = reference[
            _Clip(yInt + row, planeHeight) * planeWidth + _Clip(xInt + column, planeWidth)];

      return;
    }

    for (var row = 0; row < height; ++row)
      for (var column = 0; column < width; ++column)
        pred[row * width + column] = _Sample(
          reference, planeWidth, planeHeight, xInt + column, yInt + row, xFrac, yFrac);
  }

  /// <summary>
  /// Predicts a chroma block, which is bilinear at eighth-sample resolution — clause 8.4.2.2.2.
  /// </summary>
  /// <remarks>
  /// Eighth-sample because a chroma plane of 4:2:0 is half the size in each direction, so a luma
  /// vector of one quarter sample is one eighth of a chroma sample. There is no six-tap here: chroma
  /// carries little enough high frequency that the standard does not spend a filter on it.
  /// </remarks>
  internal static void PredictChroma(
    byte[] reference, int planeWidth, int planeHeight,
    int x, int y, int mvX, int mvY, int width, int height, Span<byte> pred) {
    var xInt = x + (mvX >> 3);
    var yInt = y + (mvY >> 3);
    var xFrac = mvX & 7;
    var yFrac = mvY & 7;

    for (var row = 0; row < height; ++row) {
      var y0 = _Clip(yInt + row, planeHeight) * planeWidth;
      var y1 = _Clip(yInt + row + 1, planeHeight) * planeWidth;

      for (var column = 0; column < width; ++column) {
        var x0 = _Clip(xInt + column, planeWidth);
        var x1 = _Clip(xInt + column + 1, planeWidth);

        var a = reference[y0 + x0];
        var b = reference[y0 + x1];
        var c = reference[y1 + x0];
        var d = reference[y1 + x1];

        pred[row * width + column] = (byte)(
          ((8 - xFrac) * (8 - yFrac) * a
           + xFrac * (8 - yFrac) * b
           + (8 - xFrac) * yFrac * c
           + xFrac * yFrac * d
           + 32) >> 6);
      }
    }
  }

  /// <summary>One luma sample at a fractional position — Table 8-12 and equations 8-241 to 8-261.</summary>
  private static byte _Sample(byte[] reference, int width, int height, int x, int y, int xFrac, int yFrac) {
    // The three half-sample values this position may be built from, computed only where needed.
    // b is the horizontal half-sample, h the vertical one, j the one at the centre of the four.
    switch (xFrac, yFrac) {
      case (0, 2):
        return _Round32(_Vertical(reference, width, height, x, y));

      case (2, 0):
        return _Round32(_Horizontal(reference, width, height, x, y));

      case (2, 2):
        return _Centre(reference, width, height, x, y);
    }

    var g = reference[_Clip(y, height) * width + _Clip(x, width)];

    switch (xFrac, yFrac) {
      case (1, 0): // a
        return _Average(g, _Round32(_Horizontal(reference, width, height, x, y)));

      case (3, 0): // c
        return _Average(
          reference[_Clip(y, height) * width + _Clip(x + 1, width)],
          _Round32(_Horizontal(reference, width, height, x, y)));

      case (0, 1): // d
        return _Average(g, _Round32(_Vertical(reference, width, height, x, y)));

      case (0, 3): // n
        return _Average(
          reference[_Clip(y + 1, height) * width + _Clip(x, width)],
          _Round32(_Vertical(reference, width, height, x, y)));

      case (2, 1): // f
        return _Average(
          _Round32(_Horizontal(reference, width, height, x, y)),
          _Centre(reference, width, height, x, y));

      case (2, 3): // q
        return _Average(
          _Centre(reference, width, height, x, y),
          _Round32(_Horizontal(reference, width, height, x, y + 1)));

      case (1, 2): // i
        return _Average(
          _Round32(_Vertical(reference, width, height, x, y)),
          _Centre(reference, width, height, x, y));

      case (3, 2): // k
        return _Average(
          _Centre(reference, width, height, x, y),
          _Round32(_Vertical(reference, width, height, x + 1, y)));

      case (1, 1): // e
        return _Average(
          _Round32(_Horizontal(reference, width, height, x, y)),
          _Round32(_Vertical(reference, width, height, x, y)));

      case (3, 1): // g
        return _Average(
          _Round32(_Horizontal(reference, width, height, x, y)),
          _Round32(_Vertical(reference, width, height, x + 1, y)));

      case (1, 3): // p
        return _Average(
          _Round32(_Vertical(reference, width, height, x, y)),
          _Round32(_Horizontal(reference, width, height, x, y + 1)));

      default: // (3, 3), r
        return _Average(
          _Round32(_Vertical(reference, width, height, x + 1, y)),
          _Round32(_Horizontal(reference, width, height, x, y + 1)));
    }
  }

  /// <summary>The horizontal six-tap before its rounding shift — <c>b1</c>, equation 8-241.</summary>
  private static int _Horizontal(byte[] reference, int width, int height, int x, int y) {
    var row = _Clip(y, height) * width;
    return reference[row + _Clip(x - 2, width)]
           - 5 * reference[row + _Clip(x - 1, width)]
           + 20 * reference[row + _Clip(x, width)]
           + 20 * reference[row + _Clip(x + 1, width)]
           - 5 * reference[row + _Clip(x + 2, width)]
           + reference[row + _Clip(x + 3, width)];
  }

  /// <summary>The vertical six-tap before its rounding shift — <c>h1</c>, equation 8-242.</summary>
  private static int _Vertical(byte[] reference, int width, int height, int x, int y) {
    var column = _Clip(x, width);
    return reference[_Clip(y - 2, height) * width + column]
           - 5 * reference[_Clip(y - 1, height) * width + column]
           + 20 * reference[_Clip(y, height) * width + column]
           + 20 * reference[_Clip(y + 1, height) * width + column]
           - 5 * reference[_Clip(y + 2, height) * width + column]
           + reference[_Clip(y + 3, height) * width + column];
  }

  /// <summary>
  /// The centre half-sample <c>j</c>: the six-tap applied to the unrounded horizontal intermediates
  /// (equations 8-246 and 8-247).
  /// </summary>
  private static byte _Centre(byte[] reference, int width, int height, int x, int y) {
    var value = _Horizontal(reference, width, height, x, y - 2)
                - 5 * _Horizontal(reference, width, height, x, y - 1)
                + 20 * _Horizontal(reference, width, height, x, y)
                + 20 * _Horizontal(reference, width, height, x, y + 1)
                - 5 * _Horizontal(reference, width, height, x, y + 2)
                + _Horizontal(reference, width, height, x, y + 3);

    return _Clamp((value + 512) >> 10);
  }

  private static byte _Round32(int value) => _Clamp((value + 16) >> 5);

  /// <summary>The quarter-sample average, rounded upwards — equations 8-250 to 8-261.</summary>
  private static byte _Average(int first, int second) => (byte)((first + second + 1) >> 1);

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);

  /// <summary>Clamps a sample coordinate into the picture, which extends its edges outwards.</summary>
  private static int _Clip(int value, int limit) => value < 0 ? 0 : value >= limit ? limit - 1 : value;
}
