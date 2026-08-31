using System;
using System.Linq;
namespace FileFormat.Codecs.H264.Tests;

[TestFixture]
public sealed class H264Transform8x8Tests {

  [Test]
  public void DcCoefficientProducesAUniformResidual() {
    var levels = new int[64];
    levels[0] = 64;
    var scaling = Enumerable.Repeat((byte)16, 64).ToArray();
    Span<int> residual = stackalloc int[64];

    H264Transform8x8.DecodeBlock(levels, qp: 0, scaling, residual);

    Assert.That(residual.ToArray(), Is.All.EqualTo(5));
  }

  [Test]
  public void ScalingListWeightAffectsTheCoefficientBeforeTheTransform() {
    var levels = new int[64];
    levels[0] = 64;
    var scaling = Enumerable.Repeat((byte)16, 64).ToArray();
    scaling[0] = 32;
    Span<int> residual = stackalloc int[64];

    H264Transform8x8.DecodeBlock(levels, qp: 0, scaling, residual);

    Assert.That(residual.ToArray(), Is.All.EqualTo(10));
  }

  [Test]
  public void NormAdjustmentClassesRepeatEveryFourRowsAndColumns() {
    Assert.That(H264Transform8x8.LevelScale(0, 0, 0, 16), Is.EqualTo(320));
    Assert.That(H264Transform8x8.LevelScale(0, 0, 1, 16), Is.EqualTo(304));
    Assert.That(H264Transform8x8.LevelScale(0, 2, 2, 16), Is.EqualTo(512));
    Assert.That(H264Transform8x8.LevelScale(0, 4, 4, 16), Is.EqualTo(320));
  }

  [Test]
  public void NegativeCoefficientsUseArithmeticShifts() {
    Span<int> input = stackalloc int[64];
    input[1] = -65;
    Span<int> residual = stackalloc int[64];

    H264Transform8x8.InverseTransform(input, residual);

    Assert.That(residual.ToArray(), Has.Some.LessThan(0));
    Assert.That(residual.ToArray(), Has.Some.GreaterThan(0));
  }

  [Test]
  public void AsymmetricCoefficientsUseTheNormativeRowThenColumnOrder() {
    Span<int> input = stackalloc int[64];
    input[9] = 47;
    input[11] = 198;
    input[13] = -38;
    input[39] = -121;
    input[46] = -105;
    Span<int> residual = stackalloc int[64];

    H264Transform8x8.InverseTransform(input, residual);

    int[] expected = [
       5,  4, -10, -1,  2,   8, -1, -7,
       8, -3,  -1, -8,  5,   5, -2, -5,
       4,  0,  -1, -5,  5,   0,  2, -4,
       0,  4,  -6,  3, -1,   2,  0, -2,
      -1, -1,   1,  3, -5,   3, -3,  3,
      -2, -2,   6, -1,  0,  -5,  1,  3,
      -6,  0,   5,  2,  1, -10,  5,  4,
      -7, -1,   5,  6, -8,  -3, -2,  8,
    ];
    Assert.That(residual.ToArray(), Is.EqualTo(expected));
  }
}
