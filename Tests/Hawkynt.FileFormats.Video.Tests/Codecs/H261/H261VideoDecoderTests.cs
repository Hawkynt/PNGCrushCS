using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Codecs.H261.Tests;

/// <summary>
/// The H.261 decoder, on streams built here bit by bit.
/// </summary>
/// <remarks>
/// The decoder's arithmetic was checked against ffmpeg over two encoded streams — a QCIF and a CIF
/// clip, sixty frames each, one intra picture anchoring the whole chain — compared plane by plane
/// against ffmpeg's own decode. What these tests add is what that comparison could not reach: ffmpeg's
/// H.261 encoder was measured never to emit the loop filter, a mid-group MQUANT or the bit-stuffing
/// codeword, so those three are built by hand here, along with every refusal. The macroblock-address
/// gap ("skip") mechanism and motion-compensated prediction, by contrast, are exercised thousands of
/// times over in that real corpus — 1883 of 5232 macroblock addresses in the QCIF stream and 1642 of
/// 23523 in the CIF one are gaps, matched exactly against ffmpeg's own reference handling — so the
/// tests below check the sharpest single case of each (the loop filter's rounding, and a gap
/// immediately after a motion-compensated macroblock) rather than re-deriving what the corpus already
/// measured at scale.
/// </remarks>
[TestFixture]
public sealed class H261VideoDecoderTests {

  // ============================================================================================
  // Intra pictures: the DC value and dequantisation
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFlatIntraPictureIsMidGrey() {
    // INTRADC 255 stands for reconstruction level 1024 (4.2.4.2), and 1024/8 = 128 everywhere.
    var frame = _Decode(H261TestStream.FlatQcifIntraPicture(255)).Single();

    Assert.That(frame.Width, Is.EqualTo(176));
    Assert.That(frame.Height, Is.EqualTo(144));
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
  }

  [Test]
  [Category("Unit")]
  public void TheIntraDcIsAValueAndNotADifferenceFromTheBlockBefore() {
    var stream = new H261TestStream().PictureHeader();
    stream.GroupHeader(1, 1);
    stream.FlatIntraMacroblock(1, 32);
    stream.FlatIntraMacroblock(1, 64);
    for (var address = 3; address <= 33; ++address)
      stream.FlatIntraMacroblock(1, 255);
    stream.FlatIntraGroup(3, 1, 255);
    stream.FlatIntraGroup(5, 1, 255);

    var frame = _Decode(stream.ToArray()).Single();

    Assert.That(_Red(frame, 0, 0), Is.EqualTo(_Grey(32)));
    Assert.That(_Red(frame, 16, 0), Is.EqualTo(_Grey(64)));
  }

  [TestCase(0)]
  [TestCase(128)]
  [Category("Unit")]
  public void AnIntraDcValueTheRecommendationDoesNotUseIsRefused(int dc) {
    var stream = new H261TestStream().PictureHeader();
    stream.GroupHeader(1, 1);
    stream.MacroblockAddress(1).Code(H261TestStream.TypeIntra).Bits(dc, 8);

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream.ToArray()));
    Assert.That(failure.Message, Does.Contain("4.2.4.2"));
  }

  [Test]
  [Category("Unit")]
  public void AnAlternatingCurrentCoefficientIsDequantisedAndTransformed() {
    // The same arithmetic H.263's equivalent test uses, because H261BlockDecoder reuses
    // H263Quantisation and H263InverseDct unchanged: one coefficient at scan position 1, level 40,
    // QUANT 1 gives the ramp 142, 140, 136, 131, 125, 120, 116, 114 across the block's columns.
    var frame = _Decode(_RampPicture()).Single();

    int[] expected = [142, 140, 136, 131, 125, 120, 116, 114];
    for (var x = 0; x < 8; ++x)
      Assert.That(_Red(frame, x, 0), Is.EqualTo(_Grey(expected[x])), $"column {x}");
  }

  // ============================================================================================
  // Macroblock addressing — ITU-T H.261, 4.2.3.1
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheStuffingCodewordCarriesNoMacroblock() {
    var stream = new H261TestStream().PictureHeader();
    stream.GroupHeader(1, 1);
    stream.Code(H261TestStream.MbaStuffing).Code(H261TestStream.MbaStuffing);
    for (var address = 1; address <= 33; ++address)
      stream.FlatIntraMacroblock(1, 255);
    stream.FlatIntraGroup(3, 1, 255);
    stream.FlatIntraGroup(5, 1, 255);

    Assert.That(_Decode(stream.ToArray()).Single().PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
  }

  [Test]
  [Category("Unit")]
  public void AMacroblockAddressGapLeavesTheSkippedOnesAtTheReferencesValue() {
    // The second picture codes only macroblock 1 of group 1 — as Inter+MC with a zero vector and no
    // residual — and transmits nothing at all for every other macroblock of the picture, which clause
    // 4.2.3.1 says means they carry no information, not that they are coded with a zero residual: the
    // canvas this decoder reconstructs into already holds the reference's samples everywhere before a
    // single macroblock of the new picture is read.
    var first = new H261TestStream().PictureHeader();
    first.GroupHeader(1, 1);
    first.FlatIntraMacroblock(1, 64);
    for (var address = 2; address <= 33; ++address)
      first.FlatIntraMacroblock(1, 255);
    first.FlatIntraGroup(3, 1, 255);
    first.FlatIntraGroup(5, 1, 255);

    var second = new H261TestStream().PictureHeader(temporalReference: 1);
    second.GroupHeader(1, 1);
    second.MacroblockAddress(1).Code(H261TestStream.TypeInterMc)
      .MotionVectorComponent(0).MotionVectorComponent(0);
    second.GroupHeader(3, 1);
    second.GroupHeader(5, 1);

    var frames = _Decode(first.ToArray(), second.ToArray());

    Assert.That(frames.Count, Is.EqualTo(2));
    // Macroblock 2 (address 2, at pixel column 16) was never transmitted in the second picture, so it
    // keeps the first picture's value of 255 (grey 130) rather than becoming macroblock 1's 64.
    // DC field 255 stands for reconstruction level 1024, luminance 128 (4.2.4.2) — not the DC field
    // value itself, which is what makes this the value worth asserting rather than a coincidence.
    Assert.That(_Red(frames[1], 16, 0), Is.EqualTo(_Grey(128)));
    Assert.That(_Red(frames[1], 0, 0), Is.EqualTo(_Grey(64)));
  }

  [Test]
  [Category("Unit")]
  public void AContiguousMotionCompensatedMacroblockInheritsThePreviousVectorAsItsPredictor() {
    // Macroblock 1 (a group's row start, so its own predictor is zero regardless — rule 1) is coded
    // Inter+MC at +2 whole pixels. Macroblock 2 carries a ramp in its own reference position and is
    // coded Inter+MC with MVD 0: contiguous with macroblock 1 and macroblock 1 was MC, so rule 3 does
    // not reset it and it should inherit +2, reading the ramp two pixels in rather than at its start.
    var first = new H261TestStream().PictureHeader();
    first.GroupHeader(1, 1);
    first.FlatIntraMacroblock(1, 255); // macroblock 1: flat
    first.MacroblockAddress(1).Code(H261TestStream.TypeIntra); // macroblock 2: the ramp
    first.Bits(255, 8).EscapedCoefficient(0, 40).EndOfBlock();
    first.IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255);
    for (var address = 3; address <= 33; ++address)
      first.FlatIntraMacroblock(1, 255);
    first.FlatIntraGroup(3, 1, 255);
    first.FlatIntraGroup(5, 1, 255);

    var second = new H261TestStream().PictureHeader(temporalReference: 1);
    second.GroupHeader(1, 1);
    second.MacroblockAddress(1).Code(H261TestStream.TypeInterMc)
      .MotionVectorComponent(2).MotionVectorComponent(0);
    second.MacroblockAddress(1).Code(H261TestStream.TypeInterMc)
      .MotionVectorComponent(0).MotionVectorComponent(0);
    second.GroupHeader(3, 1);
    second.GroupHeader(5, 1);

    var frames = _Decode(first.ToArray(), second.ToArray());

    // Macroblock 2's own area starts at pixel column 16; inheriting +2 reads the reference ramp from
    // column 18, which is two positions into it: 136 rather than the ramp's own first value, 142.
    Assert.That(_Red(frames[1], 16, 0), Is.EqualTo(_Grey(136)));
  }

  [Test]
  [Category("Unit")]
  public void AGroupOfBlocksHeaderIsMandatoryEvenWithNoMacroblocks() {
    // ITU-T H.261 4.2.2, unlike H.263's optional group headers: a group carrying nothing still states
    // GN and GQUANT. The first picture of a stream needs every macroblock coded, so an empty group is
    // only reachable on a predicted picture.
    var first = new H261TestStream().PictureHeader();
    first.GroupHeader(1, 1);
    for (var address = 1; address <= 33; ++address)
      first.FlatIntraMacroblock(1, 64);
    first.FlatIntraGroup(3, 1, 255);
    first.FlatIntraGroup(5, 1, 255);

    var second = new H261TestStream().PictureHeader(temporalReference: 1);
    second.GroupHeader(1, 1);   // no macroblocks at all in this group
    second.GroupHeader(3, 1);
    second.GroupHeader(5, 1);

    var frames = _Decode(first.ToArray(), second.ToArray());

    Assert.That(frames.Count, Is.EqualTo(2));
    Assert.That(frames[1].PixelData.Distinct().ToArray(), Is.EqualTo(frames[0].PixelData.Distinct().ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void AGroupNumberOutOfOrderIsRefused() {
    var stream = new H261TestStream().PictureHeader();
    stream.GroupHeader(1, 1);
    for (var address = 1; address <= 33; ++address)
      stream.FlatIntraMacroblock(1, 255);
    stream.GroupHeader(4, 1); // 3 was due

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream.ToArray()));
    Assert.That(failure.Message, Does.Contain("Group of blocks 4"));
  }

  [Test]
  [Category("Unit")]
  public void AReservedGroupNumberIsRefused() {
    var stream = new H261TestStream().PictureHeader();
    stream.GroupHeader(13, 1);

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream.ToArray()));
    Assert.That(failure.Message, Does.Contain("reserved"));
  }

  [Test]
  [Category("Unit")]
  public void AGroupQuantiserOfZeroIsRefused() {
    var stream = new H261TestStream().PictureHeader();
    stream.GroupHeader(1, 0);

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream.ToArray()));
    Assert.That(failure.Message, Does.Contain("GQUANT"));
  }

  [Test]
  [Category("Unit")]
  public void AMidGroupQuantiserChangeAppliesToTheMacroblocksAfterIt() {
    // Ffmpeg's encoder never restates the quantiser inside a group, so this is reached only by hand.
    // The first macroblock codes a coefficient at QUANT 1; the second states MQUANT 5 and recodes the
    // same coefficient, which reconstructs at a different level.
    var stream = new H261TestStream().PictureHeader();
    stream.GroupHeader(1, 1);
    stream.MacroblockAddress(1).Code(H261TestStream.TypeIntra);
    stream.IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255);
    stream.MacroblockAddress(1).Code(H261TestStream.TypeIntraQuant).Bits(5, 5);
    stream.Bits(255, 8).EscapedCoefficient(0, 40).EndOfBlock();
    stream.IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255);
    for (var address = 3; address <= 33; ++address)
      stream.FlatIntraMacroblock(1, 255);
    stream.FlatIntraGroup(3, 1, 255);
    stream.FlatIntraGroup(5, 1, 255);

    var frame = _Decode(stream.ToArray()).Single();

    // Level 40 at QUANT 5 (odd): REC = 5*(2*40+1) = 405. Same arithmetic as H.263's equivalent test.
    int[] expected = [198, 188, 168, 142, 114, 88, 68, 58];
    for (var x = 0; x < 8; ++x)
      Assert.That(_Red(frame, 16 + x, 0), Is.EqualTo(_Grey(expected[x])), $"column {x}");
  }

  // ============================================================================================
  // Motion vectors — ITU-T H.261, 3.2.2 and 4.2.3.4
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AWholePixelMotionVectorShiftsThePrediction() {
    var frames = _Decode(_RampPicture(), _ShiftedPicture(mvdValue: 2));

    int[] reference = [142, 140, 136, 131, 125, 120, 116, 114];
    for (var x = 0; x < 6; ++x)
      Assert.That(_Red(frames[1], x, 0), Is.EqualTo(_Grey(reference[x + 2])), $"column {x}");
  }

  [Test]
  [Category("Unit")]
  public void AVectorThatWouldLeaveThePermittedRangeTakesTheOtherValueOfItsPair() {
    // Macroblock 1 (row start, predictor zero) is coded Inter+MC at +15 whole pixels — the largest
    // ITU-T H.261 3.2.2 allows. Macroblock 2 is contiguous and also Inter+MC, so it inherits +15 as
    // its own predictor, and states MVD +2: the raw sum is +17, past +15, so the pair's other value
    // applies and the reconstructed vector is +17 - 32 = -15, not the +17 nobody could have coded.
    //
    // Macroblock 2's own area begins at pixel column 16, so a vector of -15 reads the reference from
    // column 1 — one pixel into the ramp this picture carries at columns 0 to 7 — rather than from
    // column 31, which +17 would have read and which clamping to +15 (column 31) would also have read
    // wrongly, and both would be vectors nobody transmitted.
    var second = new H261TestStream().PictureHeader(temporalReference: 1);
    second.GroupHeader(1, 1);
    second.MacroblockAddress(1).Code(H261TestStream.TypeInterMc)
      .MotionVectorComponent(15).MotionVectorComponent(0);
    second.MacroblockAddress(1).Code(H261TestStream.TypeInterMc)
      .MotionVectorComponent(2).MotionVectorComponent(0);
    second.GroupHeader(3, 1);
    second.GroupHeader(5, 1);

    var frames = _Decode(_RampPicture(), second.ToArray());

    Assert.That(frames.Count, Is.EqualTo(2));
    int[] expected = [140, 136, 131, 125, 120, 116, 114, 128];
    for (var x = 0; x < 8; ++x)
      Assert.That(_Red(frames[1], 16 + x, 0), Is.EqualTo(_Grey(expected[x])), $"column {x}");
  }

  [Test]
  [Category("Unit")]
  public void AVectorPointingOutsideTheReferenceIsRefused() {
    // -1 whole pixel at the top-left macroblock reads a column that was never coded.
    var failure = Assert.Throws<InvalidDataException>(() => _Decode(_RampPicture(), _ShiftedPicture(mvdValue: -1)));
    Assert.That(failure.Message, Does.Contain("outside"));
    Assert.That(failure.Message, Does.Contain("3.2.2"));
  }

  // ============================================================================================
  // The loop filter — ITU-T H.261, 3.2.3
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheLoopFilterRunsOnThePredictionBeforeTheResidualIsAdded() {
    // The reference's first luminance block carries the ramp 142, 140, 136, 131, 125, 120, 116, 114
    // down every column. The second picture codes that macroblock Inter+MC+FIL with a zero vector and
    // no residual — Table 2's second note explicitly allows FIL without motion — so what comes out is
    // the filter's own answer for that ramp and nothing added to it.
    //
    // Filtering is column-invariant here because the ramp carries no vertical frequency at all, so the
    // two-dimensional filter reduces to one-dimensional horizontal averaging: edges (x = 0, x = 7) pass
    // through unfiltered, and every interior column is the mean of itself and its two neighbours,
    // rounded up on a fractional half. Column 6 is the one place that half occurs — (120 + 2*116 +
    // 114)/4 = 116.5 — which clause 3.2.3 says rounds up to 117 and not down to 116.
    var second = new H261TestStream().PictureHeader(temporalReference: 1);
    second.GroupHeader(1, 1);
    second.MacroblockAddress(1).Code(H261TestStream.TypeInterMcFil)
      .MotionVectorComponent(0).MotionVectorComponent(0);
    second.GroupHeader(3, 1);
    second.GroupHeader(5, 1);

    var frames = _Decode(_RampPicture(), second.ToArray());

    Assert.That(frames.Count, Is.EqualTo(2));

    int[] filtered = [142, 140, 136, 131, 125, 120, 117, 114];
    for (var x = 0; x < 8; ++x)
      Assert.That(_Red(frames[1], x, 0), Is.EqualTo(_Grey(filtered[x])), $"column {x}");

    // Every row repeats the same ramp, so the filtered result does too — the vertical pass of a
    // column-constant block is a no-op by construction (three equal taps average back to themselves).
    for (var y = 1; y < 8; ++y)
      Assert.That(_Red(frames[1], 6, y), Is.EqualTo(_Grey(117)), $"row {y}, column 6");
  }

  [Test]
  [Category("Unit")]
  public void TheLoopFilterAppliesBeforeAResidualIsThenAddedOnTopOfIt() {
    // The same ramp macroblock, this time Inter+MC+FIL with a residual on top of the filtered
    // prediction: a single coefficient at scan position 0 (run 0, level 8) dequantises at QUANT 1 to
    // 1*(2*8+1) = 17 and, being the only coefficient, transforms to a flat 17/8 rounded — 2 — added to
    // every sample. The filter-only test above already isolates the ordering at the block's one
    // rounding boundary; this one confirms a coded block still adds its residual on top of the
    // filtered result rather than the filter running a second time or being skipped once CBP is set.
    var second = new H261TestStream().PictureHeader(temporalReference: 1);
    second.GroupHeader(1, 1);
    second.MacroblockAddress(1).Code(H261TestStream.TypeInterMcFilCoded)
      .MotionVectorComponent(0).MotionVectorComponent(0)
      .CodedBlockPattern(0b10_0000);
    second.FirstCoefficient(0, 8).EndOfBlock();
    second.GroupHeader(3, 1);
    second.GroupHeader(5, 1);

    var frames = _Decode(_RampPicture(), second.ToArray());

    Assert.That(_Red(frames[1], 0, 0), Is.EqualTo(_Grey(142 + 2)));
    Assert.That(_Red(frames[1], 7, 0), Is.EqualTo(_Grey(114 + 2)));
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void StillImageTransmissionIsRefusedByAnnex() {
    var stream = new H261TestStream().PictureHeader(requestStillImage: true).ToArray();

    var failure = Assert.Throws<NotSupportedException>(() => _Decode(stream));
    Assert.That(failure.Message, Does.Contain("Annex D"));
  }

  [Test]
  [Category("Unit")]
  public void APredictedMacroblockBeforeAnyReferenceIsRefused() {
    var stream = new H261TestStream().PictureHeader();
    stream.GroupHeader(1, 1);
    stream.MacroblockAddress(1).Code(H261TestStream.TypeInterMc)
      .MotionVectorComponent(0).MotionVectorComponent(0);

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream.ToArray()));
    Assert.That(failure.Message, Does.Contain("no reference"));
  }

  [Test]
  [Category("Unit")]
  public void ASkippedMacroblockOnTheFirstPictureIsRefused() {
    var stream = new H261TestStream().PictureHeader();
    stream.GroupHeader(1, 1);
    stream.MacroblockAddress(2).Code(H261TestStream.TypeIntra); // starts at 2, not 1: a gap with nothing to copy
    stream.IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255);

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream.ToArray()));
    Assert.That(failure.Message, Does.Contain("nothing to copy"));
  }

  [Test]
  [Category("Unit")]
  public void AGroupTrailingOffBeforeThirtyThreeOnTheFirstPictureIsRefused() {
    var stream = new H261TestStream().PictureHeader();
    stream.GroupHeader(1, 1);
    stream.FlatIntraMacroblock(1, 255); // only macroblock 1, then straight to the next group
    stream.GroupHeader(3, 1);

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream.ToArray()));
    Assert.That(failure.Message, Does.Contain("leaving the rest"));
  }

  [Test]
  [Category("Unit")]
  public void AnEscapedLevelOfZeroIsRefused() {
    var stream = new H261TestStream().PictureHeader();
    stream.GroupHeader(1, 1);
    stream.MacroblockAddress(1).Code(H261TestStream.TypeIntra);
    stream.Bits(255, 8).EscapedCoefficient(0, 0);

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream.ToArray()));
    Assert.That(failure.Message, Does.Contain("zero"));
  }

  [Test]
  [Category("Unit")]
  public void AnEscapedLevelOfMinusOneHundredTwentyEightIsRefused() {
    var stream = new H261TestStream().PictureHeader();
    stream.GroupHeader(1, 1);
    stream.MacroblockAddress(1).Code(H261TestStream.TypeIntra);
    stream.Bits(255, 8).EscapedCoefficient(0, -128);

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream.ToArray()));
    Assert.That(failure.Message, Does.Contain("FORBIDDEN"));
  }

  [Test]
  [Category("Unit")]
  public void ARunPastTheEndOfABlockIsRefused() {
    var stream = new H261TestStream().PictureHeader();
    stream.GroupHeader(1, 1);
    stream.MacroblockAddress(1).Code(H261TestStream.TypeIntra);
    stream.Bits(255, 8).EscapedCoefficient(63, 1);

    var failure = Assert.Throws<InvalidDataException>(() => _Decode(stream.ToArray()));
    Assert.That(failure.Message, Does.Contain("scan position"));
  }

  [Test]
  [Category("Unit")]
  public void APictureSizeChangeMidStreamIsRefused() {
    var first = H261TestStream.FlatQcifIntraPicture(255);
    var second = new H261TestStream().PictureHeader(isCif: true, temporalReference: 1).ToArray();

    var failure = Assert.Throws<NotSupportedException>(() => _Decode(first, second));
    Assert.That(failure.Message, Does.Contain("changes picture size"));
  }

  // ============================================================================================
  // Identity
  // ============================================================================================

  [TestCase("H261", true)]
  [TestCase("h261", true)]
  [TestCase("H263", false)]
  [TestCase("MPG1", false)]
  [Category("Unit")]
  public void TheCodecTakesTheStreamsItsContainerNames(string tag, bool expected)
    => Assert.That(H261VideoDecoder.Accepts(_Stream(tag)), Is.EqualTo(expected));

  [Test]
  [Category("Unit")]
  public void AnAudioStreamIsNotTakenWhateverItsTag()
    => Assert.That(
      H261VideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("H261") }),
      Is.False);

  // ============================================================================================
  // Streams
  // ============================================================================================

  /// <summary>A flat QCIF intra picture whose first luminance block carries the ramp 142..114.</summary>
  private static byte[] _RampPicture() {
    var stream = new H261TestStream().PictureHeader();
    stream.GroupHeader(1, 1);
    stream.MacroblockAddress(1).Code(H261TestStream.TypeIntra);
    stream.Bits(255, 8).EscapedCoefficient(0, 40).EndOfBlock();
    stream.IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255).IntraBlock(255);
    for (var address = 2; address <= 33; ++address)
      stream.FlatIntraMacroblock(1, 255);
    stream.FlatIntraGroup(3, 1, 255);
    stream.FlatIntraGroup(5, 1, 255);
    return stream.ToArray();
  }

  /// <summary>A second picture whose only transmitted macroblock is address 1, Inter+MC at this MVD.</summary>
  private static byte[] _ShiftedPicture(int mvdValue) {
    var stream = new H261TestStream().PictureHeader(temporalReference: 1);
    stream.GroupHeader(1, 1);
    stream.MacroblockAddress(1).Code(H261TestStream.TypeInterMc)
      .MotionVectorComponent(mvdValue).MotionVectorComponent(0);
    stream.GroupHeader(3, 1);
    stream.GroupHeader(5, 1);
    return stream.ToArray();
  }

  private static MediaStreamInfo _Stream(string tag)
    => new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters(tag) };

  private static List<RawImage> _Decode(params byte[][] pictures) {
    var decoder = H261VideoDecoder.Create(_Stream("H261"));
    var frames = new List<RawImage>();

    foreach (var picture in pictures)
      if (decoder.TryDecode(new(0, picture), out var frame))
        frames.Add(frame);

    return frames;
  }

  private static byte _Red(RawImage image, int x, int y) => image.PixelData[(y * image.Width + x) * 3];

  private static byte _Grey(int luminance) => (byte)Math.Clamp((298 * (luminance - 16) + 128) >> 8, 0, 255);
}
