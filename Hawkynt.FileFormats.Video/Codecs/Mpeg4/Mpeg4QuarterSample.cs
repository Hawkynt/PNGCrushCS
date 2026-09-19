using System;

namespace FileFormat.Codecs.Mpeg4;

/// <summary>
/// Quarter-sample luminance motion compensation for MPEG-4 Part 2 rectangular VOPs.
/// </summary>
/// <remarks>
/// ISO/IEC 14496-2 7.6.2.2 first reads the <c>(M+1)x(N+1)</c> integer-sample reference needed for an
/// <c>MxN</c> prediction block, mirrors that block by three samples at each boundary, applies the
/// eight-tap half-sample filter, and only then forms the quarter positions by bilinear interpolation.
/// The 2004 corrigendum makes the order explicit: horizontal mirror/filter/blend first, then the same
/// vertical process.
/// <para/>
/// The block size is part of the normative operation. A one-vector progressive macroblock predicts
/// one 16x16 luminance block, while INTER4V predicts four independently mirrored 8x8 blocks. Those
/// are intentionally not interchangeable in quarter-sample mode even when all four vectors happen
/// to be equal.
/// </remarks>
internal static class Mpeg4QuarterSample {

  private const int _SUB_BLOCK = 8;
  private const int _MACROBLOCK = 16;
  private const int _MAX_REFERENCE = _MACROBLOCK + 1;

  /// <summary>
  /// Predicts one independently mirrored 8x8 luminance block from a quarter-sample motion vector.
  /// </summary>
  internal static void Predict(
    Span<int> prediction, byte[] plane, int stride, int origin, int width, int height,
    int blockX, int blockY, int vectorX, int vectorY, int rounding)
    => Predict(
      prediction, plane, stride, origin, width, height,
      blockX, blockY, _SUB_BLOCK, vectorX, vectorY, rounding);

  /// <summary>
  /// Predicts one 8x8 or 16x16 luminance block from a quarter-sample motion vector.
  /// </summary>
  /// <param name="prediction">At least <paramref name="blockSize"/> squared integers, filled in raster order.</param>
  /// <param name="plane">The reconstructed reference luminance plane.</param>
  /// <param name="stride">Reference-plane row stride.</param>
  /// <param name="origin">Index of reference sample (0,0).</param>
  /// <param name="width">Decoded reference width in samples, including coded macroblock padding.</param>
  /// <param name="height">Decoded reference height in samples, including coded macroblock padding.</param>
  /// <param name="blockX">Left edge of the destination prediction block.</param>
  /// <param name="blockY">Top edge of the destination prediction block.</param>
  /// <param name="blockSize">Eight for an INTER4V/direct sub-block, sixteen for a single-vector macroblock.</param>
  /// <param name="vectorX">Horizontal motion-vector component in quarter-sample units.</param>
  /// <param name="vectorY">Vertical motion-vector component in quarter-sample units.</param>
  /// <param name="rounding">The VOP rounding-control bit.</param>
  internal static void Predict(
    Span<int> prediction, byte[] plane, int stride, int origin, int width, int height,
    int blockX, int blockY, int blockSize, int vectorX, int vectorY, int rounding) {
    if (blockSize is not (_SUB_BLOCK or _MACROBLOCK))
      throw new ArgumentOutOfRangeException(
        nameof(blockSize), blockSize, "MPEG-4 quarter-sample prediction blocks are 8x8 or 16x16 here.");

    var sampleCount = checked(blockSize * blockSize);
    if (prediction.Length < sampleCount)
      throw new ArgumentException(
        $"An MPEG-4 {blockSize}x{blockSize} quarter-sample prediction needs {sampleCount} output samples.",
        nameof(prediction));

    if (width <= 0 || height <= 0)
      throw new ArgumentOutOfRangeException(nameof(width), "The reference plane must have positive dimensions.");

    var referenceSize = blockSize + 1;
    var wholeX = vectorX >> 2;
    var wholeY = vectorY >> 2;
    var fractionX = vectorX & 3;
    var fractionY = vectorY & 3;
    rounding &= 1;

    Span<byte> referenceStorage = stackalloc byte[_MAX_REFERENCE * _MAX_REFERENCE];
    var reference = referenceStorage[..(referenceSize * referenceSize)];
    var sourceX = blockX + wholeX;
    var sourceY = blockY + wholeY;
    for (var y = 0; y < referenceSize; ++y)
      for (var x = 0; x < referenceSize; ++x)
        reference[y * referenceSize + x] = _At(
          plane, stride, origin, width, height, sourceX + x, sourceY + y);

    // Horizontal processing yields M columns but keeps all N+1 rows for the vertical stage.
    Span<byte> horizontalStorage = stackalloc byte[_MACROBLOCK * _MAX_REFERENCE];
    var horizontal = horizontalStorage[..(blockSize * referenceSize)];
    for (var y = 0; y < referenceSize; ++y) {
      var sourceRow = reference.Slice(y * referenceSize, referenceSize);
      var targetRow = horizontal.Slice(y * blockSize, blockSize);
      if (fractionX == 0) {
        sourceRow[..blockSize].CopyTo(targetRow);
        continue;
      }

      for (var x = 0; x < blockSize; ++x) {
        var half = _Half(sourceRow, x, rounding);
        targetRow[x] = fractionX switch {
          1 => _Average(sourceRow[x], half, rounding),
          2 => half,
          _ => _Average(half, sourceRow[x + 1], rounding),
        };
      }
    }

    if (fractionY == 0) {
      for (var y = 0; y < blockSize; ++y)
        for (var x = 0; x < blockSize; ++x)
          prediction[y * blockSize + x] = horizontal[y * blockSize + x];
      return;
    }

    Span<byte> columnStorage = stackalloc byte[_MAX_REFERENCE];
    var column = columnStorage[..referenceSize];
    for (var x = 0; x < blockSize; ++x) {
      for (var y = 0; y < referenceSize; ++y)
        column[y] = horizontal[y * blockSize + x];

      for (var y = 0; y < blockSize; ++y) {
        var half = _Half(column, y, rounding);
        prediction[y * blockSize + x] = fractionY switch {
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
    // 2,1,0,0,1,2,3,4. At the other edge they are M-4,M-3,M-2,M-1,M,M,M-1,M-2.
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
