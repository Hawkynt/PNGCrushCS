using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.UtVideo.Tests;

/// <summary>
/// The Ut Video decoder, on streams built here symbol by symbol.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg over 163 streams and 883 frames: every pixel
/// format its encoder writes, all four predictors, both colour-space spellings, slice counts from
/// one to eight, and geometries where the slice count does not divide the height. What these tests
/// add is what that comparison cannot reach, and it is most of what is interesting about this
/// format — because almost none of the coding is written down anywhere, every rule below was
/// established by measurement, and each test here pins one of them against the reading it was
/// mistaken for on the way.
/// </remarks>
[TestFixture]
public class UtVideoDecoderTests {

  // ============================================================================================
  // The Huffman tables
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void CodesAreAssignedFromTheLongestLengthDownAndNotTheShortestUp() {
    // Lengths of 1, 2, 2 for the first three symbols. The format's own description says the longest
    // code has a zero prefix and the shortest is all ones, so the two length-2 codes are 00 and 01
    // and the length-1 code is 1. The canonical assignment every reader reaches for first would make
    // them 0, 10 and 11 and would read this slice as another picture entirely.
    var stream = UtVideoTestStream.Stream("ULY4", 4, 1);
    var lengths = UtVideoTestStream.LengthsOf(1, 2, 2);
    var slice = new UtVideoTestStream().Code("1 00 01 1").End();
    var frame = UtVideoTestStream.Frame(
      UtVideoTestStream.NONE, new(lengths, [slice]), new(lengths, [slice]), new(lengths, [slice]));

    var planes = _Planes(stream, frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 0, 2, 1, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void WithinOneLengthTheHighestSymbolTakesTheFirstCode() {
    // Four symbols sharing a length of two, so the order between them is the whole of the reading.
    // Taken from the highest down, 3 gets 00, 2 gets 01, 1 gets 10 and 0 gets 11; ascending order —
    // which is what every other Huffman format here does — reverses the picture. Nothing states
    // this; it was found on a plane whose length-5 symbols were 127, 253, 254 and 255, where
    // ascending order decoded every short code correctly and handed back 253 wherever the picture
    // had 254.
    var stream = UtVideoTestStream.Stream("ULY4", 4, 1);
    var lengths = UtVideoTestStream.LengthsOf(2, 2, 2, 2);
    var slice = new UtVideoTestStream().Code("00 01 10 11").End();
    var frame = UtVideoTestStream.Frame(
      UtVideoTestStream.NONE, new(lengths, [slice]), new(lengths, [slice]), new(lengths, [slice]));

    var planes = _Planes(stream, frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 3, 2, 1, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void APlaneThatUsesOneSymbolCostsNoBitsAtAll() {
    // A flat alpha channel produces this on every frame it appears in: the one symbol that occurs is
    // given a length of nought and the slice carries nothing, because there is nothing to tell
    // apart. Reading the nought as a malformed length refuses a picture that is merely opaque.
    var stream = UtVideoTestStream.Stream("ULY4", 4, 2);
    var frame = UtVideoTestStream.Frame(
      UtVideoTestStream.NONE,
      new(UtVideoTestStream.OnlySymbol(200), [[]]),
      new(UtVideoTestStream.OnlySymbol(9), [[]]),
      new(UtVideoTestStream.OnlySymbol(255), [[]]));

    var planes = _Planes(stream, frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 200, 200, 200, 200, 200, 200, 200, 200 }));
    Assert.That(planes[2], Is.EqualTo(new byte[] { 255, 255, 255, 255, 255, 255, 255, 255 }));
  }

  [Test]
  [Category("Unit")]
  public void ATableWhoseLengthsDoNotDescribeACompleteCodeIsRefused() {
    var stream = UtVideoTestStream.Stream("ULY4", 4, 1);
    var lengths = UtVideoTestStream.LengthsOf(1, 2);
    var frame = UtVideoTestStream.Frame(
      UtVideoTestStream.NONE, new(lengths, [[0, 0, 0, 0]]), new(lengths, [[0, 0, 0, 0]]),
      new(lengths, [[0, 0, 0, 0]]));

    var failure = Assert.Throws<InvalidDataException>(() => _Planes(stream, frame));
    Assert.That(failure!.Message, Does.Contain("complete code"));
  }

  [Test]
  [Category("Unit")]
  public void ALengthOfNoughtBesideOtherSymbolsIsRefused() {
    // Nought means "the only symbol there is". Beside a second symbol it means nothing, and reading
    // it as a zero-bit code would put the decoder in a loop that never consumes a bit.
    var stream = UtVideoTestStream.Stream("ULY4", 4, 1);
    var lengths = UtVideoTestStream.LengthsOf(0, 1);
    var frame = UtVideoTestStream.Frame(
      UtVideoTestStream.NONE, new(lengths, [[0, 0, 0, 0]]), new(lengths, [[0, 0, 0, 0]]),
      new(lengths, [[0, 0, 0, 0]]));

    var failure = Assert.Throws<InvalidDataException>(() => _Planes(stream, frame));
    Assert.That(failure!.Message, Does.Contain("one symbol"));
  }

  // ============================================================================================
  // The bits
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryFourBytesOfASliceAreReadBackToFront() {
    // The slice below is the bytes 4, 3, 2, 1 as they lie in the file. Read in file order they are
    // the complements 251, 252, 253, 254; read as the little-endian word the coder wrote, they are
    // 1, 2, 3, 4 and so the symbols 254, 253, 252, 251. Nothing states the word order — it was found
    // by a plane that decodes correctly for a dozen samples and then wanders, which is what a bit
    // stream right in blocks of four bytes and scrambled between them looks like.
    var stream = UtVideoTestStream.Stream("ULY4", 4, 1);
    var frame = UtVideoTestStream.Frame(
      UtVideoTestStream.NONE,
      UtVideoTestStream.Plane.Flat([4, 3, 2, 1]),
      UtVideoTestStream.Plane.Flat([4, 3, 2, 1]),
      UtVideoTestStream.Plane.Flat([4, 3, 2, 1]));

    var planes = _Planes(stream, frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 254, 253, 252, 251 }));
  }

  // ============================================================================================
  // The predictors
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void LeftPredictionStartsASliceAtOneHundredAndTwentyEight() {
    // Not at nought, which is the value a reader assumes. Left prediction is a running sum, so the
    // starting value never leaves it: reading it as nought is wrong by exactly 128 on every sample
    // of every plane, which is a whole picture out rather than a corner of one.
    var stream = UtVideoTestStream.Stream("ULY4", 4, 1);
    var slice = new UtVideoTestStream().Symbols(1, 1, 1, 1).End();
    var frame = UtVideoTestStream.Frame(
      UtVideoTestStream.LEFT, UtVideoTestStream.Plane.Flat(slice),
      UtVideoTestStream.Plane.Flat(slice), UtVideoTestStream.Plane.Flat(slice));

    var planes = _Planes(stream, frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 129, 130, 131, 132 }));
  }

  [Test]
  [Category("Unit")]
  public void LeftPredictionCarriesTheRunningSumAcrossTheEndOfARow() {
    var stream = UtVideoTestStream.Stream("ULY4", 2, 2);
    var slice = new UtVideoTestStream().Symbols(1, 1, 1, 1).End();
    var frame = UtVideoTestStream.Frame(
      UtVideoTestStream.LEFT, UtVideoTestStream.Plane.Flat(slice),
      UtVideoTestStream.Plane.Flat(slice), UtVideoTestStream.Plane.Flat(slice));

    var planes = _Planes(stream, frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 129, 130, 131, 132 }));
  }

  [Test]
  [Category("Unit")]
  public void ASampleWrapsRatherThanSaturating() {
    // 128 + 200 is 72 and not 255. Saturating would lose the codec's losslessness at the first
    // sample either side of the range, which is exactly where a lossless codec must not lose it.
    var stream = UtVideoTestStream.Stream("ULY4", 2, 1);
    var slice = new UtVideoTestStream().Symbols(200, 200).End();
    var frame = UtVideoTestStream.Frame(
      UtVideoTestStream.LEFT, UtVideoTestStream.Plane.Flat(slice),
      UtVideoTestStream.Plane.Flat(slice), UtVideoTestStream.Plane.Flat(slice));

    var planes = _Planes(stream, frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 72, 16 }));
  }

  [Test]
  [Category("Unit")]
  public void TheMedianPredictorRunsOnPastTheEndOfARow() {
    // The sample to the left of column zero is the last sample of the row above, and the one
    // above-left of it the last sample of the row above that. Reading the first sample of every row
    // as predicted from the sample above it instead reproduces most rows of most pictures — the
    // linear rule usually chooses that sample anyway — and quietly gets the rest wrong.
    //
    // Row 0 is 128, 129, 131, 134 by left prediction from 128.
    // Row 1 opens predicted from the sample above it: 128 + 1 = 129.
    // Then left=129, above=129, above-left=128, gradient=130, median(129,129,130)=129, +1 = 130.
    var stream = UtVideoTestStream.Stream("ULY4", 4, 2);
    var slice = new UtVideoTestStream().Symbols(0, 1, 2, 3, 1, 1, 1, 1).End();
    var frame = UtVideoTestStream.Frame(
      UtVideoTestStream.MEDIAN, UtVideoTestStream.Plane.Flat(slice),
      UtVideoTestStream.Plane.Flat(slice), UtVideoTestStream.Plane.Flat(slice));

    var planes = _Planes(stream, frame);

    Assert.That(planes[0][..4], Is.EqualTo(new byte[] { 128, 129, 131, 134 }));
    Assert.That(planes[0][4], Is.EqualTo(129));
  }

  [Test]
  [Category("Unit")]
  public void TheGradientPredictorStartsEveryRowFromTheSampleAboveIt() {
    // Where the median runs on linearly, the gradient does not: at column zero it takes the sample
    // above and nothing else, which is what left + above - above-left reduces to when the left and
    // the above-left are the same absent thing. ffmpeg's encoder will not write a gradient frame, so
    // this was established the other way round — by coding streams and having ffmpeg's decoder read
    // them back — and it is the one rule here that no encoded file could have shown.
    //
    // Row 0: 128, 138, 148 by left prediction. Row 1 column 0: above is 128, plus 5 is 133.
    // Row 1 column 1: left 133, above 138, above-left 128, gradient 143, plus 0 is 143.
    var stream = UtVideoTestStream.Stream("ULY4", 3, 2);
    var slice = new UtVideoTestStream().Symbols(0, 10, 10, 5, 0, 0).End();
    var frame = UtVideoTestStream.Frame(
      UtVideoTestStream.GRADIENT, UtVideoTestStream.Plane.Flat(slice),
      UtVideoTestStream.Plane.Flat(slice), UtVideoTestStream.Plane.Flat(slice));

    var planes = _Planes(stream, frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 128, 138, 148, 133, 143, 153 }));
  }

  [Test]
  [Category("Unit")]
  public void NoPredictionLeavesTheSymbolsAsTheSamples() {
    var stream = UtVideoTestStream.Stream("ULY4", 3, 1);
    var slice = new UtVideoTestStream().Symbols(7, 200, 255).End();
    var frame = UtVideoTestStream.Frame(
      UtVideoTestStream.NONE, UtVideoTestStream.Plane.Flat(slice),
      UtVideoTestStream.Plane.Flat(slice), UtVideoTestStream.Plane.Flat(slice));

    var planes = _Planes(stream, frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 7, 200, 255 }));
  }

  // ============================================================================================
  // The slices
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EachSliceStartsItsPredictionAgain() {
    // Which is the whole point of having slices: a decoder with four cores can take one each only
    // if none of them needs the one before it. Both slices below carry the same symbols and both
    // come out the same, which they could not if the sum ran on between them.
    var stream = UtVideoTestStream.Stream("ULY4", 2, 2, slices: 2);
    var first = new UtVideoTestStream().Symbols(1, 1).End();
    var second = new UtVideoTestStream().Symbols(1, 1).End();
    var plane = new UtVideoTestStream.Plane(UtVideoTestStream.FlatLengths(), [first, second]);
    var frame = UtVideoTestStream.Frame(UtVideoTestStream.LEFT, plane, plane, plane);

    var planes = _Planes(stream, frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 129, 130, 129, 130 }));
  }

  [Test]
  [Category("Unit")]
  public void AFourTwoZeroFrameIsCutOnWholeChrominanceRows() {
    // A 4:2:0 frame cannot be cut between the two luminance rows that share a chrominance row. The
    // cut is rounded down to a whole chrominance row and the luminance boundary follows from it, so
    // eighteen rows in five slices start at luminance rows 0, 2, 6, 10 and 14. Dividing each plane
    // on its own height instead puts the luminance boundaries at 0, 3, 7, 10 and 14 against the same
    // chrominance boundaries, so the two planes cover different bands of picture and both decode
    // into rubbish from the second slice on. This is exactly the frame it was found on.
    var format = UtVideoFormat.Parse(CodecTag.FromCharacters("ULY0"), _Extra(slices: 5), 0);

    var luma = new int[6];
    var chroma = new int[6];
    for (var i = 0; i < 6; ++i) {
      luma[i] = format.SliceStart(i, 18, 0);
      chroma[i] = format.SliceStart(i, 18, 1);
    }

    Assert.That(format.SliceCount, Is.EqualTo(5));
    Assert.That(luma, Is.EqualTo(new[] { 0, 2, 6, 10, 14, 18 }));
    Assert.That(chroma, Is.EqualTo(new[] { 0, 1, 3, 5, 7, 9 }));
  }

  [Test]
  [Category("Unit")]
  public void TheSliceCountIsTheTopByteOfTheFlagsPlusOne() {
    Assert.That(UtVideoFormat.Parse(CodecTag.FromCharacters("ULY2"), _Extra(slices: 1), 0).SliceCount, Is.EqualTo(1));
    Assert.That(UtVideoFormat.Parse(CodecTag.FromCharacters("ULY2"), _Extra(slices: 8), 0).SliceCount, Is.EqualTo(8));
    Assert.That(UtVideoFormat.Parse(CodecTag.FromCharacters("ULY2"), _Extra(slices: 256), 0).SliceCount, Is.EqualTo(256));
  }

  // ============================================================================================
  // The colour
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void BlueAndRedAreStoredAsTheirDistanceFromGreenWithOneHundredAndTwentyEightAdded() {
    // The community write-up says only that red and blue are "a difference to the correspondent
    // green value". The 128 is not mentioned anywhere, and a decoder that adds green alone is out by
    // exactly that on every sample of both planes — a picture with its blues and reds inverted
    // rather than one that looks broken.
    //
    // Green 100; blue stored 128 means blue equals green; red stored 148 means green plus 20.
    var stream = UtVideoTestStream.Stream("ULRG", 2, 1);
    var frame = UtVideoTestStream.Frame(
      UtVideoTestStream.NONE,
      UtVideoTestStream.Plane.Flat(new UtVideoTestStream().Symbols(100, 100).End()),
      UtVideoTestStream.Plane.Flat(new UtVideoTestStream().Symbols(128, 108).End()),
      UtVideoTestStream.Plane.Flat(new UtVideoTestStream().Symbols(148, 128).End()));

    var planes = _Planes(stream, frame);

    Assert.That(planes[0], Is.EqualTo(new byte[] { 100, 100 }), "green");
    Assert.That(planes[1], Is.EqualTo(new byte[] { 100, 80 }), "blue");
    Assert.That(planes[2], Is.EqualTo(new byte[] { 120, 100 }), "red");
  }

  [Test]
  [Category("Unit")]
  public void TheColourPlanesAreGreenThenBlueThenRed() {
    // The community write-up gives the order as green, red, blue. Every file measured here has blue
    // second and red third, which is settled by a picture's blues and reds coming out the right way
    // round rather than swapped.
    var stream = UtVideoTestStream.Stream("ULRG", 1, 1);
    var frame = UtVideoTestStream.Frame(
      UtVideoTestStream.NONE,
      UtVideoTestStream.Plane.Flat(new UtVideoTestStream().Symbols(0).End()),
      UtVideoTestStream.Plane.Flat(new UtVideoTestStream().Symbols(128 + 10).End()),
      UtVideoTestStream.Plane.Flat(new UtVideoTestStream().Symbols(128 + 20).End()));

    var decoder = UtVideoDecoder.Create(stream);
    Assert.That(decoder.TryDecode(new(0, frame), out var picture), Is.True);

    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 20, 0, 10 }));
  }

  [Test]
  [Category("Unit")]
  public void AnAlphaPlaneIsNotDecorrelated() {
    // Only blue and red are carried against green. Alpha is a plane like any other, and adding green
    // to it would make an opaque picture translucent wherever it is dark.
    var stream = UtVideoTestStream.Stream("ULRA", 1, 1);
    var frame = UtVideoTestStream.Frame(
      UtVideoTestStream.NONE,
      UtVideoTestStream.Plane.Flat(new UtVideoTestStream().Symbols(30).End()),
      UtVideoTestStream.Plane.Flat(new UtVideoTestStream().Symbols(128).End()),
      UtVideoTestStream.Plane.Flat(new UtVideoTestStream().Symbols(128).End()),
      UtVideoTestStream.Plane.Flat(new UtVideoTestStream().Symbols(200).End()));

    var decoder = UtVideoDecoder.Create(stream);
    Assert.That(decoder.TryDecode(new(0, frame), out var picture), Is.True);

    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 30, 30, 30, 200 }));
  }

  [Test]
  [Category("Unit")]
  public void TheCodeSaysWhichPrimariesTheChrominanceIsAgainst() {
    // ULY2 and ULH2 are the same bits read against a different matrix, and the four-character code
    // is the only thing that says which. A mid grey is grey either way; a saturated chrominance is
    // not, which is what this measures.
    var frame = UtVideoTestStream.Frame(
      UtVideoTestStream.NONE,
      UtVideoTestStream.Plane.Flat(new UtVideoTestStream().Symbols(128, 128).End()),
      UtVideoTestStream.Plane.Flat(new UtVideoTestStream().Symbols(200).End()),
      UtVideoTestStream.Plane.Flat(new UtVideoTestStream().Symbols(60).End()));

    var bt601 = _Picture(UtVideoTestStream.Stream("ULY2", 2, 1), frame);
    var bt709 = _Picture(UtVideoTestStream.Stream("ULH2", 2, 1), frame);

    Assert.That(bt601.PixelData, Is.Not.EqualTo(bt709.PixelData));
  }

  // ============================================================================================
  // What refuses
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFrameCodedWithFiniteStateEntropyCodingIsRefusedByName() {
    // Version 23 of the codec added a mode that keeps the median prediction and replaces the Huffman
    // coding with the entropy coder from Zstandard. The flag that says a frame is Huffman coded is
    // clear in such a stream, and its bitstream is published nowhere.
    var stream = UtVideoTestStream.Stream("ULY2", 4, 4, flags: 0);

    var failure = Assert.Throws<NotSupportedException>(() => UtVideoDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("finite state entropy"));
  }

  [Test]
  [Category("Unit")]
  public void AnInterlacedStreamIsRefusedByName() {
    var stream = UtVideoTestStream.Stream(
      "ULY2", 4, 4, flags: UtVideoTestStream.HUFFMAN | UtVideoTestStream.INTERLACED);

    var failure = Assert.Throws<NotSupportedException>(() => UtVideoDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("interlace"));
  }

  [Test]
  [Category("Unit")]
  public void TheTenBitProCodesAreRefusedByName() {
    foreach (var code in new[] { "UQRG", "UQRA", "UQY2", "UQY0" }) {
      var stream = UtVideoTestStream.Stream(code, 4, 4);
      Assert.That(UtVideoDecoder.Accepts(stream), Is.True, code);

      var failure = Assert.Throws<NotSupportedException>(() => UtVideoDecoder.Create(stream), code);
      Assert.That(failure!.Message, Does.Contain("ten-bit"), code);
    }
  }

  [Test]
  [Category("Unit")]
  public void TheTwoFamilyCodesAreRefusedByName() {
    foreach (var code in new[] { "UMRG", "UMRA", "UMY2", "UMY4", "UMH2", "UMH4" }) {
      var stream = UtVideoTestStream.Stream(code, 4, 4);
      Assert.That(UtVideoDecoder.Accepts(stream), Is.True, code);

      var failure = Assert.Throws<NotSupportedException>(() => UtVideoDecoder.Create(stream), code);
      Assert.That(failure!.Message, Does.Contain("T2"), code);
    }
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoRoomForTheSliceCountIsRefused() {
    var stream = UtVideoTestStream.Stream("ULY2", 4, 4, extraLength: 8);

    var failure = Assert.Throws<InvalidDataException>(() => UtVideoDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("stream description"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameWhosePartsDoNotAddUpToItsLengthIsRefused() {
    var stream = UtVideoTestStream.Stream("ULY4", 2, 1);
    var slice = new UtVideoTestStream().Symbols(1, 1).End();
    var frame = UtVideoTestStream.Frame(
      UtVideoTestStream.NONE, UtVideoTestStream.Plane.Flat(slice),
      UtVideoTestStream.Plane.Flat(slice), UtVideoTestStream.Plane.Flat(slice));
    Array.Resize(ref frame, frame.Length + 7);

    var failure = Assert.Throws<InvalidDataException>(() => _Planes(stream, frame));
    Assert.That(failure!.Message, Does.Contain("unaccounted for"));
  }

  [Test]
  [Category("Unit")]
  public void ASubsampledStreamOfOddSizeIsRefused() {
    var across = UtVideoTestStream.Stream("ULY2", 5, 4);
    Assert.That(Assert.Throws<InvalidDataException>(() => UtVideoDecoder.Create(across))!.Message,
      Does.Contain("odd width"));

    var down = UtVideoTestStream.Stream("ULY0", 4, 5);
    Assert.That(Assert.Throws<InvalidDataException>(() => UtVideoDecoder.Create(down))!.Message,
      Does.Contain("odd height"));
  }

  [Test]
  [Category("Unit")]
  public void APictureOfNoSizeIsRefused() {
    var stream = UtVideoTestStream.Stream("ULY2", 0, 0);

    Assert.Throws<InvalidDataException>(() => UtVideoDecoder.Create(stream));
  }

  // ============================================================================================
  // Which streams this codec answers for
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheCodecAnswersForEveryCodeItReads() {
    foreach (var code in new[] { "ULRG", "ULRA", "ULY0", "ULY2", "ULY4", "ULH0", "ULH2", "ULH4" })
      Assert.That(UtVideoDecoder.Accepts(UtVideoTestStream.Stream(code, 4, 4)), Is.True, code);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecDoesNotAnswerForAnotherCode() {
    Assert.That(UtVideoDecoder.Accepts(UtVideoTestStream.Stream("HFYU", 4, 4)), Is.False);
    Assert.That(UtVideoDecoder.Accepts(UtVideoTestStream.Stream("FFV1", 4, 4)), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void EveryCodeItReadsIsReachableThroughTheRegistry() {
    foreach (var code in new[] { "ULRG", "ULRA", "ULY0", "ULY2", "ULY4", "ULH0", "ULH2", "ULH4" })
      Assert.That(
        Hawkynt.FileFormats.Video.VideoFormatRegistry.CanDecode(UtVideoTestStream.Stream(code, 4, 4)),
        Is.True, code);
  }

  // ============================================================================================

  private static byte[] _Extra(int slices) {
    var extra = new byte[16];
    BitConverter.GetBytes(4u).CopyTo(extra, 8);
    BitConverter.GetBytes(UtVideoTestStream.HUFFMAN | ((uint)(slices - 1) << 24)).CopyTo(extra, 12);
    return extra;
  }

  private static byte[][] _Planes(MediaStreamInfo stream, byte[] frame)
    => UtVideoDecoder.Create(stream).DecodePlanes(frame);

  private static RawImage _Picture(MediaStreamInfo stream, byte[] frame) {
    var decoder = UtVideoDecoder.Create(stream);
    Assert.That(decoder.TryDecode(new(0, frame), out var picture), Is.True);
    return picture;
  }
}
