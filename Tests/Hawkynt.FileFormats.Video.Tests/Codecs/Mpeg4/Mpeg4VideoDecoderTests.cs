using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Codecs.Mpeg4.Tests;

/// <summary>
/// The MPEG-4 Part 2 decoder, on streams built here bit by bit.
/// </summary>
/// <remarks>
/// The decoder's arithmetic was checked against ffmpeg over twenty-one encoded streams, frame by
/// frame and sample by sample; what these tests add is the part that comparison cannot reach. Some of
/// it is syntax ffmpeg's encoder never emits — macroblock stuffing, all three escape forms in one
/// block, a DC differential wide enough to need its marker bit — and some of it is the refusals,
/// which by definition no valid stream produces.
/// <para/>
/// The expected samples are worked out from the standard rather than recorded from a run. Where a
/// number here disagrees with the decoder, one of the two is wrong and the arithmetic in the comment
/// above it says which.
/// </remarks>
[TestFixture]
public sealed class Mpeg4VideoDecoderTests {

  // ============================================================================================
  // Intra pictures
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFlatIntraPictureIsMidGrey() {
    // At quantiser 1 the DC step is 8 for both components, so the DC of a block with no neighbours
    // is predicted at 1024/8 = 128 and a differential of zero leaves it there. The transform of a
    // block whose only coefficient is 128 * 8 is 128 everywhere, and a luminance of 128 with neutral
    // chrominance is (298 * (128 - 16) + 128) >> 8 = 130.
    var frame = _Decode(_OneMacroblockPicture(stream => stream.FlatIntraMacroblock())).Single();

    Assert.That(frame.Width, Is.EqualTo(16));
    Assert.That(frame.Height, Is.EqualTo(16));
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
  }

  [Test]
  [Category("Unit")]
  public void TheIntraDcIsPredictedFromANeighbourAndNotCodedOutright() {
    // Two macroblocks side by side. The first codes a differential of +22 against a prediction of
    // 128, giving 150; the second codes zero against a prediction taken from the first, giving 150
    // again. A decoder without the prediction would put the second at 128 — which is a picture, and
    // is the failure this whole clause exists to make visible.
    var frame = _Decode(_Picture(32, 16, stream => stream
      .FlatIntraMacroblock(22)
      .FlatIntraMacroblock())).Single();

    Assert.That(_Red(frame, 0, 0), Is.EqualTo(_Grey(150)));
    Assert.That(_Red(frame, 16, 0), Is.EqualTo(_Grey(150)));
  }

  [Test]
  [Category("Unit")]
  public void AnAlternatingCurrentCoefficientIsDequantisedAndTransformed() {
    // One coefficient at scan position 1, level 40, quantiser 1, the H.263 quantisation method. The
    // quantiser is odd, so the reconstruction level is 1 * (2 * 40 + 1) = 81 with nothing subtracted.
    // With the DC at 1024 the transform is 128 + 81/2 * cos((2x+1)pi/16) / (2 sqrt 2), constant down
    // each column, which rounds to the eight luminances below.
    var frame = _Decode(_OneMacroblockPicture(stream => stream
      .Code(Mpeg4TestStream.IntraMacroblock).Bits(0, 1).Code(Mpeg4TestStream.FirstLuminanceCoded)
      .IntraDc(0, luminance: true).EscapedCoefficient(last: true, run: 0, level: 40)
      .IntraDc(0, luminance: true).IntraDc(0, luminance: true).IntraDc(0, luminance: true)
      .IntraDc(0, luminance: false).IntraDc(0, luminance: false))).Single();

    int[] expected = [142, 140, 136, 131, 125, 120, 116, 114];
    for (var x = 0; x < 8; ++x)
      Assert.That(_Red(frame, x, 0), Is.EqualTo(_Grey(expected[x])), $"column {x}");

    for (var y = 0; y < 8; ++y)
      Assert.That(_Red(frame, 0, y), Is.EqualTo(_Grey(142)), $"row {y}");
  }

  [Test]
  [Category("Unit")]
  public void TheStuffingCodewordCarriesNoMacroblock() {
    var frame = _Decode(_OneMacroblockPicture(stream => stream
      .Code(Mpeg4TestStream.MacroblockStuffing)
      .Code(Mpeg4TestStream.MacroblockStuffing)
      .FlatIntraMacroblock())).Single();

    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
  }

  [Test]
  [Category("Unit")]
  public void AQuantiserDifferenceIsClippedRatherThanRefused() {
    // The quantiser starts at 1 and DQUANT states minus two, which clause 6.3.6 clips back to 1. The
    // coefficient that follows therefore reconstructs at quantiser 1: level 40 gives 81, the same
    // eight luminances as the picture above.
    var frame = _Decode(_OneMacroblockPicture(stream => stream
      .Code(Mpeg4TestStream.IntraMacroblockWithQuantiser).Bits(0, 1).Code(Mpeg4TestStream.FirstLuminanceCoded)
      .Bits(1, 2)
      .IntraDc(0, luminance: true).EscapedCoefficient(last: true, run: 0, level: 40)
      .IntraDc(0, luminance: true).IntraDc(0, luminance: true).IntraDc(0, luminance: true)
      .IntraDc(0, luminance: false).IntraDc(0, luminance: false))).Single();

    Assert.That(_Red(frame, 0, 0), Is.EqualTo(_Grey(142)));
  }

  // ============================================================================================
  // The three escape forms — ISO/IEC 14496-2, 7.4.1.3
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheFirstEscapeFormAddsTheLargestLevelTheTableHolds() {
    // Escape type 1 is the escape code, a zero, and then an ordinary coefficient code whose level is
    // a difference from the largest the table has for that run. The intra table's largest level for
    // the last coefficient at run nought is 8, so the code for level 3 there means 3 + 8 = 11.
    //
    // At quantiser 2 the reconstruction level is 2 * (2 * 11 + 1) - 1 = 45, which is not what the
    // code for level 3 alone would give — that would be 2 * 7 - 1 = 13.
    var frame = _Decode(_PictureWithFirstBlockCoefficient(
      quantiser: 2,
      coefficient: stream => stream.Code(Mpeg4TestStream.CoefficientEscape).Bits(0, 1)
        .Code("0001 0110").Bits(0, 1))).Single();

    // The coefficient sits at scan position 1 with a reconstruction level of 45, so the block is
    // 128 + 45/2 * cos((2x+1)pi/16) / (2 sqrt 2) across.
    int[] expected = [136, 135, 132, 130, 126, 124, 121, 120];
    for (var x = 0; x < 8; ++x)
      Assert.That(_Red(frame, x, 0), Is.EqualTo(_Grey(expected[x])), $"column {x}");
  }

  [Test]
  [Category("Unit")]
  public void TheSecondEscapeFormAddsTheLargestRunTheTableHolds() {
    // Escape type 2 is the escape code, a one and a zero, and then an ordinary code whose run is a
    // difference from the largest the table has for that level. The intra table's largest run for a
    // last coefficient of level 1 is 20, so the code for run 0 there means 0 + 20 + 1 = 21.
    //
    // The coefficient therefore lands at scan position 21 rather than 1, which in the zig-zag is
    // raster position 48 — the first column of the sixth row, a purely vertical frequency.
    var frame = _Decode(_PictureWithFirstBlockCoefficient(
      quantiser: 1,
      coefficient: stream => stream.Code(Mpeg4TestStream.CoefficientEscape).Bits(2, 2)
        .Code("0111").Bits(0, 1))).Single();

    // A vertical frequency is constant across each row, so the whole first row is one value.
    var first = _Red(frame, 0, 0);
    for (var x = 1; x < 8; ++x)
      Assert.That(_Red(frame, x, 0), Is.EqualTo(first), $"column {x}");

    // …and it differs down the column, which a coefficient left at scan position 1 could not do.
    Assert.That(_Red(frame, 0, 1), Is.Not.EqualTo(first));
  }

  [Test]
  [Category("Unit")]
  public void TheThirdEscapeFormWritesTheWholeTripleOut() {
    // Escape type 3 is the escape code, two ones, and then the last flag, the run and a twelve-bit
    // level with a marker bit on each side of it. Level 40 is past anything the table can reach by
    // either of the other two forms at run nought.
    var frame = _Decode(_PictureWithFirstBlockCoefficient(
      quantiser: 1,
      coefficient: stream => stream.EscapedCoefficient(last: true, run: 0, level: 40))).Single();

    Assert.That(_Red(frame, 0, 0), Is.EqualTo(_Grey(142)));
  }

  [TestCase(0, TestName = "An escaped level of zero is refused")]
  [TestCase(-2048, TestName = "An escaped level of minus 2048 is refused")]
  [Category("Unit")]
  public void AnEscapedLevelTheStandardForbidsIsRefused(int level) {
    var stream = _PictureWithFirstBlockCoefficient(
      quantiser: 1, coefficient: s => s.EscapedCoefficient(last: true, run: 0, level: level));

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("Table B-18"));
  }

  [Test]
  [Category("Unit")]
  public void ARunPastTheEndOfABlockIsRefused() {
    var stream = _PictureWithFirstBlockCoefficient(
      quantiser: 1, coefficient: s => s.EscapedCoefficient(last: true, run: 63, level: 1));

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("scan position"));
  }

  [Test]
  [Category("Unit")]
  public void AMarkerBitInsideAnEscapedCoefficientIsChecked() {
    var stream = _OneMacroblockPicture(s => s
      .Code(Mpeg4TestStream.IntraMacroblock).Bits(0, 1).Code(Mpeg4TestStream.FirstLuminanceCoded)
      .IntraDc(0, luminance: true)
      .Code(Mpeg4TestStream.CoefficientEscape).Bits(3, 2).Bits(1, 1).Bits(0, 6)
      .Bits(0, 1)      // the marker before the level, which the standard fixes at one
      .Bits(40, 12).Bits(1, 1)
      .IntraDc(0, luminance: true).IntraDc(0, luminance: true).IntraDc(0, luminance: true)
      .IntraDc(0, luminance: false).IntraDc(0, luminance: false));

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("marker bit"));
  }

  // ============================================================================================
  // Motion compensation
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AWholeSampleMotionVectorShiftsThePrediction() {
    // The MVD table's values are twice what the standard prints beside them, so its code for "1"
    // means two half-samples, which is one whole sample.
    var frames = _Decode(_ShiftedPicture("0010"));

    Assert.That(frames.Count, Is.EqualTo(2));

    int[] reference = [142, 140, 136, 131, 125, 120, 116, 114];
    for (var x = 0; x < 7; ++x)
      Assert.That(_Red(frames[1], x, 0), Is.EqualTo(_Grey(reference[x + 1])), $"column {x}");
  }

  [Test]
  [Category("Unit")]
  public void AHalfSampleMotionVectorInterpolatesThePrediction() {
    // The code for "0.5" means one half-sample: each sample is the mean of the two it sits between,
    // rounded upward because this picture's rounding type is zero.
    var frames = _Decode(_ShiftedPicture("010"));

    int[] reference = [142, 140, 136, 131, 125, 120, 116, 114];
    for (var x = 0; x < 7; ++x)
      Assert.That(_Red(frames[1], x, 0), Is.EqualTo(_Grey((reference[x] + reference[x + 1] + 1) >> 1)), $"column {x}");
  }

  [Test]
  [Category("Unit")]
  public void TheRoundingTypeDecidesWhichWayAHalfSampleRounds() {
    // The same half-sample vector with the picture's rounding type set, which subtracts one from the
    // bias. An encoder alternates the flag so that the interpolation does not drift in one direction
    // through a run of predicted pictures, and a decoder that ignored it would drift with it.
    var frames = _Decode(_ShiftedPicture("010", rounding: 1));

    int[] reference = [142, 140, 136, 131, 125, 120, 116, 114];
    for (var x = 0; x < 7; ++x)
      Assert.That(_Red(frames[1], x, 0), Is.EqualTo(_Grey((reference[x] + reference[x + 1]) >> 1)), $"column {x}");
  }

  [Test]
  [Category("Unit")]
  public void AMacroblockThatIsNotCodedIsTheReferenceUnchanged() {
    var stream = _Picture(16, 16, s => s.FlatIntraMacroblock(22))
      .Concat(_PredictedPicture(16, 16, s => s.Bits(1, 1)))
      .ToArray();

    var frames = _Decode(stream);

    Assert.That(frames.Count, Is.EqualTo(2));
    Assert.That(frames[1].PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { _Grey(150) }));
  }

  [Test]
  [Category("Unit")]
  public void AVectorMayPointOutsideThePictureAndReadsTheEdgeSample() {
    // MPEG-4 extends a reference picture by repeating its edge, so a vector of minus one whole
    // sample at the left edge reads the first column twice rather than being refused. The reference
    // is flat, so what is checked is that the picture decodes and keeps its value.
    var stream = _Picture(16, 16, s => s.FlatIntraMacroblock(22))
      .Concat(_PredictedPicture(16, 16, s => s
        .Bits(0, 1).Code(Mpeg4TestStream.InterMacroblock).Code(Mpeg4TestStream.AllLuminanceCoded)
        .Code("0011").Code("1")))
      .ToArray();

    var frames = _Decode(stream);

    Assert.That(frames.Count, Is.EqualTo(2));
    Assert.That(frames[1].PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { _Grey(150) }));
  }

  [TestCase(0, 0)]
  [TestCase(4, 1)]
  [TestCase(8, 1)]
  [TestCase(12, 1)]
  [TestCase(16, 2)]
  [TestCase(20, 3)]
  [TestCase(-4, -1)]
  [TestCase(-16, -2)]
  [Category("Unit")]
  public void TheChrominanceVectorRoundsTowardsTheHalfSample(int sumOfFour, int expected) {
    // Table 7-6: the sum of the four luminance vectors is sixteen times the chrominance vector in
    // whole samples, and the sixteenth positions between are pulled towards the half rather than to
    // the nearest — only the last two of the sixteen reach the next whole sample.
    Assert.That(Mpeg4MotionCompensation.ToChroma(sumOfFour), Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void FourMotionVectorsPerMacroblockAreRead() {
    // An INTER4V macroblock carries one vector for each luminance block, and the second, third and
    // fourth predict from the first rather than from the macroblock beside them. Coding a whole
    // sample into the first and a difference of zero into the other three leaves all four at one
    // whole sample.
    var stream = _Picture(32, 16, s => s.FlatIntraMacroblock(22).FlatIntraMacroblock())
      .Concat(_PredictedPicture(32, 16, s => s
        .Bits(0, 1).Code(Mpeg4TestStream.InterMacroblockWithFourVectors).Code(Mpeg4TestStream.AllLuminanceCoded)
        .Code("0010").Code("1")
        .Code("1").Code("1")
        .Code("1").Code("1")
        .Code("1").Code("1")
        .Bits(1, 1)))
      .ToArray();

    var frames = _Decode(stream);

    Assert.That(frames.Count, Is.EqualTo(2));
    Assert.That(frames[1].PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { _Grey(150) }));
  }

  // ============================================================================================
  // Pictures that carry nothing, and pictures predicted from both sides
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APictureThatStatesItIsNotCodedIsTheOneBeforeItAgain() {
    var stream = _Picture(16, 16, s => s.FlatIntraMacroblock(22))
      .Concat(new Mpeg4TestStream()
        .VideoObjectPlane(codingType: 1, timeIncrement: 1, isCoded: false).ToArray())
      .ToArray();

    var frames = _Decode(stream);

    Assert.That(frames.Count, Is.EqualTo(2));
    Assert.That(frames[1].PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { _Grey(150) }));
  }

  [Test]
  [Category("Unit")]
  public void ABidirectionallyCodedPictureIsShownBetweenTheTwoItIsPredictedFrom() {
    // Three pictures: an intra one at 150, a predicted one at 100 two ticks later, and between them
    // in time a bidirectionally coded one that predicts from both. Its macroblock states MODB of one,
    // which is the direct mode with a zero delta — and with the anchor's vectors zero that is the
    // mean of the two, (150 + 100 + 1) / 2 = 125.
    //
    // The order they come out in is the order they are shown in and not the order they were coded in,
    // which is the whole reason an anchor is held back.
    var frames = _Decode(_BidirectionalStream());

    Assert.That(frames.Count, Is.EqualTo(3));
    Assert.That(frames[0].PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { _Grey(150) }));
    Assert.That(frames[1].PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { _Grey(125) }));
    Assert.That(frames[2].PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { _Grey(100) }));
  }

  [Test]
  [Category("Unit")]
  public void ABidirectionallyCodedPictureCountsItsSecondsFromThePictureShownBeforeIt() {
    // Two streams that say exactly the same thing about where their three pictures sit in time, and
    // differ only in whether the group crosses a whole second. In the first the anchors are at ticks
    // 0 and 3; in the second they are at 24 and 27, so the second one is a whole second past the
    // first and says so with a modulo time base of one. The bidirectionally coded picture sits a
    // third of the way between the anchors in both, so both must decode to the same picture.
    //
    // They do not if the seconds of a bidirectionally coded picture are counted from the anchor
    // decoded before it. That anchor is the one shown *after* it, which in the second stream is a
    // second further on — so its distance from the anchor it predicts forwards from comes out
    // twenty-six ticks instead of one, and direct mode scales the vectors it inherits by twenty-six
    // times too much. Nothing shows until a group of pictures crosses a second, and then two frames
    // in twenty-five are badly wrong with everything either side of them right.
    var withoutBoundary = _Decode(_DirectModeStream(intraIncrement: 0, anchorSeconds: 0, anchorIncrement: 3,
      bidirectionalSeconds: 0, bidirectionalIncrement: 1));
    var acrossBoundary = _Decode(_DirectModeStream(intraIncrement: 24, anchorSeconds: 1, anchorIncrement: 2,
      bidirectionalSeconds: 1, bidirectionalIncrement: 0));

    Assert.That(withoutBoundary.Count, Is.EqualTo(3));
    Assert.That(acrossBoundary.Count, Is.EqualTo(3));
    Assert.That(acrossBoundary[1].PixelData, Is.EqualTo(withoutBoundary[1].PixelData));
  }

  [Test]
  [Category("Unit")]
  public void ABidirectionallyCodedPictureBeforeBothItsReferencesIsRefused() {
    var stream = _Picture(16, 16, s => s.FlatIntraMacroblock(22))
      .Concat(new Mpeg4TestStream()
        .VideoObjectPlane(codingType: 2, timeIncrement: 1).Code("1").ToArray())
      .ToArray();

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("both of the pictures"));
  }

  // ============================================================================================
  // Video packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AVideoPacketRestartsTheQuantiserAndThePrediction() {
    // Two macroblocks with a video packet boundary between them. The packet states quantiser 2, and
    // the second macroblock's DC prediction starts afresh from the middle of the range rather than
    // from the first macroblock — so it reconstructs at 128 and not at 150.
    var stream = new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(width: 32, height: 16, resyncMarkers: true)
      .VideoObjectPlane(quantiser: 1)
      .FlatIntraMacroblock(22)
      .ResyncMarker(macroblockNumber: 1, numberBits: 1, quantiser: 1, quantiserPrecision: 5)
      .FlatIntraMacroblock()
      .ToArray();

    var frame = _Decode(stream).Single();

    Assert.That(_Red(frame, 0, 0), Is.EqualTo(_Grey(150)));
    Assert.That(_Red(frame, 16, 0), Is.EqualTo(_Grey(128)));
  }

  [Test]
  [Category("Unit")]
  public void AVideoPacketThatDoesNotBeginWhereItIsDueIsRefused() {
    var stream = new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(width: 48, height: 16, resyncMarkers: true)
      .VideoObjectPlane(quantiser: 1)
      .FlatIntraMacroblock(22)
      .ResyncMarker(macroblockNumber: 2, numberBits: 2, quantiser: 1, quantiserPrecision: 5)
      .FlatIntraMacroblocks(2)
      .ToArray();

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("macroblock_number"));
  }

  // ============================================================================================
  // Streams
  // ============================================================================================

  private static byte[] _Picture(int width, int height, Action<Mpeg4TestStream> macroblocks) {
    var stream = new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(width: width, height: height)
      .VideoObjectPlane(quantiser: 1);

    macroblocks(stream);
    return stream.ToArray();
  }

  private static byte[] _OneMacroblockPicture(Action<Mpeg4TestStream> macroblocks)
    => _Picture(16, 16, macroblocks);

  private static byte[] _PredictedPicture(int width, int height, Action<Mpeg4TestStream> macroblocks) {
    var stream = new Mpeg4TestStream().VideoObjectPlane(codingType: 1, quantiser: 1, timeIncrement: 1);
    macroblocks(stream);
    return stream.ToArray();
  }

  /// <summary>An intra picture whose first luminance block carries one coefficient at scan position 1.</summary>
  private static byte[] _PictureWithFirstBlockCoefficient(int quantiser, Action<Mpeg4TestStream> coefficient) {
    var stream = new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(width: 16, height: 16)
      .VideoObjectPlane(quantiser: quantiser)
      .Code(Mpeg4TestStream.IntraMacroblock).Bits(0, 1).Code(Mpeg4TestStream.FirstLuminanceCoded)
      .IntraDc(0, luminance: true);

    coefficient(stream);

    return stream
      .IntraDc(0, luminance: true).IntraDc(0, luminance: true).IntraDc(0, luminance: true)
      .IntraDc(0, luminance: false).IntraDc(0, luminance: false)
      .ToArray();
  }

  /// <summary>
  /// An intra picture whose first block carries a horizontal ramp, and a predicted picture that
  /// displaces it by the given vector.
  /// </summary>
  private static byte[] _ShiftedPicture(string motionCode, int rounding = 0) {
    var stream = new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(width: 32, height: 16)
      .VideoObjectPlane(quantiser: 1)
      .Code(Mpeg4TestStream.IntraMacroblock).Bits(0, 1).Code(Mpeg4TestStream.FirstLuminanceCoded)
      .IntraDc(0, luminance: true).EscapedCoefficient(last: true, run: 0, level: 40)
      .IntraDc(0, luminance: true).IntraDc(0, luminance: true).IntraDc(0, luminance: true)
      .IntraDc(0, luminance: false).IntraDc(0, luminance: false)
      .FlatIntraMacroblock();

    return stream
      .VideoObjectPlane(codingType: 1, quantiser: 1, timeIncrement: 1, rounding: rounding)
      .Bits(0, 1).Code(Mpeg4TestStream.InterMacroblock).Code(Mpeg4TestStream.AllLuminanceCoded)
      .Code(motionCode).Code("1")
      .Bits(1, 1)
      .ToArray();
  }

  /// <summary>
  /// An intra picture, a predicted one that displaces half of it, and a direct-mode bidirectional one
  /// between them — whose vectors are therefore the predicted picture's, scaled by where in time it
  /// sits.
  /// </summary>
  /// <remarks>
  /// The times are the caller's so that two streams saying the same thing about the spacing of three
  /// pictures, one of them crossing a whole second and one not, can be compared against each other.
  /// Comparing them rather than against numbers worked out by hand is what makes the test about the
  /// time base and not about the interpolation.
  /// </remarks>
  private static byte[] _DirectModeStream(
    int intraIncrement, int anchorSeconds, int anchorIncrement, int bidirectionalSeconds,
    int bidirectionalIncrement)
    => new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(width: 32, height: 16, lowDelay: false)
      .VideoObjectPlane(quantiser: 1, timeIncrement: intraIncrement)
      .FlatIntraMacroblock(22)
      .FlatIntraMacroblock(-50)

      // The predicted picture moves its first macroblock fifteen samples along, so the vector the
      // bidirectional picture inherits from it is not zero and the scaling can be seen.
      .VideoObjectPlane(codingType: 1, quantiser: 1, seconds: anchorSeconds, timeIncrement: anchorIncrement)
      .Bits(0, 1).Code(Mpeg4TestStream.InterMacroblock).Code(Mpeg4TestStream.AllLuminanceCoded)
      .Code("0000 0000 0100").Code("1")
      .Bits(1, 1)

      // MODB of one: the direct mode with a zero delta, so the vectors are entirely the anchor's.
      .VideoObjectPlane(codingType: 2, quantiser: 1, seconds: bidirectionalSeconds,
        timeIncrement: bidirectionalIncrement)
      .Code("1").Code("1")
      .ToArray();

  /// <summary>An intra picture, a predicted one two ticks later, and a bidirectional one between them.</summary>
  private static byte[] _BidirectionalStream()
    => new Mpeg4TestStream().VisualObjectSequence()
      .VideoObjectLayer(width: 16, height: 16, lowDelay: false)
      .VideoObjectPlane(quantiser: 1)
      .FlatIntraMacroblock(22)

      // The predicted picture is intra coded inside, which is the shortest way to give the backward
      // reference a value of its own without coding a residual against the first one.
      .VideoObjectPlane(codingType: 1, quantiser: 1, timeIncrement: 2)
      .Bits(0, 1).Code(Mpeg4TestStream.IntraMacroblockInPredictedPicture).Bits(0, 1)
      .Code(Mpeg4TestStream.NoLuminanceCoded)
      .IntraDc(-28, luminance: true).IntraDc(0, luminance: true)
      .IntraDc(0, luminance: true).IntraDc(0, luminance: true)
      .IntraDc(0, luminance: false).IntraDc(0, luminance: false)

      // MODB of one: no type, no pattern, no vector — the direct mode with a zero delta.
      .VideoObjectPlane(codingType: 2, quantiser: 1, timeIncrement: 1)
      .Code("1")
      .ToArray();

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static List<RawImage> _Decode(byte[] stream) {
    var decoder = Mpeg4VideoDecoder.Create(
      new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("mp4v") });

    var frames = new List<RawImage>();
    foreach (var picture in _Pictures(stream))
      if (decoder.TryDecode(new(0, picture), out var frame))
        frames.Add(frame);

    frames.AddRange(decoder.Flush());
    return frames;
  }

  /// <summary>Cuts the built stream at its picture start codes, the way a container hands out packets.</summary>
  private static IEnumerable<byte[]> _Pictures(byte[] stream) {
    var starts = new List<int>();
    for (var offset = 0; offset + 4 <= stream.Length; ++offset)
      if (stream[offset] == 0 && stream[offset + 1] == 0 && stream[offset + 2] == 1
          && stream[offset + 3] == Mpeg4StartCode.VideoObjectPlane)
        starts.Add(offset);

    if (starts.Count == 0) {
      yield return stream;
      yield break;
    }

    // Everything before the first picture is the headers, which belong with it.
    for (var index = 0; index < starts.Count; ++index) {
      var from = index == 0 ? 0 : starts[index];
      var to = index + 1 < starts.Count ? starts[index + 1] : stream.Length;
      yield return stream[from..to];
    }
  }

  private static byte _Red(RawImage image, int x, int y) => image.PixelData[(y * image.Width + x) * 3];

  /// <summary>
  /// The red — and, with neutral chrominance, also green and blue — a luminance converts to.
  /// </summary>
  private static byte _Grey(int luminance) => (byte)Math.Clamp((298 * (luminance - 16) + 128) >> 8, 0, 255);
}
