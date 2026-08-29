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
}
