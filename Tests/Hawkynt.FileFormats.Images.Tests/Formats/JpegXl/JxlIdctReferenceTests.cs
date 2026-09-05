using System;
using System.Linq;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// The inverse transforms against a plain textbook one written here, rather
/// than against themselves.
/// </summary>
/// <remarks>
/// The fast transform in the decoder is a butterfly, and a butterfly is easy to
/// get subtly wrong in a way no round-trip against its own forward direction
/// will catch. The reference below is the definition — a sum over cosines, one
/// axis then the other — written out slowly and used for nothing else. Where
/// the two agree the transform is the transform, whatever else may be wrong
/// around it.
///
/// <para>The normalisation is the one the decoder uses throughout: a basis
/// vector has length <c>sqrt(N)</c>, so a block holding nothing but its lowest
/// coefficient comes back flat at that value. That convention is pinned
/// separately by <see cref="JxlFlatBlockInvariantTests"/>.</para>
/// </remarks>
[TestFixture]
internal sealed class JxlIdctReferenceTests {

  /// <summary>One axis of the inverse transform, straight from the definition.</summary>
  private static double[] _Reference1D(double[] coefficients) {
    var n = coefficients.Length;
    var result = new double[n];
    for (var at = 0; at < n; ++at) {
      var sum = 0.0;
      for (var k = 0; k < n; ++k) {
        var amplitude = k == 0 ? 1.0 : Math.Sqrt(2.0);
        sum += coefficients[k] * amplitude * Math.Cos(Math.PI * (at + 0.5) * k / n);
      }

      result[at] = sum;
    }

    return result;
  }

  /// <summary>Both axes: the rows, then the columns of what that leaves.</summary>
  private static double[,] _Reference2D(double[,] coefficients) {
    var height = coefficients.GetLength(0);
    var width = coefficients.GetLength(1);

    var rows = new double[height, width];
    for (var y = 0; y < height; ++y) {
      var row = new double[width];
      for (var x = 0; x < width; ++x)
        row[x] = coefficients[y, x];
      var done = _Reference1D(row);
      for (var x = 0; x < width; ++x)
        rows[y, x] = done[x];
    }

    var result = new double[height, width];
    for (var x = 0; x < width; ++x) {
      var column = new double[height];
      for (var y = 0; y < height; ++y)
        column[y] = rows[y, x];
      var done = _Reference1D(column);
      for (var y = 0; y < height; ++y)
        result[y, x] = done[y];
    }

    return result;
  }

  private static float[] _Random(int count, int seed) {
    var random = new Random(seed);
    var values = new float[count];
    for (var i = 0; i < count; ++i)
      values[i] = (float)Math.Round(random.NextDouble() * 200.0 - 100.0, 3);

    return values;
  }

  /// <param name="strategy">A shape whose whole block is one transform.</param>
  [TestCase(JxlAcStrategyType.Dct8x8)]
  [TestCase(JxlAcStrategyType.Dct16x16)]
  [TestCase(JxlAcStrategyType.Dct32x32)]
  [TestCase(JxlAcStrategyType.Dct16x8)]
  [TestCase(JxlAcStrategyType.Dct8x16)]
  [TestCase(JxlAcStrategyType.Dct32x16)]
  [TestCase(JxlAcStrategyType.Dct16x32)]
  public void AWholeBlockTransformIsTheTransform(JxlAcStrategyType strategy) {
    var (width, height) = JxlVarDctIdct.BlockSize(strategy);
    var coefficients = _Random(width * height, seed: (int)strategy * 7919 + 11);

    var pixels = new float[width * height];
    JxlVarDctIdct.InverseAcStrategy(strategy, coefficients, pixels);

    var reference = new double[height, width];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      reference[y, x] = coefficients[y * width + x];
    reference = _Reference2D(reference);

    var worst = 0.0;
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      worst = Math.Max(worst, Math.Abs(reference[y, x] - pixels[y * width + x]));

    Assert.That(worst, Is.LessThan(0.01), $"{strategy} differs from the definition by {worst}");
  }

  /// <summary>
  /// The two shapes that split the block into halves. Each half takes alternate
  /// rows of the block, its level comes from the sum and difference of the two
  /// coefficients the format keeps it in, and the shorter side of the half is
  /// laid out first — so the half's own coefficients are the transpose of what
  /// was gathered.
  /// </summary>
  [TestCase(JxlAcStrategyType.Dct4x8)]
  [TestCase(JxlAcStrategyType.Dct8x4)]
  public void ASplitBlockIsTwoOfThatTransformSideBySide(JxlAcStrategyType strategy) {
    var stored = _Random(64, seed: (int)strategy * 104729 + 3);

    var pixels = new float[64];
    JxlVarDctIdct.InverseAcStrategy(strategy, stored, pixels);

    // A block is kept the way the scan order fills it, which is the transpose
    // of the way the format writes it down. A shape that picks rows out of the
    // block by number has to be handed it the written way round.
    var coefficients = new float[64];
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x)
      coefficients[y * 8 + x] = stored[x * 8 + y];

    var levels = new[] { coefficients[0] + coefficients[8], coefficients[0] - coefficients[8] };
    var reference = new double[8, 8];
    for (var half = 0; half < 2; ++half) {
      var gathered = new double[4, 8];
      gathered[0, 0] = levels[half];
      for (var iy = 0; iy < 4; ++iy)
      for (var ix = 0; ix < 8; ++ix) {
        if (ix == 0 && iy == 0)
          continue;

        gathered[iy, ix] = coefficients[(half + iy * 2) * 8 + ix];
      }

      if (strategy == JxlAcStrategyType.Dct4x8) {
        // Four rows of eight, stacked one above the other.
        var done = _Reference2D(gathered);
        for (var y = 0; y < 4; ++y)
        for (var x = 0; x < 8; ++x)
          reference[half * 4 + y, x] = done[y, x];
      } else {
        // Eight rows of four, side by side; the gather is their transpose.
        var turned = new double[8, 4];
        for (var y = 0; y < 8; ++y)
        for (var x = 0; x < 4; ++x)
          turned[y, x] = gathered[x, y];
        var done = _Reference2D(turned);
        for (var y = 0; y < 8; ++y)
        for (var x = 0; x < 4; ++x)
          reference[y, half * 4 + x] = done[y, x];
      }
    }

    var worst = 0.0;
    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x)
      worst = Math.Max(worst, Math.Abs(reference[y, x] - pixels[y * 8 + x]));

    Assert.That(worst, Is.LessThan(0.01), $"{strategy} differs from the definition by {worst}");
  }
}
