using System;
using FileFormat.Codecs.Mpeg4;

namespace FileFormat.Codecs.Wmv2;

/// <summary>
/// WMV2's "mspel" motion compensation: a luminance prediction to a quarter of a sample horizontally,
/// interpolated with a four-tap filter where WMV1 averages two samples.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>ff_mspel_motion</c> and the <c>put_mspel8_mc*</c> functions of
/// <c>libavcodec/wmv2dec.c</c> (LGPL-2.1-or-later). The half-sample positions are filtered with
/// (-1, 9, 9, -1) / 16 and clipped to a byte; the extra quarter step a macroblock may state
/// horizontally is the average of that and the sample beside it. The clips of the source position to
/// sixteen samples outside the picture — and the dropping of the fraction there — are FFmpeg's, and so
/// are the ones it makes against the picture's stated width rather than its macroblock-aligned one.
/// Chrominance is predicted to a half sample as in WMV1.
/// </remarks>
internal static class Wmv2MotionCompensation {

  private const int _WINDOW = 19;

  /// <summary>Predicts the six blocks of one macroblock, sixty-four samples each, into <paramref name="blocks"/>.</summary>
  /// <param name="reference">The picture predicted from.</param>
  /// <param name="column">The macroblock's column.</param>
  /// <param name="row">The macroblock's row.</param>
  /// <param name="vectorX">The vector, in half samples.</param>
  /// <param name="vectorY">The vector, in half samples.</param>
  /// <param name="quarterShift">The extra horizontal quarter step the macroblock stated.</param>
  /// <param name="width">The picture's stated width.</param>
  /// <param name="height">The picture's stated height.</param>
  /// <param name="rounding">One where the half-sample averages of the chrominance round down.</param>
  internal static void Predict(
    Mpeg4Frame reference, int column, int row, int vectorX, int vectorY, int quarterShift,
    int width, int height, int rounding, Span<int> blocks) {
    var dxy = 2 * (((vectorY & 1) << 1) | (vectorX & 1)) + quarterShift;
    var sourceX = Math.Clamp(column * 16 + (vectorX >> 1), -16, width);
    var sourceY = Math.Clamp(row * 16 + (vectorY >> 1), -16, height);
    if (sourceX <= -16 || sourceX >= width)
      dxy &= ~3;
    if (sourceY <= -16 || sourceY >= height)
      dxy &= ~4;

    Span<byte> window = stackalloc byte[_WINDOW * _WINDOW];
    var plane = reference.Luma;
    for (var y = 0; y < _WINDOW; ++y) {
      var sy = Math.Clamp(sourceY - 1 + y, 0, reference.LumaHeight - 1);
      var line = reference.LumaOrigin + sy * reference.LumaStride;
      for (var x = 0; x < _WINDOW; ++x)
        window[y * _WINDOW + x] = plane[line + Math.Clamp(sourceX - 1 + x, 0, reference.LumaWidth - 1)];
    }

    for (var block = 0; block < 4; ++block)
      _PredictLuma(window, 1 + (block >> 1) * 8, 1 + (block & 1) * 8, dxy, blocks.Slice(block * 64, 64));

    var chromaDxy = ((vectorX & 3) != 0 ? 1 : 0) | ((vectorY & 3) != 0 ? 2 : 0);
    var chromaX = Math.Clamp(column * 8 + (vectorX >> 2), -8, width >> 1);
    if (chromaX == width >> 1)
      chromaDxy &= ~1;
    var chromaY = Math.Clamp(row * 8 + (vectorY >> 2), -8, height >> 1);
    if (chromaY == height >> 1)
      chromaDxy &= ~2;

    var halfX = 2 * (chromaX - column * 8) + (chromaDxy & 1);
    var halfY = 2 * (chromaY - row * 8) + (chromaDxy >> 1);
    for (var index = 4; index < 6; ++index) {
      var (chroma, stride, origin, planeWidth, planeHeight) = reference.PlaneOf(index);
      Mpeg4MotionCompensation.PredictHalfSample(
        blocks.Slice(index * 64, 64), chroma, stride, origin, Mpeg4Frame.Border / 2, planeWidth, planeHeight,
        column * 8, row * 8, halfX, halfY, rounding);
    }
  }

  /// <summary>One 8x8 luminance prediction from the window, whose sample (x, y) of the block is at <paramref name="top"/> + y, <paramref name="left"/> + x.</summary>
  private static void _PredictLuma(ReadOnlySpan<byte> window, int top, int left, int dxy, Span<int> output) {
    Span<byte> halfH = stackalloc byte[88];
    Span<byte> halfV = stackalloc byte[64];
    Span<byte> halfHV = stackalloc byte[64];
    var origin = top * _WINDOW + left;

    switch (dxy) {
      case 0:
        for (var y = 0; y < 8; ++y)
          for (var x = 0; x < 8; ++x)
            output[y * 8 + x] = window[origin + y * _WINDOW + x];
        return;

      case 1:
      case 3:
        _Horizontal(window, origin, _WINDOW, halfV, 8, 8);
        for (var y = 0; y < 8; ++y)
          for (var x = 0; x < 8; ++x)
            output[y * 8 + x] = (window[origin + y * _WINDOW + x + (dxy >> 1)] + halfV[y * 8 + x] + 1) >> 1;
        return;

      case 2:
        _Horizontal(window, origin, _WINDOW, halfV, 8, 8);
        _Widen(halfV, output);
        return;

      case 4:
        _Vertical(window, origin, _WINDOW, halfV, 8);
        _Widen(halfV, output);
        return;

      case 5:
      case 7:
        _Horizontal(window, origin - _WINDOW, _WINDOW, halfH, 8, 11);
        _Vertical(window, origin + (dxy == 7 ? 1 : 0), _WINDOW, halfV, 8);
        _Vertical(halfH, 8, 8, halfHV, 8);
        for (var i = 0; i < 64; ++i)
          output[i] = (halfV[i] + halfHV[i] + 1) >> 1;
        return;

      default:
        _Horizontal(window, origin - _WINDOW, _WINDOW, halfH, 8, 11);
        _Vertical(halfH, 8, 8, halfHV, 8);
        _Widen(halfHV, output);
        return;
    }
  }

  /// <summary><c>wmv2_mspel8_h_lowpass</c>: <paramref name="rows"/> rows of eight half samples.</summary>
  private static void _Horizontal(ReadOnlySpan<byte> source, int origin, int stride, Span<byte> target, int targetStride, int rows) {
    for (var y = 0; y < rows; ++y) {
      var s = origin + y * stride;
      for (var x = 0; x < 8; ++x)
        target[y * targetStride + x] = _Clip((9 * (source[s + x] + source[s + x + 1]) - (source[s + x - 1] + source[s + x + 2]) + 8) >> 4);
    }
  }

  /// <summary><c>wmv2_mspel8_v_lowpass</c>: eight rows of eight half samples.</summary>
  private static void _Vertical(ReadOnlySpan<byte> source, int origin, int stride, Span<byte> target, int targetStride) {
    for (var x = 0; x < 8; ++x)
      for (var y = 0; y < 8; ++y) {
        var s = origin + y * stride + x;
        target[y * targetStride + x] = _Clip(
          (9 * (source[s] + source[s + stride]) - (source[s - stride] + source[s + 2 * stride]) + 8) >> 4);
      }
  }

  private static void _Widen(ReadOnlySpan<byte> samples, Span<int> output) {
    for (var i = 0; i < 64; ++i)
      output[i] = samples[i];
  }

  private static byte _Clip(int value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;
}
