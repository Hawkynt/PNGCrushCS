using System;

namespace FileFormat.Codecs.Mpeg4;

/// <summary>
/// Quarter-sample luminance motion compensation for MPEG-4 Part 2 rectangular VOPs.
/// </summary>
/// <remarks>
/// ISO/IEC 14496-2 7.6.2.2 first reads the 9x9 integer-sample reference needed for an 8x8 block,
/// mirrors that block by three samples at each boundary, applies the eight-tap half-sample filter,
/// and only then forms the quarter positions by bilinear interpolation. The 2004 corrigendum makes
/// the order explicit: horizontal mirror/filter/blend first, then the same vertical process.
/// <para/>
/// This deliberately works on one coded 8x8 luminance block at a time. Looking through the block
/// boundary into neighbouring picture samples is not equivalent to the standard's boundary mirror;
/// four 8x8 predictions can therefore differ from one 16x16 prediction even when their motion
/// vectors are equal.
/// </remarks>
internal static class Mpeg4QuarterSample {

  private const int _BLOCK = 8;
  private const int _REFERENCE = _BLOCK + 1;

  /// <summary>
  /// Predicts one 8x8 luminance block from a quarter-sample motion vector.
  /// </summary>
  /// <param name="prediction">At least 64 integers, filled in raster order.</param>
  /// <param name="plane">The reconstructed reference luminance plane.</param>
  /// <param name="stride">Reference-plane row stride.</param>
  /// <param name="origin">Index of reference sample (0,0).</param>
  /// <param name="width">Decoded reference width in samples, including coded macroblock padding.</param>
  /// <param name="height">Decoded reference height in samples, including coded macroblock padding.</param>
  /// <param name="blockX">Left edge of the destination block.</param>
  /// <param name="blockY">Top edge of the destination block.</param>
  /// <param name="vectorX">Horizontal motion-vector component in quarter-sample units.</param>
  /// <param name="vectorY">Vertical motion-vector component in quarter-sample units.</param>
  /// <param name="rounding">The VOP rounding-control bit.</param>
  internal static void Predict(
    Span<int> prediction, byte[] plane, int stride, int origin, int width, int height,
    int blockX, int blockY, int vectorX, int vectorY, int rounding) {
    if (prediction.Length < _BLOCK * _BLOCK)
      throw new ArgumentException("An MPEG-4 quarter-sample prediction needs 64 output samples.", nameof(prediction));

    var wholeX = vectorX >> 2;
    var wholeY = vectorY >> 2;
    var fractionX = vectorX & 3;
    var fractionY = vectorY & 3;
    rounding &= 1;

    Span<byte> reference = stackalloc byte[_REFERENCE * _REFERENCE];
    var sourceX = blockX + wholeX;
    var sourceY = blockY + wholeY;
    for (var y = 0; y < _REFERENCE; ++y)
      for (var x = 0; x < _REFERENCE; ++x)
        reference[y * _REFERENCE + x] = _At(
          plane, stride, origin, width, height, sourceX + x, sourceY + y);

    // Horizontal processing yields eight columns but keeps all nine rows for the vertical stage.
    Span<byte> horizontal = stackalloc byte[_BLOCK * _REFERENCE];
    for (var y = 0; y < _REFERENCE; ++y) {
      var sourceRow = reference.Slice(y * _REFERENCE, _REFERENCE);
      var targetRow = horizontal.Slice(y * _BLOCK, _BLOCK);
      if (fractionX == 0) {
        sourceRow[.._BLOCK].CopyTo(targetRow);
        continue;
      }

      for (var x = 0; x < _BLOCK; ++x) {
        var half = _Half(sourceRow, x, rounding);
        targetRow[x] = fractionX switch {
          1 => _Average(sourceRow[x], half, rounding),
          2 => half,
          _ => _Average(half, sourceRow[x + 1], rounding),
        };
      }
    }

    if (fractionY == 0) {
      for (var y = 0; y < _BLOCK; ++y)
        for (var x = 0; x < _BLOCK; ++x)
          prediction[y * _BLOCK + x] = horizontal[y * _BLOCK + x];
      return;
    }

    Span<byte> column = stackalloc byte[_REFERENCE];
    for (var x = 0; x < _BLOCK; ++x) {
      for (var y = 0; y < _REFERENCE; ++y)
        column[y] = horizontal[y * _BLOCK + x];

      for (var y = 0; y < _BLOCK; ++y) {
        var half = _Half(column, y, rounding);
        prediction[y * _BLOCK + x] = fractionY switch {
          1 => _Average(column[y], half, rounding),
          2 => half,
          _ => _Average(half, column[y + 1], rounding),
        };
      }
    }
  }

  /// <summary>
  /// ISO/IEC 14496-2 Table 7-13 conversion used by B-VOP direct mode: a co-located quarter-sample
  /// vector is reduced to the half-sample grid before temporal scaling.
  /// </summary>
  internal static int ToDirectHalfSample(int vector) {
    var magnitude = Math.Abs(vector);
    var half = 2 * (magnitude >> 2) + ((magnitude & 3) == 0 ? 0 : 1);
    return vector < 0 ? -half : half;
  }

  /// <summary>
  /// Derives one chrominance component from the four luminance components in quarter-sample mode.
  /// Section 7.6.5 divides each luminance vector by two before summation; division is the language's
  /// truncation-toward-zero integer division, after which the ordinary Table 7-10 reduction applies.
  /// </summary>
  internal static int ToChroma(
    int first, int second, int third, int fourth)
    => Mpeg4MotionCompensation.ToChroma(first / 2 + second / 2 + third / 2 + fourth / 2);

  /// <summary>One filtered half-sample between <paramref name="samples"/>[index] and index+1.</summary>
  private static byte _Half(scoped ReadOnlySpan<byte> samples, int index, int rounding) {
    // Figure 7-30 mirrors the edge sample itself first: for index zero the eight taps are
    // 2,1,0,0,1,2,3,4. At the other edge they are 4,5,6,7,8,8,7,6.
    var sum =
      -8 * (_Mirrored(samples, index - 3) + _Mirrored(samples, index + 4))
      + 24 * (_Mirrored(samples, index - 2) + _Mirrored(samples, index + 3))
      - 48 * (_Mirrored(samples, index - 1) + _Mirrored(samples, index + 2))
      + 160 * (_Mirrored(samples, index) + _Mirrored(samples, index + 1));

    // ISO's integer division truncates toward zero, as C# integer division does. An arithmetic
    // right shift would round a negative pre-clipped filter result toward minus infinity instead.
    return (byte)Math.Clamp((sum + 128 - rounding) / 256, 0, 255);
  }

  private static byte _Average(int left, int right, int rounding)
    => (byte)((left + right + 1 - rounding) / 2);

  private static byte _Mirrored(scoped ReadOnlySpan<byte> samples, int index) {
    if (index < 0)
      index = -index - 1;
    else if (index >= samples.Length)
      index = 2 * samples.Length - index - 1;

    return samples[index];
  }

  private static byte _At(
    byte[] plane, int stride, int origin, int width, int height, int x, int y) {
    x = Math.Clamp(x, 0, width - 1);
    y = Math.Clamp(y, 0, height - 1);
    return plane[origin + y * stride + x];
  }
}
