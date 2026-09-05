using System;
using System.Linq;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// A flat block comes out flat, and at the same level, whichever shape the
/// encoder picked for it.
/// </summary>
/// <remarks>
/// An encoder chooses a transform shape per block, and two neighbouring blocks
/// of the same flat colour may well get different ones. Nothing in the picture
/// says which was chosen, so if the shapes disagreed about what a lone lowest
/// coefficient means, a flat region would come back in stripes along the seams
/// between them — and a decoder measured only against detailed pictures would
/// never be told. This is what holds the whole family to one convention.
/// </remarks>
[TestFixture]
internal sealed class JxlFlatBlockInvariantTests {

  private static readonly JxlAcStrategyType[] _EveryShape = [
    JxlAcStrategyType.Dct8x8, JxlAcStrategyType.Hornuss, JxlAcStrategyType.Dct2x2,
    JxlAcStrategyType.Dct4x4, JxlAcStrategyType.Dct16x16, JxlAcStrategyType.Dct32x32,
    JxlAcStrategyType.Dct16x8, JxlAcStrategyType.Dct8x16, JxlAcStrategyType.Dct32x8,
    JxlAcStrategyType.Dct8x32, JxlAcStrategyType.Dct32x16, JxlAcStrategyType.Dct16x32,
    JxlAcStrategyType.Dct4x8, JxlAcStrategyType.Dct8x4, JxlAcStrategyType.Afv0,
    JxlAcStrategyType.Afv1, JxlAcStrategyType.Afv2, JxlAcStrategyType.Afv3,
    JxlAcStrategyType.Dct64x64, JxlAcStrategyType.Dct64x32, JxlAcStrategyType.Dct32x64,
  ];

  /// <param name="strategy">The shape the encoder picked.</param>
  [TestCaseSource(nameof(_EveryShape))]
  public void OneLowestCoefficientFillsTheBlockWithThatValue(JxlAcStrategyType strategy) {
    const float level = 100.0f;
    var (width, height) = JxlVarDctIdct.BlockSize(strategy);
    var coefficients = new float[width * height];
    coefficients[0] = level;
    var pixels = new float[width * height];

    JxlVarDctIdct.InverseAcStrategy(strategy, coefficients, pixels);

    Assert.Multiple(() => {
      Assert.That(pixels.Min(), Is.EqualTo(level).Within(1e-3f), "the darkest sample");
      Assert.That(pixels.Max(), Is.EqualTo(level).Within(1e-3f), "the brightest");
    });
  }

  /// <summary>
  /// And a lone coefficient anywhere else carries its whole weight into the
  /// block rather than losing part of it. A shape that spends its coefficients
  /// over a smaller area keeps the same total, which is what lets one
  /// quantisation step mean the same thing for all of them.
  /// </summary>
  [TestCaseSource(nameof(_EveryShape))]
  public void ALoneCoefficientKeepsItsWeight(JxlAcStrategyType strategy) {
    const float level = 100.0f;
    var (width, height) = JxlVarDctIdct.BlockSize(strategy);

    // Position 8 is the second row of the block, which every shape in the
    // family treats as a lowest coefficient of its own or a plain frequency.
    var coefficients = new float[width * height];
    coefficients[8] = level;
    var pixels = new float[width * height];

    JxlVarDctIdct.InverseAcStrategy(strategy, coefficients, pixels);

    var rms = Math.Sqrt(pixels.Sum(v => (double)v * v) / pixels.Length);
    Assert.That(rms, Is.EqualTo(level).Within(1e-2), "the block holds what was put into it");
  }
}
