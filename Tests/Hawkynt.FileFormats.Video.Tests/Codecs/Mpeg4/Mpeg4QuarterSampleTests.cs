using System;

namespace FileFormat.Codecs.Mpeg4.Tests;

/// <summary>ISO/IEC 14496-2 quarter-sample interpolation and vector reduction.</summary>
[TestFixture]
public sealed class Mpeg4QuarterSampleTests {

  [Test]
  [Category("Unit")]
  public void HalfPositionsMirrorTheEightByEightBlockBoundary() {
    // Figure 7-30 mirrors the first source sample into the first missing tap. For x=0 the taps are
    // 40,20,0,0,20,40,60,80 rather than samples from outside this 8x8 prediction block. With the
    // [-8,24,-48,160,160,-48,24,-8] / 256 filter that reconstructs nine, and the other seven
    // positions below exercise the opposite mirrored boundary as well.
    var reference = new byte[9 * 9];
    for (var y = 0; y < 9; ++y)
      for (var x = 0; x < 9; ++x)
        reference[y * 9 + x] = (byte)(20 * x);

    Span<int> prediction = stackalloc int[64];
    Mpeg4QuarterSample.Predict(prediction, reference, 9, 0, 9, 9, 0, 0, 2, 0, 0);

    int[] expected = [9, 30, 49, 70, 90, 111, 130, 151];
    Assert.That(prediction[..8].ToArray(), Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void QuarterPositionsAreFilteredHorizontallyBeforeVertically() {
    // A two-dimensional gradient makes the two passes observable independently. These five samples
    // are obtained directly from the corrected Figure 7-30/7-31 procedure: horizontal 1/4 first,
    // then vertical 3/4, clipping each eight-tap result before the bilinear quarter step.
    var reference = new byte[9 * 9];
    for (var y = 0; y < 9; ++y)
      for (var x = 0; x < 9; ++x)
        reference[y * 9 + x] = (byte)(17 * x + 11 * y);

    Span<int> prediction = stackalloc int[64];
    Mpeg4QuarterSample.Predict(prediction, reference, 9, 0, 9, 9, 0, 0, 1, 3, 0);

    Assert.Multiple(() => {
      Assert.That(prediction[0], Is.EqualTo(12));
      Assert.That(prediction[3], Is.EqualTo(64));
      Assert.That(prediction[3 * 8], Is.EqualTo(46));
      Assert.That(prediction[3 * 8 + 3], Is.EqualTo(98));
      Assert.That(prediction[7 * 8 + 7], Is.EqualTo(210));
    });
  }

  [Test]
  [Category("Unit")]
  public void ReferenceVopEdgeIsClampedBeforeTheBlockIsMirrored() {
    // A vector of -1 quarter sample has an integer anchor one sample left of the VOP and fraction 3.
    // Clause 7.6.4 first repeats the VOP's left edge, making the 9-sample source
    // 0,0,20,40,...; only that source is then mirrored for the qpel filter.
    var reference = new byte[9 * 9];
    for (var y = 0; y < 9; ++y)
      for (var x = 0; x < 9; ++x)
        reference[y * 9 + x] = (byte)(20 * x);

    Span<int> prediction = stackalloc int[64];
    Mpeg4QuarterSample.Predict(prediction, reference, 9, 0, 9, 9, 0, 0, -1, 0, 0);

    Assert.That(prediction[..4].ToArray(), Is.EqualTo(new[] { 0, 14, 36, 55 }));
  }

  [TestCase(-8, -4)]
  [TestCase(-5, -3)]
  [TestCase(-4, -2)]
  [TestCase(-3, -1)]
  [TestCase(-2, -1)]
  [TestCase(-1, -1)]
  [TestCase(0, 0)]
  [TestCase(1, 1)]
  [TestCase(2, 1)]
  [TestCase(3, 1)]
  [TestCase(4, 2)]
  [TestCase(5, 3)]
  [TestCase(8, 4)]
  [Category("Unit")]
  public void DirectModeReducesQuarterVectorsWithTableSevenThirteen(int quarterSample, int halfSample) {
    Assert.That(Mpeg4QuarterSample.ToDirectHalfSample(quarterSample), Is.EqualTo(halfSample));
  }

  [Test]
  [Category("Unit")]
  public void QuarterSampleChromaHalvesEachLumaVectorBeforeSumming() {
    // 7.6.5 says to divide the vectors before summation. Doing the tempting algebraic equivalent
    // after summation is observably wrong because integer division discards each vector's odd part:
    // [1,1,1,3] -> [0,0,0,1] -> Table 7-10 position 1 -> zero half-samples.
    Assert.That(Mpeg4QuarterSample.ToChroma(1, 1, 1, 3), Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void PredictionRequiresOneCompleteEightByEightDestination() {
    var reference = new byte[9 * 9];
    var tooSmall = new int[63];

    Assert.Throws<ArgumentException>(() =>
      Mpeg4QuarterSample.Predict(tooSmall, reference, 9, 0, 9, 9, 0, 0, 0, 0, 0));
  }
}
