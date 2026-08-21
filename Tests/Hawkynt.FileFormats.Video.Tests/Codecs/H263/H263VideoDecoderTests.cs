using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Codecs.H263.Tests;

/// <summary>
/// The H.263 decoder, on streams built here bit by bit.
/// </summary>
/// <remarks>
/// The decoder's arithmetic was checked against ffmpeg over thirty encoded streams, frame by frame
/// and sample by sample; what these tests add is the part that comparison cannot reach. Some of it is
/// syntax ffmpeg's encoders never emit — macroblock stuffing, a DQUANT that has to be clipped, a
/// Sorenson picture that states its size in the bitstream — and some of it is the refusals, which by
/// definition no valid stream produces.
/// <para/>
/// The expected samples are worked out from the Recommendation rather than recorded from a run. Where
/// a number here disagrees with the decoder, one of the two is wrong and the arithmetic in the
/// comment above it says which.
/// </remarks>
[TestFixture]
public sealed class H263VideoDecoderTests {

  /// <summary>Sub-QCIF, the smallest picture ITU-T H.263 has a source format for: eight by six macroblocks.</summary>
  private const int _SUB_QCIF_MACROBLOCKS = 8 * 6;

  // ============================================================================================
  // Intra pictures: the DC value, dequantisation and the transform
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFlatIntraPictureIsMidGrey() {
    // INTRADC 255 stands for a reconstruction level of 1024 (5.4.1), and the transform of a block
    // whose only coefficient is 1024 is 1024/8 = 128 everywhere. A luminance of 128 with neutral
    // chrominance is (298 * (128 - 16) + 128) >> 8 = 130.
    var frame = _Decode(_FlatIntraPicture(255)).Single();

    Assert.That(frame.Width, Is.EqualTo(128));
    Assert.That(frame.Height, Is.EqualTo(96));
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
  }

  [Test]
  [Category("Unit")]
  public void TheIntraDcIsAValueAndNotADifferenceFromTheBlockBefore() {
    // H.263 has no prediction between intra DC coefficients, so a macroblock coded at 64 after one
    // coded at 32 is 64 and not 96. Reading the field as a differential — which is what MPEG-1 does
    // with the same six blocks in the same order — would produce a picture that brightens across
    // every row and looks like a gradient somebody meant.
    var stream = new H263TestStream().PictureHeader(sourceFormat: 1)
      .FlatIntraMacroblock(32)
      .FlatIntraMacroblock(64)
      .FlatIntraMacroblocks(_SUB_QCIF_MACROBLOCKS - 2, 255)
      .ToArray();

    var frame = _Decode(stream).Single();

    Assert.That(_Red(frame, 0, 0), Is.EqualTo(_Grey(32)));
    Assert.That(_Red(frame, 16, 0), Is.EqualTo(_Grey(64)));
  }

  [TestCase(0)]
  [TestCase(128)]
  [Category("Unit")]
  public void AnIntraDcValueTheRecommendationDoesNotUseIsRefused(int intraDc) {
    var stream = new H263TestStream().PictureHeader(sourceFormat: 1)
      .FlatIntraMacroblock(intraDc)
      .FlatIntraMacroblocks(_SUB_QCIF_MACROBLOCKS - 1, 255)
      .ToArray();

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("INTRADC"));
    Assert.That(failure.Message, Does.Contain("5.4.1"));
  }

  [Test]
  [Category("Unit")]
  public void AnAlternatingCurrentCoefficientIsDequantisedAndTransformed() {
    // One coefficient at scan position 1, level 40, QUANT 1. The quantiser is odd, so the
    // reconstruction level is 1 * (2 * 40 + 1) = 81 with nothing subtracted. With the DC at 1024 the
    // transform is 128 + 81/2 * cos((2x+1)pi/16) / (2 sqrt 2), constant down each column, which
    // rounds to the eight luminances below.
    var frame = _Decode(_IntraPictureWithFirstBlockCoefficient(quantiser: 1, level: 40)).Single();

    int[] expected = [142, 140, 136, 131, 125, 120, 116, 114];
    for (var x = 0; x < 8; ++x)
      Assert.That(_Red(frame, x, 0), Is.EqualTo(_Grey(expected[x])), $"column {x}");

    // …and the same down the column, since the coefficient carries no vertical frequency.
    for (var y = 0; y < 8; ++y)
      Assert.That(_Red(frame, 0, y), Is.EqualTo(_Grey(142)), $"row {y}");
  }

  [Test]
  [Category("Unit")]
  public void AnEvenQuantiserPullsEveryReconstructionLevelOneTowardsZero() {
    // The same level 40 at QUANT 4 and at QUANT 5. Four is even, so 4 * 81 = 324 becomes 323; five is
    // odd, so 5 * 81 = 405 stands. Leaving the subtraction out would put the even step's
    // reconstruction points on top of the odd step's below it, which is a contrast that creeps upward
    // through a run of predicted pictures rather than an error visible in one.
    var atFour = _Decode(_IntraPictureWithFirstBlockCoefficient(quantiser: 4, level: 40)).Single();
    var atFive = _Decode(_IntraPictureWithFirstBlockCoefficient(quantiser: 5, level: 40)).Single();

    int[] expectedAtFour = [184, 175, 160, 139, 117, 96, 81, 72];
    int[] expectedAtFive = [198, 188, 168, 142, 114, 88, 68, 58];
    for (var x = 0; x < 8; ++x) {
      Assert.That(_Red(atFour, x, 0), Is.EqualTo(_Grey(expectedAtFour[x])), $"QUANT 4, column {x}");
      Assert.That(_Red(atFive, x, 0), Is.EqualTo(_Grey(expectedAtFive[x])), $"QUANT 5, column {x}");
    }
  }

  [Test]
  [Category("Unit")]
  public void TheStuffingCodewordCarriesNoMacroblock() {
    // Two stuffing codewords in front of the first macroblock. A decoder that treated them as a
    // macroblock would be one short at the end of the picture and would run off the bitstream.
    var stream = new H263TestStream().PictureHeader(sourceFormat: 1)
      .Code(H263TestStream.MacroblockStuffing)
      .Code(H263TestStream.MacroblockStuffing)
      .FlatIntraMacroblocks(_SUB_QCIF_MACROBLOCKS, 255)
      .ToArray();

    Assert.That(_Decode(stream).Single().PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
  }

  [Test]
  [Category("Unit")]
  public void TheQuantiserDifferenceIsClippedRatherThanRefused() {
    // QUANT starts at 1 and DQUANT states -2, which clause 5.3.6 clips back to 1 rather than treating
    // as an error. The coefficient that follows is therefore reconstructed at QUANT 1: level 40 gives
    // 81, the same eight luminances as the picture above.
    var stream = new H263TestStream().PictureHeader(sourceFormat: 1, quantiser: 1)
      .Code(H263TestStream.IntraMacroblockWithQuantiser).Code(H263TestStream.FirstLuminanceCoded)
      .Bits(1, 2)                                          // DQUANT 01: minus two
      .IntraBlock(255).EscapedCoefficient(last: true, run: 0, level: 40)
      .IntraBlock(255).IntraBlock(255).IntraBlock(255)
      .IntraBlock(255).IntraBlock(255)
      .FlatIntraMacroblocks(_SUB_QCIF_MACROBLOCKS - 1, 255)
      .ToArray();

    Assert.That(_Red(_Decode(stream).Single(), 0, 0), Is.EqualTo(_Grey(142)));
  }

  // ============================================================================================
  // Motion compensation
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AWholePixelMotionVectorShiftsThePrediction() {
    // MVD code "0010" is +1 whole pixel, which is two half-pixel units, so the predicted picture is
    // the reference read one pixel along.
    var frames = _Decode(_ShiftedPicture("0010"));

    Assert.That(frames.Count, Is.EqualTo(2));

    int[] reference = [142, 140, 136, 131, 125, 120, 116, 114];
    for (var x = 0; x < 7; ++x)
      Assert.That(_Red(frames[1], x, 0), Is.EqualTo(_Grey(reference[x + 1])), $"column {x}");

    // The eighth sample reads past the ramp into the flat block beside it.
    Assert.That(_Red(frames[1], 7, 0), Is.EqualTo(_Grey(128)));
  }

  [Test]
  [Category("Unit")]
  public void AHalfPixelMotionVectorInterpolatesThePrediction() {
    // MVD code "010" is +0.5 of a pixel: each sample is the mean of the two it sits between, rounded
    // upward, which is the "+1 - RCONTROL" of Figure 13 with RCONTROL fixed at zero in a picture
    // without the extended header.
    var frames = _Decode(_ShiftedPicture("010"));

    int[] reference = [142, 140, 136, 131, 125, 120, 116, 114];
    for (var x = 0; x < 7; ++x) {
      var expected = (reference[x] + reference[x + 1] + 1) >> 1;
      Assert.That(_Red(frames[1], x, 0), Is.EqualTo(_Grey(expected)), $"column {x}");
    }
  }

  [Test]
  [Category("Unit")]
  public void AMacroblockWhoseCodedFlagIsSetIsTheReferenceUnchanged() {
    // Every macroblock of the second picture states COD = 1, so the whole picture is the first one
    // repeated. That is not the same as a coded macroblock with a zero vector and no residual — this
    // one carries no bits at all after the flag.
    var stream = new H263TestStream().PictureHeader(sourceFormat: 1)
      .FlatIntraMacroblock(64)
      .FlatIntraMacroblocks(_SUB_QCIF_MACROBLOCKS - 1, 255)
      .PictureHeader(sourceFormat: 1, isIntra: false, temporalReference: 1)
      .NotCodedMacroblocks(_SUB_QCIF_MACROBLOCKS)
      .ToArray();

    var frames = _Decode(stream);

    Assert.That(frames.Count, Is.EqualTo(2));
    Assert.That(_Red(frames[1], 0, 0), Is.EqualTo(_Grey(64)));
    Assert.That(_Red(frames[1], 16, 0), Is.EqualTo(_Grey(128)));
  }

  [Test]
  [Category("Unit")]
  public void ThePredictorIsTheMedianOfTheThreeNeighbouringVectors() {
    // Macroblock 11 — column 3 of row 1 — has three candidates: the macroblock to its left carries
    // three whole pixels, the one above it carries one, and the one above and to its right carries
    // two. The median is two, so a difference of zero leaves it predicting from the reference read
    // two pixels along.
    //
    // The three are deliberately all different and the median is neither the left one nor the one
    // above: a decoder that took the left candidate would read three pixels along and one that took
    // the one above would read one, and both would pass an assertion that only checked the picture
    // decoded.
    var frames = _Decode(_MedianPredictionStream());

    Assert.That(frames.Count, Is.EqualTo(2));

    // The reference carries the ramp of 142, 140, 136, 131, 125, 120, 116, 114 in that macroblock's
    // first luminance block and 128 everywhere after it, so reading it two pixels along gives the
    // ramp from its third value and then the flat block beside it.
    int[] expected = [136, 131, 125, 120, 116, 114, 128, 128];
    for (var x = 0; x < 8; ++x)
      Assert.That(_Red(frames[1], 48 + x, 16), Is.EqualTo(_Grey(expected[x])), $"column {x}");
  }

  [TestCase(0, 0)]
  [TestCase(1, 1)]
  [TestCase(2, 1)]
  [TestCase(3, 1)]
  [TestCase(4, 2)]
  [TestCase(5, 3)]
  [TestCase(-1, -1)]
  [TestCase(-3, -1)]
  [TestCase(-4, -2)]
  [TestCase(-5, -3)]
  [Category("Unit")]
  public void TheChrominanceVectorRoundsEveryQuarterPixelToTheHalf(int luminance, int expected) {
    // Table 18: halving a half-pixel luminance vector leaves a quarter-pixel chrominance one, and
    // both quarter positions — a quarter and three quarters — become the half of the pixel they are
    // inside. Rounding to the nearest half instead would send three quarters to the next whole pixel,
    // and truncating towards zero would send both to nought.
    Assert.That(H263MotionCompensation.ToChroma(luminance), Is.EqualTo(expected));
  }

  [Test]
  [Category("Unit")]
  public void AVectorThatWouldLeaveThePermittedRangeTakesTheOtherValueOfItsPair() {
    // Every MVD code stands for a pair of differences thirty-two whole pixels apart, and only one of
    // the pair puts the vector inside -16 to 15.5. The first macroblock is coded at +15 whole pixels,
    // which stands. The second predicts from it and states the code whose first value is one whole
    // pixel: +15 plus one is +16, past the +15.5 the range ends at, so the other value of the pair —
    // thirty-one whole pixels the other way — is the one that was meant, and the vector becomes -16.
    //
    // Clamping to the end of the range instead would give +15.5, and ignoring the range would give
    // +16; both are vectors nobody coded, and both read a different part of the reference.
    var frames = _Decode(_WrappedVectorStream());

    Assert.That(frames.Count, Is.EqualTo(2));

    // The second macroblock's first luminance block sits at column 16 and reads sixteen pixels back,
    // which is the reference from column 0: the ramp exactly.
    int[] expected = [142, 140, 136, 131, 125, 120, 116, 114];
    for (var x = 0; x < 8; ++x)
      Assert.That(_Red(frames[1], 16 + x, 0), Is.EqualTo(_Grey(expected[x])), $"column {x}");
  }

  [Test]
  [Category("Unit")]
  public void AVectorPointingOutsideTheReferenceIsRefused() {
    // Baseline H.263 requires every referenced sample to lie inside the coded picture (6.1.1). The
    // first macroblock of the picture is coded with a vector of -1 whole pixel, which reads a column
    // that was never coded.
    var stream = new H263TestStream().PictureHeader(sourceFormat: 1)
      .FlatIntraMacroblocks(_SUB_QCIF_MACROBLOCKS, 255)
      .PictureHeader(sourceFormat: 1, isIntra: false, temporalReference: 1)
      .Coded().Code(H263TestStream.InterMacroblock).Code("11")  // CBPY: no luminance coded, for an inter macroblock
      .Code("0011").Code("1")                                   // MVD -1 horizontally, 0 vertically
      .NotCodedMacroblocks(_SUB_QCIF_MACROBLOCKS - 1)
      .ToArray();

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("outside"));
    Assert.That(failure.Message, Does.Contain("Annex D"));
  }

  [Test]
  [Category("Unit")]
  public void APredictedPictureBeforeAnyIntraPictureIsRefused() {
    var stream = new H263TestStream()
      .PictureHeader(sourceFormat: 1, isIntra: false)
      .NotCodedMacroblocks(_SUB_QCIF_MACROBLOCKS)
      .ToArray();

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("intra picture"));
  }

  // ============================================================================================
  // Group of blocks layer
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AGroupOfBlocksHeaderChangesTheQuantiserForTheRestOfThePicture() {
    // The first row is coded at QUANT 1 and the second group states QUANT 5. The same level of 40
    // therefore reconstructs at 81 in the first row and at 405 in the second.
    var stream = new H263TestStream().PictureHeader(sourceFormat: 1, quantiser: 1)
      .Code(H263TestStream.IntraMacroblock).Code(H263TestStream.FirstLuminanceCoded)
      .IntraBlock(255).EscapedCoefficient(last: true, run: 0, level: 40)
      .IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255)
      .FlatIntraMacroblocks(7, 255)
      .GroupHeader(groupNumber: 1, quantiser: 5)
      .Code(H263TestStream.IntraMacroblock).Code(H263TestStream.FirstLuminanceCoded)
      .IntraBlock(255).EscapedCoefficient(last: true, run: 0, level: 40)
      .IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255)
      .FlatIntraMacroblocks(7, 255)
      .FlatIntraMacroblocks(_SUB_QCIF_MACROBLOCKS - 16, 255)
      .ToArray();

    var frame = _Decode(stream).Single();

    Assert.That(_Red(frame, 0, 0), Is.EqualTo(_Grey(142)));
    Assert.That(_Red(frame, 0, 16), Is.EqualTo(_Grey(198)));
  }

  [Test]
  [Category("Unit")]
  public void AGroupNumberOutOfOrderIsRefused() {
    var stream = new H263TestStream().PictureHeader(sourceFormat: 1, quantiser: 1)
      .FlatIntraMacroblocks(8, 255)
      .GroupHeader(groupNumber: 4, quantiser: 5)
      .FlatIntraMacroblocks(_SUB_QCIF_MACROBLOCKS - 8, 255)
      .ToArray();

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("group number 4"));
  }

  // ============================================================================================
  // Refusals in the picture header
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheExtendedPictureHeaderIsRefusedByName() {
    var failure = Assert.Throws<NotSupportedException>(() => _Decode(_HeaderOnly(sourceFormat: 7)));
    Assert.That(failure.Message, Does.Contain("5.1.4"));
  }

  [TestCase(0)]
  [TestCase(6)]
  [Category("Unit")]
  public void ASourceFormatTheRecommendationDoesNotDefineIsRefused(int sourceFormat) {
    var failure = Assert.Throws<InvalidDataException>(() => _Decode(_HeaderOnly(sourceFormat: sourceFormat)));
    Assert.That(failure.Message, Does.Contain("source format"));
  }

  [Test]
  [Category("Unit")]
  public void UnrestrictedMotionVectorsAreRefusedByAnnex() {
    var stream = new H263TestStream().PictureHeader(sourceFormat: 1, unrestrictedMotionVectors: true).ToArray();
    var failure = Assert.Throws<NotSupportedException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("Annex D"));
  }

  [Test]
  [Category("Unit")]
  public void ArithmeticCodingIsRefusedByAnnex() {
    var stream = new H263TestStream().PictureHeader(sourceFormat: 1, arithmeticCoding: true).ToArray();
    var failure = Assert.Throws<NotSupportedException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("Annex E"));
  }

  [Test]
  [Category("Unit")]
  public void AdvancedPredictionIsRefusedByAnnex() {
    var stream = new H263TestStream().PictureHeader(sourceFormat: 1, advancedPrediction: true).ToArray();
    var failure = Assert.Throws<NotSupportedException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("Annex F"));
  }

  [Test]
  [Category("Unit")]
  public void PbFramesAreRefusedByAnnex() {
    var stream = new H263TestStream().PictureHeader(sourceFormat: 1, pbFrames: true).ToArray();
    var failure = Assert.Throws<NotSupportedException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("Annex G"));
  }

  [Test]
  [Category("Unit")]
  public void ContinuousPresenceMultipointIsRefusedByAnnex() {
    var stream = new H263TestStream().PictureHeader(sourceFormat: 1, continuousPresenceMultipoint: true).ToArray();
    var failure = Assert.Throws<NotSupportedException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("Annex C"));
  }

  [Test]
  [Category("Unit")]
  public void AQuantiserOfZeroIsRefused() {
    var stream = new H263TestStream().PictureHeader(sourceFormat: 1, quantiser: 0).ToArray();
    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("QUANT"));
  }

  [TestCase(0, 0, TestName = "The first PTYPE bit is fixed at one")]
  [TestCase(1, 1, TestName = "The second PTYPE bit is fixed at zero")]
  [Category("Unit")]
  public void APtypeBitTheRecommendationFixesIsRefusedWhenItIsWrong(int first, int second) {
    var stream = new H263TestStream()
      .PictureHeader(sourceFormat: 1, firstPtypeBit: first, secondPtypeBit: second)
      .ToArray();

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("PTYPE"));
  }

  [Test]
  [Category("Unit")]
  public void FourMotionVectorsPerMacroblockAreRefusedByAnnex() {
    var stream = new H263TestStream().PictureHeader(sourceFormat: 1)
      .FlatIntraMacroblocks(_SUB_QCIF_MACROBLOCKS, 255)
      .PictureHeader(sourceFormat: 1, isIntra: false, temporalReference: 1)
      .Coded().Code(H263TestStream.InterMacroblockWithFourVectors)
      .ToArray();

    var failure = Assert.Throws<NotSupportedException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("Annex F"));
  }

  [Test]
  [Category("Unit")]
  public void AnEscapedLevelOfZeroIsRefused() {
    var stream = _IntraPictureWithEscape(last: true, run: 0, level: 0);
    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("level of zero"));
  }

  [Test]
  [Category("Unit")]
  public void AnEscapedLevelReservedForTheModifiedQuantiserIsRefusedByAnnex() {
    var stream = _IntraPictureWithEscape(last: true, run: 0, level: -128);
    var failure = Assert.Throws<NotSupportedException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("Annex T"));
  }

  [Test]
  [Category("Unit")]
  public void ARunPastTheEndOfABlockIsRefused() {
    var stream = _IntraPictureWithEscape(last: true, run: 63, level: 1);
    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("scan position"));
  }

  // ============================================================================================
  // Sorenson Spark
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASorensonPictureStatesItsOwnSizeInOneByte() {
    var stream = new H263TestStream()
      .SorensonPictureHeader(sizeCode: 0, width: 32, height: 16)
      .FlatIntraMacroblocks(2, 255)
      .ToArray();

    var frame = _DecodeSorenson(stream).Single();

    Assert.That(frame.Width, Is.EqualTo(32));
    Assert.That(frame.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void ASorensonPictureStatesItsOwnSizeInTwoBytes() {
    var stream = new H263TestStream()
      .SorensonPictureHeader(sizeCode: 1, width: 300, height: 16)
      .FlatIntraMacroblocks(19, 255)
      .ToArray();

    var frame = _DecodeSorenson(stream).Single();

    Assert.That(frame.Width, Is.EqualTo(300));
    Assert.That(frame.Height, Is.EqualTo(16));
  }

  [TestCase(2, 352, 288)]
  [TestCase(3, 176, 144)]
  [TestCase(4, 128, 96)]
  [TestCase(5, 320, 240)]
  [TestCase(6, 160, 120)]
  [Category("Unit")]
  public void ASorensonSizeCodeNamesAPictureFormat(int sizeCode, int width, int height) {
    var macroblocks = (width + 15) / 16 * ((height + 15) / 16);
    var stream = new H263TestStream()
      .SorensonPictureHeader(sizeCode: sizeCode)
      .FlatIntraMacroblocks(macroblocks, 255)
      .ToArray();

    var frame = _DecodeSorenson(stream).Single();

    Assert.That(frame.Width, Is.EqualTo(width));
    Assert.That(frame.Height, Is.EqualTo(height));
  }

  [Test]
  [Category("Unit")]
  public void ASorensonDisposablePictureIsShownAndNotKeptAsAReference() {
    // Picture type 2 is a predicted picture nothing predicts from. The third picture here therefore
    // predicts from the first and not from the second, so its uncoded macroblocks reconstruct at the
    // first picture's value and not at the second's.
    var stream = new H263TestStream()
      .SorensonPictureHeader(sizeCode: 0, width: 16, height: 16)
      .FlatIntraMacroblock(64)
      .SorensonPictureHeader(sizeCode: 0, width: 16, height: 16, pictureType: 2, temporalReference: 1)
      .Coded().Code(H263TestStream.IntraMacroblockInPredictedPicture).Code(H263TestStream.NoLuminanceCoded)
      .IntraBlock(200).IntraBlock(200).IntraBlock(200).IntraBlock(200).IntraBlock(255).IntraBlock(255)
      .SorensonPictureHeader(sizeCode: 0, width: 16, height: 16, pictureType: 1, temporalReference: 2)
      .NotCodedMacroblocks(1)
      .ToArray();

    var frames = _DecodeSorenson(stream);

    Assert.That(frames.Count, Is.EqualTo(3));
    Assert.That(_Red(frames[0], 0, 0), Is.EqualTo(_Grey(64)));
    Assert.That(_Red(frames[1], 0, 0), Is.EqualTo(_Grey(200)));
    Assert.That(_Red(frames[2], 0, 0), Is.EqualTo(_Grey(64)));
  }

  [TestCase(7)]
  [TestCase(11)]
  [Category("Unit")]
  public void TheSorensonEscapeCarriesALevelOfSevenOrElevenBits(int levelBits) {
    // Version 1 puts a bit in front of the escape's last flag, choosing the width of the level. Both
    // widths are used here for the same level of 40, which reconstructs at QUANT 1 to 81 either way.
    var stream = new H263TestStream()
      .SorensonPictureHeader(version: 1, sizeCode: 0, width: 16, height: 16, quantiser: 1)
      .Code(H263TestStream.IntraMacroblock).Code(H263TestStream.FirstLuminanceCoded)
      .IntraBlock(255).SorensonEscapedCoefficient(last: true, run: 0, level: 40, levelBits: levelBits)
      .IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255)
      .ToArray();

    Assert.That(_Red(_DecodeSorenson(stream).Single(), 0, 0), Is.EqualTo(_Grey(142)));
  }

  [Test]
  [Category("Unit")]
  public void ASorensonVectorMayReachOutsideThePictureAndReadsTheEdgeSample() {
    // Sorenson has unrestricted motion vectors always on, so a vector of -1 whole pixel at the left
    // edge reads the edge column repeated rather than being refused as it would be in a baseline
    // H.263 picture. The reference is flat, so what is checked is that the picture decodes at all —
    // the refusal it would otherwise get is the point.
    var stream = new H263TestStream()
      .SorensonPictureHeader(sizeCode: 0, width: 16, height: 16)
      .FlatIntraMacroblock(64)
      .SorensonPictureHeader(sizeCode: 0, width: 16, height: 16, pictureType: 1, temporalReference: 1)
      .Coded().Code(H263TestStream.InterMacroblock).Code("11")
      .Code("0011").Code("1")                                   // MVD -1 horizontally, 0 vertically
      .ToArray();

    var frames = _DecodeSorenson(stream);

    Assert.That(frames.Count, Is.EqualTo(2));
    Assert.That(_Red(frames[1], 0, 0), Is.EqualTo(_Grey(64)));
  }

  [Test]
  [Category("Unit")]
  public void ASorensonVersionTheFormatDoesNotDefineIsRefused() {
    var stream = new H263TestStream().SorensonPictureHeader(version: 2).ToArray();
    var failure = Assert.Throws<NotSupportedException>(() => _DecodeSorenson(stream));
    Assert.That(failure.Message, Does.Contain("version 2"));
  }

  [Test]
  [Category("Unit")]
  public void ASorensonReservedPictureSizeCodeIsRefused() {
    var stream = new H263TestStream().SorensonPictureHeader(sizeCode: 7).ToArray();
    var failure = Assert.Throws<InvalidDataException>(() => _DecodeSorenson(stream));
    Assert.That(failure.Message, Does.Contain("reserved"));
  }

  [Test]
  [Category("Unit")]
  public void ASorensonReservedPictureTypeIsRefused() {
    var stream = new H263TestStream().SorensonPictureHeader(sizeCode: 0, pictureType: 3).ToArray();
    var failure = Assert.Throws<InvalidDataException>(() => _DecodeSorenson(stream));
    Assert.That(failure.Message, Does.Contain("picture type 3"));
  }

  [Test]
  [Category("Unit")]
  public void ASorensonPictureOfZeroSizeIsRefused() {
    var stream = new H263TestStream().SorensonPictureHeader(sizeCode: 0, width: 0, height: 16).ToArray();
    var failure = Assert.Throws<InvalidDataException>(() => _DecodeSorenson(stream));
    Assert.That(failure.Message, Does.Contain("0x16"));
  }

  [Test]
  [Category("Unit")]
  public void ASorensonStreamThatChangesPictureSizeIsRefused() {
    var stream = new H263TestStream()
      .SorensonPictureHeader(sizeCode: 0, width: 16, height: 16)
      .FlatIntraMacroblock(64)
      .SorensonPictureHeader(sizeCode: 0, width: 32, height: 16, pictureType: 1, temporalReference: 1)
      .NotCodedMacroblocks(2)
      .ToArray();

    var failure = Assert.Throws<NotSupportedException>(() => _DecodeSorenson(stream));
    Assert.That(failure.Message, Does.Contain("changes picture size"));
  }

  // ============================================================================================
  // Identity
  // ============================================================================================

  [TestCase("H263", true)]
  [TestCase("h263", true)]
  [TestCase("s263", true)]
  [TestCase("FLV1", true)]
  [TestCase("MPG1", false)]
  [TestCase("mp4v", false)]
  [Category("Unit")]
  public void TheCodecTakesTheStreamsItsContainersName(string tag, bool expected)
    => Assert.That(H263VideoDecoder.Accepts(_Stream(tag)), Is.EqualTo(expected));

  [Test]
  [Category("Unit")]
  public void AnAudioStreamIsNotTakenWhateverItsTag()
    => Assert.That(
      H263VideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("H263") }),
      Is.False);

  // ============================================================================================
  // Streams
  // ============================================================================================

  private static byte[] _HeaderOnly(int sourceFormat)
    => new H263TestStream().PictureHeader(sourceFormat: sourceFormat).ToArray();

  private static byte[] _FlatIntraPicture(int intraDc)
    => new H263TestStream().PictureHeader(sourceFormat: 1)
      .FlatIntraMacroblocks(_SUB_QCIF_MACROBLOCKS, intraDc)
      .ToArray();

  /// <summary>A sub-QCIF intra picture whose very first luminance block carries one coefficient.</summary>
  private static byte[] _IntraPictureWithFirstBlockCoefficient(int quantiser, int level)
    => new H263TestStream().PictureHeader(sourceFormat: 1, quantiser: quantiser)
      .Code(H263TestStream.IntraMacroblock).Code(H263TestStream.FirstLuminanceCoded)
      .IntraBlock(255).EscapedCoefficient(last: true, run: 0, level: level)
      .IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255)
      .FlatIntraMacroblocks(_SUB_QCIF_MACROBLOCKS - 1, 255)
      .ToArray();

  private static byte[] _IntraPictureWithEscape(bool last, int run, int level)
    => new H263TestStream().PictureHeader(sourceFormat: 1)
      .Code(H263TestStream.IntraMacroblock).Code(H263TestStream.FirstLuminanceCoded)
      .IntraBlock(255).EscapedCoefficient(last, run, level)
      .IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255)
      .FlatIntraMacroblocks(_SUB_QCIF_MACROBLOCKS - 1, 255)
      .ToArray();

  /// <summary>
  /// An intra picture whose first block carries a horizontal ramp, and a predicted picture that
  /// displaces its first macroblock by the given motion vector difference.
  /// </summary>
  private static byte[] _ShiftedPicture(string motionCode)
    => new H263TestStream().PictureHeader(sourceFormat: 1, quantiser: 1)
      .Code(H263TestStream.IntraMacroblock).Code(H263TestStream.FirstLuminanceCoded)
      .IntraBlock(255).EscapedCoefficient(last: true, run: 0, level: 40)
      .IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255)
      .FlatIntraMacroblocks(_SUB_QCIF_MACROBLOCKS - 1, 255)
      .PictureHeader(sourceFormat: 1, isIntra: false, temporalReference: 1)
      .Coded().Code(H263TestStream.InterMacroblock).Code("11")
      .Code(motionCode).Code("1")
      .NotCodedMacroblocks(_SUB_QCIF_MACROBLOCKS - 1)
      .ToArray();

  /// <summary>
  /// An intra picture carrying a ramp in one macroblock, and a predicted picture in which the three
  /// macroblocks that macroblock's predictor is the median of carry three different vectors.
  /// </summary>
  /// <remarks>
  /// The macroblocks between the coded ones are not coded, which sets their vectors to zero (6.1.1) —
  /// that is what keeps the three candidates to the three that were coded, and what makes the
  /// arithmetic of the predictor readable from the stream.
  /// </remarks>
  private static byte[] _MedianPredictionStream() {
    // Macroblock 11 is column 3 of row 1, so its first luminance block is at (48, 16).
    var stream = _RampAt(11)
      .PictureHeader(sourceFormat: 1, isIntra: false, temporalReference: 1);

    // Row 0. Macroblock 3 is the "above" candidate and macroblock 4 the "above right" one. Every
    // macroblock of the top row predicts from the one to its left, so macroblock 3's predictor is
    // zero and macroblock 4's is macroblock 3's vector.
    stream.NotCodedMacroblocks(3);
    _InterMacroblockWithVector(stream, "0010");        // 0 + 2 half-pixels: one whole pixel
    _InterMacroblockWithVector(stream, "0010");        // 2 + 2: two whole pixels
    stream.NotCodedMacroblocks(3);

    // Row 1. Macroblock 10 is the "left" candidate: its own predictor is the median of zero (left),
    // zero (macroblock 2, not coded) and two (macroblock 3), which is zero.
    stream.NotCodedMacroblocks(2);
    _InterMacroblockWithVector(stream, "0000 1000");   // 0 + 6 half-pixels: three whole pixels
    _InterMacroblockWithVector(stream, "1");           // difference of zero: the vector is the median
    stream.NotCodedMacroblocks(4);

    stream.NotCodedMacroblocks(_SUB_QCIF_MACROBLOCKS - 16);
    return stream.ToArray();
  }

  private static byte[] _WrappedVectorStream() {
    var stream = _RampAt(0)
      .PictureHeader(sourceFormat: 1, isIntra: false, temporalReference: 1);

    // Macroblock 0 sits at the left edge and at the top, so all three candidates are zero.
    // "0000 0000 0100" is +30 half-pixels, which is +15 whole pixels and inside the range.
    _InterMacroblockWithVector(stream, "0000 0000 0100");

    // Macroblock 1 predicts from it — the top row takes the left candidate for all three — and states
    // the code whose first value is +2 half-pixels. Thirty plus two is thirty-two, past the
    // thirty-one that +15.5 pixels is, so the pair's other value applies and the vector is -32.
    _InterMacroblockWithVector(stream, "0010");
    stream.NotCodedMacroblocks(_SUB_QCIF_MACROBLOCKS - 2);
    return stream.ToArray();
  }

  /// <summary>
  /// An intra picture that is flat everywhere but the first luminance block of one macroblock, which
  /// carries the ramp of 142, 140, 136, 131, 125, 120, 116, 114.
  /// </summary>
  private static H263TestStream _RampAt(int macroblock)
    => new H263TestStream().PictureHeader(sourceFormat: 1, quantiser: 1)
      .FlatIntraMacroblocks(macroblock, 255)
      .Code(H263TestStream.IntraMacroblock).Code(H263TestStream.FirstLuminanceCoded)
      .IntraBlock(255).EscapedCoefficient(last: true, run: 0, level: 40)
      .IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255)
      .FlatIntraMacroblocks(_SUB_QCIF_MACROBLOCKS - macroblock - 1, 255);

  /// <summary>One coded inter macroblock with no coefficients and the given horizontal vector.</summary>
  private static void _InterMacroblockWithVector(H263TestStream stream, string horizontal)
    => stream.Coded().Code(H263TestStream.InterMacroblock).Code("11").Code(horizontal).Code("1");

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Stream(string tag)
    => new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters(tag) };

  private static List<RawImage> _Decode(byte[] stream) => _Decode(stream, "H263", 0xFC);

  private static List<RawImage> _DecodeSorenson(byte[] stream) => _Decode(stream, "FLV1", 0x80);

  private static List<RawImage> _Decode(byte[] stream, string tag, int startMask) {
    var decoder = H263VideoDecoder.Create(_Stream(tag));
    var frames = new List<RawImage>();

    foreach (var picture in _Pictures(stream, startMask))
      if (decoder.TryDecode(new(0, picture), out var frame))
        frames.Add(frame);

    frames.AddRange(decoder.Flush());
    return frames;
  }

  /// <summary>
  /// Cuts the built stream at its picture start codes, the way a container hands out packets.
  /// </summary>
  /// <remarks>
  /// The mask is the caller's because the two bitstreams put different things in the five bits after
  /// the seventeen-bit start code. An ITU-T picture puts a group number of zero there, so the five
  /// bits have to be tested or a group of blocks header would be taken for the start of the next
  /// picture and the picture cut in half. A Sorenson picture puts its version there instead, so
  /// testing them would leave a version this decoder is meant to refuse unfound.
  /// </remarks>
  private static IEnumerable<byte[]> _Pictures(byte[] stream, int mask) {
    var starts = new List<int>();
    for (var offset = 0; offset + 3 <= stream.Length; ++offset)
      if (stream[offset] == 0 && stream[offset + 1] == 0 && (stream[offset + 2] & mask) == 0x80)
        starts.Add(offset);

    if (starts.Count == 0) {
      yield return stream;
      yield break;
    }

    for (var index = 0; index < starts.Count; ++index) {
      var from = starts[index];
      var to = index + 1 < starts.Count ? starts[index + 1] : stream.Length;
      yield return stream[from..to];
    }
  }

  private static byte _Red(RawImage image, int x, int y) => image.PixelData[(y * image.Width + x) * 3];

  /// <summary>
  /// The red — and, with neutral chrominance, also green and blue — a luminance converts to.
  /// </summary>
  /// <remarks>
  /// H.263's samples are ITU-R BT.601 with studio swing, so a luminance of 16 is black and 235 is
  /// white: <c>(298 * (Y - 16) + 128) &gt;&gt; 8</c>. Stated here so the expectations above can be
  /// written as the luminances the Recommendation's arithmetic produces rather than as a table of
  /// converted numbers whose derivation would be invisible.
  /// </remarks>
  private static byte _Grey(int luminance) => (byte)Math.Clamp((298 * (luminance - 16) + 128) >> 8, 0, 255);
}
