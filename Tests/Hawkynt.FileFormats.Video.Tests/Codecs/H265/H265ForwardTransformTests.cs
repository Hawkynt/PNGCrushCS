using System;
using System.Linq;

namespace FileFormat.Codecs.H265.Tests;

/// <summary>
/// Holds the forward transform and quantiser against the inverse pair the decoder runs.
/// </summary>
/// <remarks>
/// Only the inverse is normative, so there is nothing to check the forward direction against except
/// the round trip it is supposed to make: transform, quantise, dequantise, inverse-transform, and
/// compare with what went in. At a quantiser of zero that has to be close; the bound loosens as the
/// quantiser coarsens, which is what quantising is for. What must not happen at any quantiser is the
/// residual coming back systematically small — which is what a wrong shift produces, and what a
/// tolerance stated as a fraction of the input would hide.
/// </remarks>
[TestFixture]
public sealed class H265ForwardTransformTests {

  [TestCase(2)]
  [TestCase(3)]
  [TestCase(4)]
  [TestCase(5)]
  [Category("RoundTrip")]
  [Category("HappyPath")]
  public void ALosslessQuantiserReturnsTheResidualItWasGiven(int log2Size) {
    var size = 1 << log2Size;
    var random = new Random(31 + log2Size);
    var residual = new int[size * size];
    for (var i = 0; i < residual.Length; ++i)
      residual[i] = random.Next(-64, 65);

    var reconstructed = _RoundTrip(residual, log2Size, qp: 0, sine: false);

    var worst = _WorstDifference(residual, reconstructed);
    Assert.That(worst, Is.LessThanOrEqualTo(2),
      $"at the finest quantiser a {size}x{size} block should come back within a step or two, worst was {worst}");
  }

  [TestCase(0)]
  [TestCase(12)]
  [TestCase(22)]
  [TestCase(34)]
  [TestCase(51)]
  [Category("RoundTrip")]
  [Category("Boundary")]
  public void TheErrorGrowsWithTheQuantiserAndNothingElse(int qp) {
    var random = new Random(900 + qp);
    var residual = new int[8 * 8];
    for (var i = 0; i < residual.Length; ++i)
      residual[i] = random.Next(-100, 101);

    var reconstructed = _RoundTrip(residual, log2Size: 3, qp, sine: false);

    // A quantiser step is roughly 2^(qp/6); the bound is that with room for the transform's own
    // rounding. The point of the assertion is the shape: an error that grew with the block size or
    // stayed flat as the quantiser coarsened would mean a shift in the wrong place.
    var allowed = 8 + (int)Math.Ceiling(Math.Pow(2.0, qp / 6.0) * 3);
    var worst = _WorstDifference(residual, reconstructed);
    Assert.That(worst, Is.LessThanOrEqualTo(allowed), $"worst difference {worst} at quantiser {qp}");
  }

  [Test]
  [Category("RoundTrip")]
  [Category("HappyPath")]
  public void TheSineTransformRoundTripsAsWellAsTheCosineOne() {
    var random = new Random(7);
    var residual = new int[4 * 4];
    for (var i = 0; i < residual.Length; ++i)
      residual[i] = random.Next(-64, 65);

    var reconstructed = _RoundTrip(residual, log2Size: 2, qp: 0, sine: true);

    Assert.That(_WorstDifference(residual, reconstructed), Is.LessThanOrEqualTo(2));
  }

  [Test]
  [Category("Boundary")]
  public void AFlatResidualBecomesOneCoefficient() {
    // A constant block is the transform's first basis function alone, so everything but the direct
    // current coefficient has to come out zero. Anything else means the matrix is being applied the
    // wrong way round.
    var residual = new int[8 * 8];
    Array.Fill(residual, 40);

    var coefficients = (int[])residual.Clone();
    H265ForwardTransform.Forward(coefficients, log2Size: 3, sine: false, bitDepth: 8);

    Assert.Multiple(() => {
      Assert.That(coefficients[0], Is.Not.Zero, "the direct current coefficient carries a flat block");
      Assert.That(coefficients.Skip(1).All(static value => value == 0), Is.True,
        "a flat block has no alternating content: " + string.Join(", ", coefficients.Take(8)));
    });
  }

  [Test]
  [Category("Boundary")]
  public void AZeroResidualQuantisesToNothing() {
    var block = new int[16 * 16];

    H265ForwardTransform.Forward(block, log2Size: 4, sine: false, bitDepth: 8);
    H265ForwardTransform.Quantise(block, log2Size: 4, qp: 26, bitDepth: 8, intra: false);

    Assert.That(block.All(static value => value == 0), Is.True);
  }

  [Test]
  [Category("EdgeCase")]
  public void ASmallResidualAtACoarseQuantiserQuantisesAwayEntirely() {
    // The cheapest block there is: nothing is coded at all. An encoder that produced a level here
    // would be spending bits on a difference the quantiser cannot represent.
    var random = new Random(5);
    var residual = new int[8 * 8];
    for (var i = 0; i < residual.Length; ++i)
      residual[i] = random.Next(-1, 2);

    var block = (int[])residual.Clone();
    H265ForwardTransform.Forward(block, log2Size: 3, sine: false, bitDepth: 8);
    H265ForwardTransform.Quantise(block, log2Size: 3, qp: 45, bitDepth: 8, intra: false);

    Assert.That(block.All(static value => value == 0), Is.True,
      "a residual of plus or minus one should not survive a quantiser of 45");
  }

  private static int[] _RoundTrip(int[] residual, int log2Size, int qp, bool sine) {
    var block = (int[])residual.Clone();

    H265ForwardTransform.Forward(block, log2Size, sine, bitDepth: 8);
    H265ForwardTransform.Quantise(block, log2Size, qp, bitDepth: 8, intra: false);
    H265Dequantiser.Scale(block, log2Size, qp, bitDepth: 8, scalingList: null, matrixId: 0);
    H265Transform.Inverse(block, log2Size, sine, bitDepth: 8);

    return block;
  }

  private static int _WorstDifference(int[] expected, int[] actual) {
    var worst = 0;
    for (var i = 0; i < expected.Length; ++i)
      worst = Math.Max(worst, Math.Abs(expected[i] - actual[i]));

    return worst;
  }
}
