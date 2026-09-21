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
  public void SingleVectorMacroblockAndInter4vHaveDifferentInternalMirrors() {
    // A one-vector macroblock is one 16x16 qpel operation. Four-vector mode applies the same filter
    // independently to each 8x8 luminance block, so the internal x=8 boundary is a real mirror only
    // for INTER4V. A ramp makes the distinction one sample wide and therefore difficult to hide with
    // a coincidental flat-picture pass.
    var reference = new byte[17 * 17];
    for (var y = 0; y < 17; ++y)
      for (var x = 0; x < 17; ++x)
        reference[y * 17 + x] = (byte)(12 * x);

    Span<int> macroblock = stackalloc int[16 * 16];
    Mpeg4QuarterSample.Predict(macroblock, reference, 17, 0, 17, 17, 0, 0, 16, 2, 0, 0);

    Span<int> inter4v = stackalloc int[16 * 16];
    Span<int> block = stackalloc int[64];
    for (var blockIndex = 0; blockIndex < 4; ++blockIndex) {
      var left = (blockIndex & 1) * 8;
      var top = (blockIndex >> 1) * 8;
      Mpeg4QuarterSample.Predict(block, reference, 17, 0, 17, 17, left, top, 8, 2, 0, 0);
      for (var y = 0; y < 8; ++y)
        block.Slice(y * 8, 8).CopyTo(inter4v.Slice((top + y) * 16 + left, 8));
    }

    // The samples are read out before the assertions: a stack-allocated span cannot be captured by
    // the closure Assert.Multiple takes.
    var macroblockLeftOfBoundary = macroblock[7];
    var inter4vLeftOfBoundary = inter4v[7];
    var macroblockRightOfBoundary = macroblock[8];
    var inter4vRightOfBoundary = inter4v[8];

    Assert.Multiple(() => {
      Assert.That(macroblockLeftOfBoundary, Is.EqualTo(90));
      Assert.That(inter4vLeftOfBoundary, Is.EqualTo(91));
      Assert.That(macroblockRightOfBoundary, Is.EqualTo(102));
      Assert.That(inter4vRightOfBoundary, Is.EqualTo(101));
    });
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

    // Read out before the assertions: a stack-allocated span cannot be captured by the closure
    // Assert.Multiple takes.
    int[] samples = [prediction[0], prediction[3], prediction[3 * 8], prediction[3 * 8 + 3], prediction[7 * 8 + 7]];

    Assert.Multiple(() => {
      Assert.That(samples[0], Is.EqualTo(12));
      Assert.That(samples[1], Is.EqualTo(64));
      Assert.That(samples[2], Is.EqualTo(46));
      Assert.That(samples[3], Is.EqualTo(98));
      Assert.That(samples[4], Is.EqualTo(210));
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
  public void PredictionRequiresACompleteDestinationForItsBlockSize() {
    var eightReference = new byte[9 * 9];
    var sixteenReference = new byte[17 * 17];

    Assert.Multiple(() => {
      Assert.Throws<ArgumentException>(() =>
        Mpeg4QuarterSample.Predict(new int[63], eightReference, 9, 0, 9, 9, 0, 0, 8, 0, 0, 0));
      Assert.Throws<ArgumentException>(() =>
        Mpeg4QuarterSample.Predict(new int[255], sixteenReference, 17, 0, 17, 17, 0, 0, 16, 0, 0, 0));
    });
  }

  [Test]
  [Category("Unit")]
  public void UnsupportedPredictionBlockSizeIsRejected() {
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      Mpeg4QuarterSample.Predict(new int[12 * 12], new byte[13 * 13], 13, 0, 13, 13, 0, 0, 12, 0, 0, 0));
  }
}
