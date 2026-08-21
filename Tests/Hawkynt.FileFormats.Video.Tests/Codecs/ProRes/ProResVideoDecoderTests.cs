using System;
using System.IO;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Codecs.ProRes.Tests;

/// <summary>
/// The Apple ProRes decoder, on frames built here a codeword at a time.
/// </summary>
/// <remarks>
/// The arithmetic was checked against ffmpeg over every profile both of its encoders will write,
/// progressive and interlaced, at sizes that are and are not a whole number of macroblocks, and with
/// alpha at both depths. What these tests add is what that comparison cannot reach: a codeword that
/// tells the two halves of a combination codebook apart, the codebook adaptations exercised where a
/// natural picture would not go, the two block arrangements that differ only for 4:4:4 chroma, and
/// the refusals, which by definition no valid stream contains.
/// </remarks>
[TestFixture]
public class ProResVideoDecoderTests {

  // The DC of a block, written with the codebooks of RDD 36:2022, Tables 9 and 21. See the tests
  // themselves for the derivation of each codeword.
  private const string _FIRST_DC_ZERO = "1 00000";
  private const string _FIRST_DC_EIGHT = "1 10000";
  private const string _DIFFERENCE_ZERO_FROM_THREE = "1 000";
  private const string _DIFFERENCE_ZERO_FROM_ZERO = "1";
  private const string _DIFFERENCE_MINUS_EIGHT_FROM_POSITIVE = "0 1 0111";
  private const string _DIFFERENCE_MINUS_EIGHT_FROM_NEGATIVE = "0 1 1000";

  // ============================================================================================
  // A whole frame, end to end
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ABlockWithOnlyADcCoefficientComesOutFlat() {
    // Every block's DC is 8 and there are no AC coefficients at all. RDD 36:2022, 7.3 makes the
    // dequantised coefficient 8 * 4 * 1 / 8 = 4 — the weight matrix defaulting to 4 and the
    // quantisation scale being 1 — and 7.4 collapses to F(0,0)/8 for a block with nothing else in
    // it, so every reconstructed value is 0.5. 7.5.1 then puts that at 2 * (0.5 + 256) = 513.
    var luma = new ProResTestStream()
      .Code(_FIRST_DC_EIGHT)
      .Code(_DIFFERENCE_ZERO_FROM_THREE)
      .Code(_DIFFERENCE_ZERO_FROM_ZERO)
      .Code(_DIFFERENCE_ZERO_FROM_ZERO)
      .End();

    var chroma = new ProResTestStream()
      .Code(_FIRST_DC_ZERO)
      .Code(_DIFFERENCE_ZERO_FROM_THREE)
      .End();

    var planes = _Decode(new(), luma, chroma, chroma);

    Assert.Multiple(() => {
      Assert.That(planes.BitDepth, Is.EqualTo(10), "the 4:2:2 profiles are reconstructed at ten bits");
      Assert.That(planes.Luma, Is.All.EqualTo(513));
      Assert.That(planes.Cb, Is.All.EqualTo(512), "a DC of nothing is mid-range, which is achromatic");
      Assert.That(planes.Cr, Is.All.EqualTo(512));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheFourLumaBlocksOfAMacroblockRunLeftToRightThenTopToBottom() {
    // RDD 36:2022, Figure 6. The four blocks are given DCs of 0, −8, −16 and −24, which come out as
    // 512, 511, 510 and 509, so each block's value names it.
    var planes = _Decode(new(), _DescendingDcs(4), _ChromaAtZero(), _ChromaAtZero());

    Assert.Multiple(() => {
      Assert.That(_At(planes.Luma, 16, 0, 0), Is.EqualTo(512), "block 0 is top left");
      Assert.That(_At(planes.Luma, 16, 8, 0), Is.EqualTo(511), "block 1 is top right");
      Assert.That(_At(planes.Luma, 16, 0, 8), Is.EqualTo(510), "block 2 is bottom left");
      Assert.That(_At(planes.Luma, 16, 8, 8), Is.EqualTo(509), "block 3 is bottom right");
    });
  }

  [Test]
  [Category("Unit")]
  public void TheFourChromaBlocksOfA444MacroblockRunTopToBottomThenLeftToRight() {
    // RDD 36:2022, Figure 8, and the specification prints a note of its own saying this differs from
    // the luma arrangement of Figure 6. Reading the chroma the way the luma is read transposes the
    // colour of every macroblock in quarters — invisible on anything smooth, which is exactly why it
    // is worth a test rather than a look.
    //
    // The values are four times those of the 4:2:2 tests because a 4:4:4 frame is reconstructed at
    // twelve bits rather than ten, and RDD 36:2022, 7.5.1 scales the same reconstructed value onto
    // whichever depth the decoder asked for.
    var options = new ProResTestStream.Options { Version = 1, ChromaFormat = 3 };
    var planes = _Decode(options, _DescendingDcs(4), _DescendingDcs(4), _DescendingDcs(4));

    Assert.Multiple(() => {
      Assert.That(planes.BitDepth, Is.EqualTo(12), "the 4:4:4 profiles are reconstructed at twelve bits");
      Assert.That(planes.ChromaWidth, Is.EqualTo(16), "4:4:4 chroma is not subsampled");
      Assert.That(_At(planes.Cb, 16, 0, 0), Is.EqualTo(2048), "block 0 is top left");
      Assert.That(_At(planes.Cb, 16, 0, 8), Is.EqualTo(2044), "block 1 is below it, not beside it");
      Assert.That(_At(planes.Cb, 16, 8, 0), Is.EqualTo(2040), "block 2 is top right");
      Assert.That(_At(planes.Cb, 16, 8, 8), Is.EqualTo(2036), "block 3 is bottom right");
    });
  }

  [Test]
  [Category("Unit")]
  public void TheTwoChromaBlocksOfA422MacroblockAreOneAboveTheOther() {
    // RDD 36:2022, Figure 7. The chroma of a 4:2:2 macroblock is eight samples wide and sixteen
    // tall, so its two blocks stack rather than sit side by side.
    var planes = _Decode(new(), _DescendingDcs(4), _DescendingDcs(2), _DescendingDcs(2));

    Assert.Multiple(() => {
      Assert.That(planes.ChromaWidth, Is.EqualTo(8), "4:2:2 chroma is half as wide");
      Assert.That(_At(planes.Cb, 8, 0, 0), Is.EqualTo(512));
      Assert.That(_At(planes.Cb, 8, 0, 8), Is.EqualTo(511));
    });
  }

  [Test]
  [Category("Unit")]
  public void AnAcCoefficientLandsAtTheFrequencyAndInTheBlockTheSliceScanNames() {
    // One AC coefficient, and everything about where it ends up is a claim worth checking.
    //
    // The run of zeroes before it is nothing, coded with the codebook Table 10 gives for the initial
    // previous run of 4 — the exponential-Golomb code of order 0, whose symbol 0 is a single '1'.
    // Its level symbol is 15, coded with the codebook Table 11 gives for the initial previous symbol
    // of 1, RICE_EXP_COMBO_CODE(1, 0, 1): 15 is past the two symbols that code's Golomb-Rice half
    // covers, so the codeword is the two '0' bits that mark the exponential half followed by the
    // order-1 exponential-Golomb codeword for 13, which is '00' then '1111'. The sign bit is 0.
    // That makes the coefficient +16.
    //
    // It is the first entry after the four DCs, which by the slice scanning of 7.2.1 is frequency
    // index 1 of block 0; by the block scan of Figure 4 frequency 1 is the raster position (u=1,
    // v=0), the first horizontal frequency. So block 0 alone gets a horizontal ripple and the other
    // three stay flat.
    var luma = new ProResTestStream()
      .Code(_FIRST_DC_EIGHT)
      .Code(_DIFFERENCE_ZERO_FROM_THREE)
      .Code(_DIFFERENCE_ZERO_FROM_ZERO)
      .Code(_DIFFERENCE_ZERO_FROM_ZERO)
      .Code("1")          // run of 0
      .Code("00 00 1111") // abs_level_minus_1 of 15
      .Code("0")          // positive
      .End();

    var planes = _Decode(new(), luma, _ChromaAtZero(), _ChromaAtZero());
    var top = Enumerable.Range(0, 16).Select(x => (int)_At(planes.Luma, 16, x, 0)).ToArray();
    var bottom = Enumerable.Range(0, 16).Select(x => (int)_At(planes.Luma, 16, x, 8)).ToArray();

    Assert.Multiple(() => {
      Assert.That(top[..8], Is.EqualTo(new[] { 516, 515, 515, 514, 512, 511, 511, 510 }),
        "block 0 carries half a cycle of the first horizontal frequency");
      Assert.That(top[8..], Is.All.EqualTo(513), "block 1 has only its DC");
      Assert.That(bottom, Is.All.EqualTo(513), "blocks 2 and 3 have only their DCs");
    });
  }

  // ============================================================================================
  // The codebooks
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ACodewordShorterThanTheSwitchPointIsReadAsGolombRice() {
    // The codebook for the first DC is the exponential-Golomb code of order 5, which RDD 36:2022,
    // 7.1.1.1 notes is the combination code with lastRiceQ 0, Rice order 5 and exponential order 6.
    // A code level of 0 is therefore its Golomb-Rice half, and the five bits after the separator are
    // the symbol itself — here 16, which the signed mapping of Table 8 makes +8.
    var planes = _Decode(new(), _DcOnlyLuma("1 10000"), _ChromaAtZero(), _ChromaAtZero());

    Assert.That(_At(planes.Luma, 16, 0, 0), Is.EqualTo(513), "a DC of 8 reconstructs as 513");
  }

  [Test]
  [Category("Unit")]
  public void ACodewordPastTheSwitchPointIsReadAsExponentialGolomb() {
    // The same codebook, one level further on. A level of 1 puts the codeword in the exponential
    // half: the first '0' is the marker that says so, and what follows is an order-6
    // exponential-Golomb codeword — a separator and six suffix bits, read together as the value
    // 2^6 + suffix. The symbol is that value less 2^6, plus the 32 symbols the Golomb-Rice half
    // already covers. With six suffix bits of 0 that is 32, which the signed mapping of Table 8
    // makes +16 — twice the DC of the test above, and so twice its distance from mid-range.
    //
    // The suffix width is the part worth pinning down: read as seven bits the codeword swallows one
    // bit too many and every symbol after it in the component is a different one.
    var planes = _Decode(new(), _DcOnlyLuma("0 1 000000"), _ChromaAtZero(), _ChromaAtZero());

    Assert.That(_At(planes.Luma, 16, 0, 0), Is.EqualTo(514), "a DC of 16 reconstructs as 514");
  }

  [Test]
  [Category("Unit")]
  public void ADcDifferenceKeepsTheSignOfTheOneBeforeIt() {
    // RDD 36:2022, 7.1.1.3: a difference is negated when the previous difference was negative, so a
    // run of falling DC values costs the same as a run of rising ones. The four blocks below step
    // down by 8 each time, and only the first of the three differences is coded with a negative
    // symbol — the other two carry the symbol for +8 and are negated on the way in.
    //
    // Without the carry the DCs would be 0, −8, 0, −8 and the four blocks would read 512, 511, 512,
    // 511 rather than stepping.
    var planes = _Decode(new(), _DescendingDcs(4), _ChromaAtZero(), _ChromaAtZero());
    var blocks = new[] {
      _At(planes.Luma, 16, 0, 0), _At(planes.Luma, 16, 8, 0),
      _At(planes.Luma, 16, 0, 8), _At(planes.Luma, 16, 8, 8),
    };

    Assert.That(blocks, Is.EqualTo(new[] { 512, 511, 510, 509 }));
  }

  // ============================================================================================
  // The layout the picture header implies
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void SlicesAreTakenAtTheDesiredSizeUntilTheRowRunsShortAndThenHalve() {
    // RDD 36:2022, 4 works this exact case through: a picture 720 pixels wide is 45 macroblocks, and
    // with a desired slice size of 8 the row comes out as five slices of 8, then one of 4, then one
    // of 1.
    Assert.That(ProResSliceLayout.Build(45, 3), Is.EqualTo(new[] { 8, 8, 8, 8, 8, 4, 1 }));
  }

  [Test]
  [Category("Unit")]
  public void ARowThatDividesEvenlyIsAllFullSlices() {
    Assert.That(ProResSliceLayout.Build(16, 3), Is.EqualTo(new[] { 8, 8 }));
  }

  [Test]
  [Category("Unit")]
  public void AQuantisationIndexPastAHundredAndTwentyEightStepsByFour() {
    // RDD 36:2022, Table 15. The index is the scale factor up to 128 and then steps by four, so the
    // 224 permitted indices reach a scale factor of 512.
    Assert.Multiple(() => {
      Assert.That(ProResPictureDecoder.QuantisationScale(1), Is.EqualTo(1));
      Assert.That(ProResPictureDecoder.QuantisationScale(128), Is.EqualTo(128));
      Assert.That(ProResPictureDecoder.QuantisationScale(129), Is.EqualTo(132));
      Assert.That(ProResPictureDecoder.QuantisationScale(224), Is.EqualTo(512));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheTwoBlockScansAreDifferentPermutationsOfTheSameSixtyFourFrequencies() {
    // RDD 36:2022, Figures 4 and 5. Both have to be permutations or a coefficient would be lost or
    // written twice, and they have to differ or the interlace mode would not matter.
    Assert.Multiple(() => {
      Assert.That(ProResScan.Progressive.OrderBy(n => n), Is.EqualTo(Enumerable.Range(0, 64)));
      Assert.That(ProResScan.Interlaced.OrderBy(n => n), Is.EqualTo(Enumerable.Range(0, 64)));
      Assert.That(ProResScan.Interlaced, Is.Not.EqualTo(ProResScan.Progressive));
      Assert.That(ProResScan.Progressive[0], Is.EqualTo(0), "the DC is the first frequency in both");
      Assert.That(ProResScan.Interlaced[0], Is.EqualTo(0));
    });
  }

  // ============================================================================================
  // The quantisation weights
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFrameThatLoadsNoChromaMatrixQuantisesItsChromaWithTheLumaOne() {
    // RDD 36:2022, 7.3. Not the same as falling back on the default: a frame that loads a luma
    // matrix and no chroma one uses the loaded matrix for both. With a luma weight of 8 rather than
    // the default 4, a chroma DC of 8 dequantises to twice what it otherwise would.
    var matrix = new byte[64];
    matrix.AsSpan().Fill(8);

    var options = new ProResTestStream.Options { LumaMatrix = matrix };
    var planes = _Decode(options, _DcOnlyLuma(_FIRST_DC_ZERO), _DcOnlyChroma(_FIRST_DC_EIGHT), _ChromaAtZero());

    // 8 * 8 * 1 / 8 = 8 dequantised, 1.0 reconstructed, 2 * (1 + 256) = 514.
    Assert.That(_At(planes.Cb, 8, 0, 0), Is.EqualTo(514));
  }

  [Test]
  [Category("Unit")]
  public void AFrameThatLoadsNoMatrixAtAllUsesAFlatFour() {
    // RDD 36:2022, 7.3. Every test above relies on this, so it is worth stating on its own.
    var frame = ProResTestStream.Frame(new(), _DcOnlyLuma(_FIRST_DC_ZERO), _ChromaAtZero(), _ChromaAtZero());
    var header = ProResFrameHeader.Parse(frame.AsSpan(8));

    Assert.Multiple(() => {
      Assert.That(header.LumaMatrix, Is.All.EqualTo(4));
      Assert.That(header.ChromaMatrix, Is.All.EqualTo(4));
    });
  }

  // ============================================================================================
  // What refuses
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ABitstreamVersionThisSpecificationDoesNotDescribeIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => _Decode(new() { Version = 2 }, _DcOnlyLuma(_FIRST_DC_ZERO), _ChromaAtZero(), _ChromaAtZero()));

    Assert.That(failure!.Message, Does.Contain("bitstream version 2"));
  }

  [Test]
  [Category("Unit")]
  [TestCase(0)]
  [TestCase(1)]
  public void AReservedChromaFormatIsRefused(int chromaFormat) {
    var options = new ProResTestStream.Options { Version = 1, ChromaFormat = chromaFormat };
    var failure = Assert.Throws<NotSupportedException>(() => _Decode(options, _DcOnlyLuma(_FIRST_DC_ZERO), _ChromaAtZero(), _ChromaAtZero()));

    Assert.That(failure!.Message, Does.Contain("chroma_format"));
  }

  [Test]
  [Category("Unit")]
  public void AReservedInterlaceModeIsRefused() {
    var options = new ProResTestStream.Options { InterlaceMode = 3 };
    var failure = Assert.Throws<NotSupportedException>(() => _Decode(options, _DcOnlyLuma(_FIRST_DC_ZERO), _ChromaAtZero(), _ChromaAtZero()));

    Assert.That(failure!.Message, Does.Contain("interlace_mode 3"));
  }

  [Test]
  [Category("Unit")]
  public void AVersionZeroFrameThatStatesFourFourFourIsRefused() {
    // RDD 36:2022, 6.4 fixes chroma_format at 2 and alpha_channel_type at 0 for version 0, so a
    // version 0 frame saying otherwise is describing itself with syntax its own version lacks.
    var options = new ProResTestStream.Options { Version = 0, ChromaFormat = 3 };
    var failure = Assert.Throws<InvalidDataException>(() => _Decode(options, _DcOnlyLuma(_FIRST_DC_ZERO), _ChromaAtZero(), _ChromaAtZero()));

    Assert.That(failure!.Message, Does.Contain("version 0"));
  }

  [Test]
  [Category("Unit")]
  [TestCase(0)]
  [TestCase(225)]
  public void AQuantisationIndexOutsideThePermittedRangeIsRefused(int index) {
    // A zero index would make every dequantised coefficient of the slice zero — a flat mid-grey that
    // looks like a decode rather than a refusal.
    var options = new ProResTestStream.Options { QuantizationIndex = index };
    var failure = Assert.Throws<InvalidDataException>(() => _Decode(options, _DcOnlyLuma(_FIRST_DC_ZERO), _ChromaAtZero(), _ChromaAtZero()));

    Assert.That(failure!.Message, Does.Contain("quantization_index"));
  }

  [Test]
  [Category("Unit")]
  public void AReservedAlphaChannelTypeIsRefused() {
    // The alpha data are the tail of every slice, so a type whose code is unknown cannot be read and
    // cannot be stepped over either.
    var options = new ProResTestStream.Options { Version = 1, AlphaChannelType = 3 };
    var failure = Assert.Throws<NotSupportedException>(() => _Decode(options, _DcOnlyLuma(_FIRST_DC_ZERO), _ChromaAtZero(), _ChromaAtZero()));

    Assert.That(failure!.Message, Does.Contain("alpha_channel_type 3"));
  }

  [Test]
  [Category("Unit")]
  public void APacketThatIsNotACompressedFrameIsRefused() {
    var options = new ProResTestStream.Options { Identifier = "mdat" };
    var failure = Assert.Throws<InvalidDataException>(() => _Decode(options, _DcOnlyLuma(_FIRST_DC_ZERO), _ChromaAtZero(), _ChromaAtZero()));

    Assert.That(failure!.Message, Does.Contain("icpf"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameClaimingMoreBytesThanItsPacketHoldsIsRefused() {
    var options = new ProResTestStream.Options { StatedFrameSize = 100000 };
    var failure = Assert.Throws<InvalidDataException>(() => _Decode(options, _DcOnlyLuma(_FIRST_DC_ZERO), _ChromaAtZero(), _ChromaAtZero()));

    Assert.That(failure!.Message, Does.Contain("states a size"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameOfADifferentSizeToTheStreamItIsInIsRefused() {
    var frame = ProResTestStream.Frame(new(), _DcOnlyLuma(_FIRST_DC_ZERO), _ChromaAtZero(), _ChromaAtZero());
    var decoder = ProResVideoDecoder.Create(ProResTestStream.Stream(32, 32));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, frame), out _));
    Assert.That(failure!.Message, Does.Contain("16x16"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoPictureSizeIsRefused() {
    var stream = ProResTestStream.Stream(0, 0);

    Assert.Throws<InvalidDataException>(() => ProResVideoDecoder.Create(stream));
  }

  // ============================================================================================
  // Which streams this codec answers to
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase("apco")]
  [TestCase("apcs")]
  [TestCase("apcn")]
  [TestCase("apch")]
  [TestCase("ap4h")]
  [TestCase("ap4x")]
  public void EveryProfileIsAccepted(string code) {
    Assert.That(ProResVideoDecoder.Accepts(ProResTestStream.Stream(codec: code)), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void AStreamOfAnotherCodecIsNotAccepted() {
    Assert.That(ProResVideoDecoder.Accepts(ProResTestStream.Stream(codec: "avc1")), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void AnAudioStreamIsNotAcceptedHoweverItIsNamed() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("apcn") };

    Assert.That(ProResVideoDecoder.Accepts(stream), Is.False);
  }

  // ============================================================================================
  // The colour conversion
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheFrameHeaderChoosesTheColourMatrixWhereItNamesOne() {
    // RDD 36:2022, Table 6. The BT.601 row is the one every other colour conversion in this library
    // uses, which is a useful check that the general form these were computed with is right.
    Assert.Multiple(() => {
      Assert.That(ProResColorConversion.Matrix(6, 1080), Is.EqualTo((409, 100, 208, 516)), "BT.601");
      Assert.That(ProResColorConversion.Matrix(1, 480), Is.EqualTo((459, 55, 136, 541)), "BT.709");
      Assert.That(ProResColorConversion.Matrix(9, 480), Is.EqualTo((430, 48, 167, 548)), "BT.2020");
    });
  }

  [Test]
  [Category("Unit")]
  public void AnUnlabelledFrameTakesItsMatrixFromItsHeight() {
    Assert.Multiple(() => {
      Assert.That(ProResColorConversion.Matrix(2, 576), Is.EqualTo((409, 100, 208, 516)), "standard definition is BT.601");
      Assert.That(ProResColorConversion.Matrix(2, 720), Is.EqualTo((459, 55, 136, 541)), "high definition is BT.709");
    });
  }

  [Test]
  [Category("Unit")]
  public void AFrameWithoutAlphaComesBackAsPackedColourWithNone() {
    var frame = ProResTestStream.Frame(new(), _DcOnlyLuma(_FIRST_DC_ZERO), _ChromaAtZero(), _ChromaAtZero());
    var decoder = ProResVideoDecoder.Create(ProResTestStream.Stream());

    Assert.That(decoder.TryDecode(new(0, frame), out var picture), Is.True);
    Assert.Multiple(() => {
      Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(picture.Width, Is.EqualTo(16));
      Assert.That(picture.Height, Is.EqualTo(16));
      Assert.That(picture.PixelData!.Length, Is.EqualTo(16 * 16 * 3));
    });
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static ProResPlanes _Decode(ProResTestStream.Options options, params byte[][] components) {
    var frame = ProResTestStream.Frame(options, components);
    var decoder = ProResVideoDecoder.Create(ProResTestStream.Stream(options.Width, options.Height));

    return decoder.DecodePlanes(frame, out _);
  }

  /// <summary>Four luma blocks whose DCs are all the value the first codeword names.</summary>
  private static byte[] _DcOnlyLuma(string firstDc) => new ProResTestStream()
    .Code(firstDc)
    .Code(_DIFFERENCE_ZERO_FROM_THREE)
    .Code(_DIFFERENCE_ZERO_FROM_ZERO)
    .Code(_DIFFERENCE_ZERO_FROM_ZERO)
    .End();

  /// <summary>Two chroma blocks whose DCs are both the value the first codeword names.</summary>
  private static byte[] _DcOnlyChroma(string firstDc) => new ProResTestStream()
    .Code(firstDc)
    .Code(_DIFFERENCE_ZERO_FROM_THREE)
    .End();

  private static byte[] _ChromaAtZero() => _DcOnlyChroma(_FIRST_DC_ZERO);

  /// <summary>
  /// A component whose blocks step down by 8 in DC, so that each block's value names it.
  /// </summary>
  /// <remarks>
  /// The first difference is coded as the symbol for −8. Every one after it is coded as the symbol
  /// for <i>+8</i> and arrives as −8, because RDD 36:2022, 7.1.1.3 negates a difference when the one
  /// before it was negative.
  /// </remarks>
  private static byte[] _DescendingDcs(int blocks) {
    var stream = new ProResTestStream().Code(_FIRST_DC_ZERO).Code(_DIFFERENCE_MINUS_EIGHT_FROM_POSITIVE);
    for (var i = 2; i < blocks; ++i)
      stream.Code(_DIFFERENCE_MINUS_EIGHT_FROM_NEGATIVE);

    return stream.End();
  }

  private static int _At(ushort[] plane, int width, int x, int y) => plane[y * width + x];
}
