using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.MagicYuv.Tests;

/// <summary>
/// The MagicYUV decoder, on frames built here symbol by symbol.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured over 309 streams and 1,446 frames against the pictures the
/// frames were made from. What these tests add is what that comparison cannot reach — and for this
/// codec that is nearly everything interesting, because the format is published nowhere and every
/// rule below was established by measurement. Each test pins one of them against the reading it was
/// mistaken for on the way.
/// </remarks>
[TestFixture]
public class MagicYuvDecoderTests {

  // ============================================================================================
  // The Huffman tables
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void CodesAreAssignedFromTheLongestLengthDownAndNotTheShortestUp() {
    // Lengths of 1, 2, 2 for the first three symbols. Handing the codes out from the longest length
    // down makes them 1, 00 and 01, so the shortest code is all ones; the canonical assignment a
    // reader reaches for first would make them 0, 10 and 11. A picture of flat colour settles it
    // without any of this arithmetic: its slice data is a run of 0xFF bytes, which it could only be
    // if its commonest symbol's one-bit code is a one.
    var stream = MagicYuvTestStream.Stream("M8G0", 4, 1);
    var body = new MagicYuvTestStream().Code("1 00 01 1").End();
    var frame = MagicYuvTestStream.SinglePlane(
      4, 1, MagicYuvTestStream.LEFT, body, MagicYuvTestStream.LengthsOf(1, 2, 2));

    var planes = _Planes(stream, frame);

    // left prediction from nought: 0, then 0+1, then 1+2, then 3+0
    Assert.That(planes[0], Is.EqualTo(new byte[] { 0, 1, 3, 3 }));
  }

  [Test]
  [Category("Unit")]
  public void WithinOneLengthTheLowestSymbolTakesTheFirstCode() {
    // Four symbols sharing a length of two, so the order between them is the whole of the reading.
    // Taken ascending, 0 gets 00, 1 gets 01, 2 gets 10 and 3 gets 11. Ut Video, whose construction
    // is otherwise identical, takes them the other way round — so getting this wrong decodes a
    // plane's commonest symbol correctly and almost nothing else.
    var stream = MagicYuvTestStream.Stream("M8G0", 4, 1);
    var body = new MagicYuvTestStream().Code("00 01 10 11").End();
    var frame = MagicYuvTestStream.SinglePlane(
      4, 1, MagicYuvTestStream.LEFT, body, MagicYuvTestStream.LengthsOf(2, 2, 2, 2));

    var planes = _Planes(stream, frame);

    // the symbols are 0, 1, 2, 3 and left prediction runs them up
    Assert.That(planes[0], Is.EqualTo(new byte[] { 0, 1, 3, 6 }));
  }

  [Test]
  [Category("Unit")]
  public void TheBitsAreReadStraightOutOfTheBytes() {
    // No little-endian word swapping, which is what HuffYUV and Ut Video both need. With every
    // symbol eight bits long, a slice is its differences one byte each and in file order: reading
    // it four bytes at a time back to front would give 4, 3, 2, 1 here instead.
    var stream = MagicYuvTestStream.Stream("M8G0", 4, 1);
    var frame = MagicYuvTestStream.SinglePlane(4, 1, MagicYuvTestStream.LEFT, [1, 2, 3, 4]);

    var planes = _Planes(stream, frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 1, 3, 6, 10 }));
  }

  [Test]
  [Category("Unit")]
  public void ATableWhoseLengthsDoNotDescribeACompleteCodeIsRefused() {
    var stream = MagicYuvTestStream.Stream("M8G0", 4, 1);
    var frame = MagicYuvTestStream.SinglePlane(
      4, 1, MagicYuvTestStream.LEFT, [0, 0, 0, 0], MagicYuvTestStream.LengthsOf(1, 2));

    var failure = Assert.Throws<InvalidDataException>(() => _Planes(stream, frame));
    Assert.That(failure!.Message, Does.Contain("complete code"));
  }

  [Test]
  [Category("Unit")]
  public void ACodeLongerThanTheFrameSaysItUsesIsRefused() {
    // The byte after the format states the longest code the frame's tables hold — twelve in every
    // eight-bit frame measured, which is also the longest any of their tables gives.
    var stream = MagicYuvTestStream.Stream("M8G0", 4, 1);
    var lengths = MagicYuvTestStream.LengthsOf(1, 14, 14);
    var frame = MagicYuvTestStream.SinglePlane(4, 1, MagicYuvTestStream.LEFT, [0, 0, 0, 0], lengths);

    var failure = Assert.Throws<InvalidDataException>(() => _Planes(stream, frame));
    Assert.That(failure!.Message, Does.Contain("longest"));
  }

  // ============================================================================================
  // The predictors
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryRowStartsAgainFromTheSampleAboveIt() {
    // Not from the end of the row before, which is what HuffYUV and Ut Video do. Reading it their
    // way decodes the first row of a plane exactly and puts every row after it out — which is how it
    // was found, on a plane that agreed for exactly its first 64 samples and disagreed from the
    // 65th.
    //
    // Row 0 is 10, 20, 30 by left prediction from nought. Row 1 opens at 10 + 5 = 15.
    var stream = MagicYuvTestStream.Stream("M8G0", 3, 2);
    var frame = MagicYuvTestStream.SinglePlane(
      3, 2, MagicYuvTestStream.LEFT, [10, 10, 10, 5, 0, 0]);

    var planes = _Planes(stream, frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 10, 20, 30, 15, 15, 15 }));
  }

  [Test]
  [Category("Unit")]
  public void TheGradientPredictorIsLeftPlusAboveLessAboveLeft() {
    // Row 0: 10, 20, 30. Row 1 column 0 takes the sample above: 10 + 0 = 10.
    // Row 1 column 1: left 10, above 20, above-left 10, so 20, plus 0.
    // Row 1 column 2: left 20, above 30, above-left 20, so 30, plus 0.
    var stream = MagicYuvTestStream.Stream("M8G0", 3, 2);
    var frame = MagicYuvTestStream.SinglePlane(
      3, 2, MagicYuvTestStream.GRADIENT, [10, 10, 10, 0, 0, 0]);

    var planes = _Planes(stream, frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 10, 20, 30, 10, 20, 30 }));
  }

  [Test]
  [Category("Unit")]
  public void TheMedianPredictorTakesTheMiddleOfTheLeftTheAboveAndTheGradient() {
    // Row 0: 10, 200, 30. Row 1 column 0 takes the sample above: 10.
    // Column 1: left 10, above 200, gradient 10 + 200 - 10 = 200, median(10, 200, 200) = 200.
    // Column 2: left 200, above 30, gradient 200 + 30 - 200 = 30, median(200, 30, 30) = 30.
    var stream = MagicYuvTestStream.Stream("M8G0", 3, 2);
    var frame = MagicYuvTestStream.SinglePlane(
      3, 2, MagicYuvTestStream.MEDIAN, [10, 190, 86, 0, 0, 0]);

    var planes = _Planes(stream, frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 10, 200, 30, 10, 200, 30 }));
  }

  [Test]
  [Category("Unit")]
  public void ASampleWrapsRatherThanSaturating() {
    // 100 + 200 is 44 and not 255. Saturating would lose the codec's losslessness at the first
    // sample either side of the range, which is exactly where a lossless codec must not lose it.
    var stream = MagicYuvTestStream.Stream("M8G0", 2, 1);
    var frame = MagicYuvTestStream.SinglePlane(2, 1, MagicYuvTestStream.LEFT, [100, 200]);

    var planes = _Planes(stream, frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 100, 44 }));
  }

  [Test]
  [Category("Unit")]
  public void ASliceStatingAPredictionMethodTheFormatDoesNotHaveIsRefused() {
    var stream = MagicYuvTestStream.Stream("M8G0", 2, 1);
    var frame = MagicYuvTestStream.SinglePlane(2, 1, predictor: 4, body: [0, 0]);

    var failure = Assert.Throws<InvalidDataException>(() => _Planes(stream, frame));
    Assert.That(failure!.Message, Does.Contain("none of the three"));
  }

  // ============================================================================================
  // A slice that is not coded at all
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASliceMayCarryItsDifferencesAsPlainBytes() {
    // Its first byte says so. The prediction still applies: only the entropy coding is skipped. A
    // frame too small for a Huffman table to pay for itself produces one, and so does a slice of
    // noise — sixty-four streams of the corpus contain at least one.
    var stream = MagicYuvTestStream.Stream("M8G0", 3, 1);
    var frame = MagicYuvTestStream.SinglePlane(
      3, 1, MagicYuvTestStream.LEFT, [76, 0, 187], flag: MagicYuvTestStream.UNCOMPRESSED);

    var planes = _Planes(stream, frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 76, 76, 7 }));
  }

  [Test]
  [Category("Unit")]
  public void ASliceWhoseFirstByteIsNeitherValueIsRefused() {
    var stream = MagicYuvTestStream.Stream("M8G0", 2, 1);
    var pieces = new MagicYuvTestStream.Piece[1, 1];
    pieces[0, 0] = new(9, MagicYuvTestStream.LEFT, [0, 0]);
    var frame = MagicYuvTestStream.Frame(
      2, 1, 1, 1, 1, [MagicYuvTestStream.FlatLengths()], pieces);

    var failure = Assert.Throws<InvalidDataException>(() => _Planes(stream, frame));
    Assert.That(failure!.Message, Does.Contain("neither the nought"));
  }

  // ============================================================================================
  // The slices
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EachSliceStartsItsPredictionAgain() {
    // Which is the whole point of having slices. Both slices below carry the same differences and
    // both come out the same, which they could not if the prediction ran on between them.
    var stream = MagicYuvTestStream.Stream("M8G0", 2, 2);
    var pieces = new MagicYuvTestStream.Piece[1, 2];
    pieces[0, 0] = MagicYuvTestStream.Piece.Coded(MagicYuvTestStream.LEFT, [5, 5]);
    pieces[0, 1] = MagicYuvTestStream.Piece.Coded(MagicYuvTestStream.LEFT, [5, 5]);
    var frame = MagicYuvTestStream.Frame(
      2, 2, 1, 1, 2, [MagicYuvTestStream.FlatLengths()], pieces);

    var planes = _Planes(stream, frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 5, 10, 5, 10 }));
  }

  [Test]
  [Category("Unit")]
  public void TheSliceMapNamesThePieceEachOffsetBelongsToAndNotTheOtherWayRound() {
    // Its k-th entry names the piece the k-th offset is for, where a piece is slice * planes +
    // plane. On a frame of one slice that map is the identity either way round, which is why a
    // single-slice frame decodes perfectly under the obvious reading and every other frame comes
    // apart. Here the two planes carry different pictures, so a shuffled reading swaps them.
    var stream = MagicYuvTestStream.Stream("M8RG", 2, 2);
    var pieces = new MagicYuvTestStream.Piece[3, 2];
    for (var p = 0; p < 3; ++p)
      for (var s = 0; s < 2; ++s)
        pieces[p, s] = MagicYuvTestStream.Piece.Coded(
          MagicYuvTestStream.LEFT, [(byte)(p * 10 + s), 0]);

    var frame = MagicYuvTestStream.Frame(
      2, 2, 1, 3, 2, [
        MagicYuvTestStream.FlatLengths(), MagicYuvTestStream.FlatLengths(),
        MagicYuvTestStream.FlatLengths(),
      ], pieces);

    var planes = _Planes(stream, frame);

    // plane 1 of the frame is green and comes back first; its two slices carry 10 and 11
    Assert.That(planes[0], Is.EqualTo(new byte[] { 10, 10, 11, 11 }), "green");
  }

  [Test]
  [Category("Unit")]
  public void ASliceHeightThatDoesNotDivideByTheChrominanceBlockIsRefused() {
    // A 4:2:0 frame cannot be cut between the two luminance rows that share a chrominance row.
    var stream = MagicYuvTestStream.Stream("M8Y0", 4, 4);
    var pieces = new MagicYuvTestStream.Piece[3, 4];
    for (var p = 0; p < 3; ++p)
      for (var s = 0; s < 4; ++s)
        pieces[p, s] = MagicYuvTestStream.Piece.Coded(MagicYuvTestStream.LEFT, [0, 0, 0, 0]);

    var frame = MagicYuvTestStream.Frame(
      4, 4, 1, 3, 4, [
        MagicYuvTestStream.FlatLengths(), MagicYuvTestStream.FlatLengths(),
        MagicYuvTestStream.FlatLengths(),
      ], pieces);

    var failure = Assert.Throws<InvalidDataException>(() => _Planes(stream, frame));
    Assert.That(failure!.Message, Does.Contain("slices would not line up"));
  }

  // ============================================================================================
  // The colour
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheColourPlanesAreBlueThenGreenThenRedAndTheOuterTwoCarryGreen() {
    // Blue and red are stored as their distance from green, with no offset — unlike Ut Video, whose
    // otherwise identical decorrelation adds 128. Green is plane one of the frame and comes back
    // first, because that is the order a planar colour buffer wants.
    var stream = MagicYuvTestStream.Stream("M8RG", 1, 1);
    var pieces = new MagicYuvTestStream.Piece[3, 1];
    pieces[0, 0] = MagicYuvTestStream.Piece.Coded(MagicYuvTestStream.LEFT, [10]);   // blue - green
    pieces[1, 0] = MagicYuvTestStream.Piece.Coded(MagicYuvTestStream.LEFT, [100]);  // green
    pieces[2, 0] = MagicYuvTestStream.Piece.Coded(MagicYuvTestStream.LEFT, [20]);   // red - green
    var frame = MagicYuvTestStream.Frame(
      1, 1, 1, 3, 1, [
        MagicYuvTestStream.FlatLengths(), MagicYuvTestStream.FlatLengths(),
        MagicYuvTestStream.FlatLengths(),
      ], pieces);

    var planes = _Planes(stream, frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 100 }), "green");
    Assert.That(planes[1], Is.EqualTo(new byte[] { 110 }), "blue");
    Assert.That(planes[2], Is.EqualTo(new byte[] { 120 }), "red");

    var decoder = MagicYuvDecoder.Create(stream);
    Assert.That(decoder.TryDecode(new(0, frame), out var picture), Is.True);
    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 120, 100, 110 }));
  }

  [Test]
  [Category("Unit")]
  public void AGreyStreamComesBackAsGreyAndNotAsColour() {
    var stream = MagicYuvTestStream.Stream("M8G0", 2, 1);
    var frame = MagicYuvTestStream.SinglePlane(2, 1, MagicYuvTestStream.LEFT, [40, 10]);

    var decoder = MagicYuvDecoder.Create(stream);
    Assert.That(decoder.TryDecode(new(0, frame), out var picture), Is.True);

    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 40, 50 }));
  }

  // ============================================================================================
  // What refuses
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFrameWithoutTheSignatureIsRefused() {
    var stream = MagicYuvTestStream.Stream("M8G0", 2, 1);
    var pieces = new MagicYuvTestStream.Piece[1, 1];
    pieces[0, 0] = MagicYuvTestStream.Piece.Coded(MagicYuvTestStream.LEFT, [0, 0]);
    var frame = MagicYuvTestStream.Frame(
      2, 1, 1, 1, 1, [MagicYuvTestStream.FlatLengths()], pieces,
      signature: [(byte)'N', (byte)'O', (byte)'P', (byte)'E']);

    var failure = Assert.Throws<InvalidDataException>(() => _Planes(stream, frame));
    Assert.That(failure!.Message, Does.Contain("signature"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameStatingAnotherHeaderSizeIsRefusedByName() {
    var stream = MagicYuvTestStream.Stream("M8G0", 2, 1);
    var pieces = new MagicYuvTestStream.Piece[1, 1];
    pieces[0, 0] = MagicYuvTestStream.Piece.Coded(MagicYuvTestStream.LEFT, [0, 0]);
    var frame = MagicYuvTestStream.Frame(
      2, 1, 1, 1, 1, [MagicYuvTestStream.FlatLengths()], pieces, headerSize: 40);

    var failure = Assert.Throws<NotSupportedException>(() => _Planes(stream, frame));
    Assert.That(failure!.Message, Does.Contain("header"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameWhoseVersionByteIsNotTheOneMeasuredIsRefusedByName() {
    var stream = MagicYuvTestStream.Stream("M8G0", 2, 1);
    var pieces = new MagicYuvTestStream.Piece[1, 1];
    pieces[0, 0] = MagicYuvTestStream.Piece.Coded(MagicYuvTestStream.LEFT, [0, 0]);
    var frame = MagicYuvTestStream.Frame(
      2, 1, 1, 1, 1, [MagicYuvTestStream.FlatLengths()], pieces, version: 6);

    var failure = Assert.Throws<NotSupportedException>(() => _Planes(stream, frame));
    Assert.That(failure!.Message, Does.Contain("nothing was measured against"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameStatingAnotherPictureSizeThanTheStreamIsRefused() {
    var stream = MagicYuvTestStream.Stream("M8G0", 4, 4);
    var frame = MagicYuvTestStream.SinglePlane(2, 1, MagicYuvTestStream.LEFT, [0, 0]);

    var failure = Assert.Throws<InvalidDataException>(() => _Planes(stream, frame));
    Assert.That(failure!.Message, Does.Contain("states a picture of"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameCarryingTheWrongNumberOfTablesIsRefused() {
    var stream = MagicYuvTestStream.Stream("M8G0", 2, 1);
    var pieces = new MagicYuvTestStream.Piece[1, 1];
    pieces[0, 0] = MagicYuvTestStream.Piece.Coded(MagicYuvTestStream.LEFT, [0, 0]);
    var frame = MagicYuvTestStream.Frame(
      2, 1, 1, 1, 1, [MagicYuvTestStream.FlatLengths()], pieces, tableCount: 2);

    var failure = Assert.Throws<InvalidDataException>(() => _Planes(stream, frame));
    Assert.That(failure!.Message, Does.Contain("one each"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameWhoseSliceMapNamesAPieceTwiceIsRefused() {
    var stream = MagicYuvTestStream.Stream("M8G0", 2, 2);
    var pieces = new MagicYuvTestStream.Piece[1, 2];
    pieces[0, 0] = MagicYuvTestStream.Piece.Coded(MagicYuvTestStream.LEFT, [0, 0]);
    pieces[0, 1] = MagicYuvTestStream.Piece.Coded(MagicYuvTestStream.LEFT, [0, 0]);
    var frame = MagicYuvTestStream.Frame(
      2, 2, 1, 1, 2, [MagicYuvTestStream.FlatLengths()], pieces, map: [0, 0]);

    var failure = Assert.Throws<InvalidDataException>(() => _Planes(stream, frame));
    Assert.That(failure!.Message, Does.Contain("twice"));
  }

  [Test]
  [Category("Unit")]
  public void TheDeeperSampleCodesAreRefusedByName() {
    foreach (var code in new[] { "M0RG", "M0RA", "M0Y0", "M0Y2", "M0Y4", "M0G0", "M2RG", "M2RA", "M4RG", "M4RA" }) {
      var stream = MagicYuvTestStream.Stream(code, 4, 4);
      Assert.That(MagicYuvDecoder.Accepts(stream), Is.True, code);

      var failure = Assert.Throws<NotSupportedException>(() => MagicYuvDecoder.Create(stream), code);
      Assert.That(failure!.Message, Does.Contain("deeper than eight bits"), code);
    }
  }

  [Test]
  [Category("Unit")]
  public void TheCodeFromBeforeEachFormatHadOneIsRefusedByName() {
    var stream = MagicYuvTestStream.Stream("MAGY", 4, 4);
    Assert.That(MagicYuvDecoder.Accepts(stream), Is.True);

    var failure = Assert.Throws<NotSupportedException>(() => MagicYuvDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("before it gave each pixel format"));
  }

  [Test]
  [Category("Unit")]
  public void GreyWithAlphaIsRefusedByName() {
    var stream = MagicYuvTestStream.Stream("M8GA", 4, 4);
    Assert.That(MagicYuvDecoder.Accepts(stream), Is.True);

    var failure = Assert.Throws<NotSupportedException>(() => MagicYuvDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("alpha"));
  }

  // ============================================================================================
  // Which streams this codec answers for
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheCodecAnswersForEveryCodeItReads() {
    foreach (var code in new[] { "M8RG", "M8RA", "M8Y0", "M8Y2", "M8Y4", "M8YA", "M8G0" })
      Assert.That(MagicYuvDecoder.Accepts(MagicYuvTestStream.Stream(code, 4, 4)), Is.True, code);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecDoesNotAnswerForAnotherCode() {
    Assert.That(MagicYuvDecoder.Accepts(MagicYuvTestStream.Stream("HFYU", 4, 4)), Is.False);
    Assert.That(MagicYuvDecoder.Accepts(MagicYuvTestStream.Stream("FFV1", 4, 4)), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void EveryCodeItReadsIsReachableThroughTheRegistry() {
    foreach (var code in new[] { "M8RG", "M8RA", "M8Y0", "M8Y2", "M8Y4", "M8YA", "M8G0" })
      Assert.That(
        Hawkynt.FileFormats.Video.VideoFormatRegistry.CanDecode(MagicYuvTestStream.Stream(code, 4, 4)),
        Is.True, code);
  }

  // ============================================================================================

  private static byte[][] _Planes(MediaStreamInfo stream, byte[] frame)
    => MagicYuvDecoder.Create(stream).DecodePlanes(frame);
}
