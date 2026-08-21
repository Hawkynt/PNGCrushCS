using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Theora.Tests;

/// <summary>
/// The Theora decoder, on streams built here.
/// </summary>
/// <remarks>
/// The decoder's arithmetic was checked by decoding twenty-four streams here and in ffmpeg and
/// comparing the sample planes frame by frame, sample by sample; what these tests add is what that
/// comparison cannot reach. Most of it is the refusals, which by definition no valid stream
/// produces. The rest is a handful of frames whose expected samples can be worked out from the
/// specification rather than recorded from a run — so that where a number here disagrees with the
/// decoder, the arithmetic in the comment says which of the two is wrong.
/// </remarks>
[TestFixture]
public sealed class TheoraVideoDecoderTests {

  /// <summary>The number of super blocks in the 16x16 4:2:0 frame most of these tests use.</summary>
  /// <remarks>
  /// One for the luma plane and one for each chroma plane. A super block never spans planes, and a
  /// 16x16 frame is two luma blocks and one chroma block across — fewer than the four a whole super
  /// block holds, so all three are partial and all three are still counted.
  /// </remarks>
  private const int _SUPER_BLOCKS = 3;

  // ============================================================================================
  // Identity
  // ============================================================================================

  [TestCase("theora")]
  [TestCase("THEORA")]
  [TestCase("V_THEORA")]
  [TestCase("v_theora")]
  [Category("Unit")]
  public void TheCodecTakesTheNamesItsContainersGiveIt(string codecId)
    => Assert.That(TheoraVideoDecoder.Accepts(_Stream(codecId: codecId)), Is.True);

  [Test]
  [Category("Unit")]
  public void TheCodecLeavesOtherStreamsAlone() {
    Assert.That(TheoraVideoDecoder.Accepts(_Stream(codecId: "V_VP8")), Is.False);
    Assert.That(TheoraVideoDecoder.Accepts(_Stream(codecId: "vorbis")), Is.False);
    Assert.That(TheoraVideoDecoder.Accepts(_Stream(code: "MJPG")), Is.False);

    // The same name on a sound track is still not a picture.
    Assert.That(TheoraVideoDecoder.Accepts(_Stream(codecId: "theora", kind: MediaStreamKind.Audio)), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsNamedForTheStandardItReads()
    => Assert.That(TheoraVideoDecoder.CodecName, Does.Contain("Theora"));

  [Test]
  [Category("Unit")]
  public void TheRegistryFindsItForAnOggOrMatroskaStream() {
    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain(TheoraVideoDecoder.CodecName));
    Assert.That(VideoFormatRegistry.CanDecode(_Stream(codecId: "theora")), Is.True);
    Assert.That(VideoFormatRegistry.CanDecode(_Stream(codecId: "V_THEORA")), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void CreatingADecoderForNothingIsRefused() {
    Assert.Throws<ArgumentNullException>(() => TheoraVideoDecoder.Create(null!));
    Assert.Throws<ArgumentNullException>(() => TheoraVideoDecoder.Accepts(null!));
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithoutItsSetupHeadersIsRefusedByName() {
    // The quantisation matrices and the eighty Huffman codes every coefficient is read through are
    // only in the headers. A stream whose container did not carry them cannot be decoded at all, and
    // saying so when the decoder is asked for names the stream rather than the first frame.
    var failure = Assert.Throws<NotSupportedException>(() => TheoraVideoDecoder.Create(_Stream(codecId: "theora")));

    Assert.That(failure!.Message, Does.Contain("private data").And.Contain("setup"));
  }

  // ============================================================================================
  // Reconstruction
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnIntraFrameOfEmptyBlocksIsMidGrey() {
    // Every block is INTRA, whose predictor is the constant 128, and every one is ended at its first
    // coefficient — so the residual is zero and the samples are 128 in all three planes. In RGB that
    // is (298 * (128 - 16) + 128) >> 8 = 130 in each channel.
    var frame = _DecodeOne(TheoraTestStream.EmptyIntraFrame());

    Assert.That(frame.Width, Is.EqualTo(16));
    Assert.That(frame.Height, Is.EqualTo(16));
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData.Length, Is.EqualTo(16 * 16 * 3));
    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
  }

  [Test]
  [Category("Unit")]
  public void ADirectCurrentCoefficientReachesEveryBlockThroughPrediction() {
    // Only the first block in coded order carries a coefficient, and DC prediction carries it to the
    // other three luma blocks: the second predicts from its left neighbour, the third from the block
    // below it, and the fourth from the weighted sum (29 - 26 + 29) / 32, which is one.
    //
    // A block whose coefficient count is under two takes the direct-current shortcut through the
    // transform, so the residual is (1 * 64 + 15) >> 5 = 2 everywhere in it. The luma is 130 and the
    // chroma untouched, which is (298 * (130 - 16) + 128) >> 8 = 133.
    var frame = _DecodeOne(TheoraTestStream.IntraFrameWithDirectCurrent(9));

    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 133 }));
  }

  [Test]
  [Category("Unit")]
  public void ANegativeDirectCurrentCoefficientDarkensTheBlock() {
    // Token 10 is −1, so the residual is (−64 + 15) >> 5. The shift is arithmetic and rounds towards
    // negative infinity, so −49 >> 5 is −2 rather than −1, and the luma is 126:
    // (298 * (126 - 16) + 128) >> 8 = 128.
    var frame = _DecodeOne(TheoraTestStream.IntraFrameWithDirectCurrent(10));

    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 128 }));
  }

  [Test]
  [Category("Unit")]
  public void TheQuantiserScalesTheResidue() {
    // The same coefficient against a base matrix of 32 rather than 16. A scale of 100 and a base
    // matrix entry of 32 give a quantiser of 32 * 100 / 100 * 4 = 128, so the residual doubles to
    // (128 + 15) >> 5 = 4 and the luma is 132: (298 * (132 - 16) + 128) >> 8 = 135.
    var options = new TheoraTestOptions { BaseMatrixValue = 32 };
    var frame = _DecodeOne(TheoraTestStream.IntraFrameWithDirectCurrent(9), options);

    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 135 }));
  }

  [TestCase(0)]
  [TestCase(2)]
  [TestCase(3)]
  [Category("Unit")]
  public void EveryPixelFormatDecodes(int pixelFormat) {
    // The three subsamplings differ in how many chroma blocks a macro block covers and therefore in
    // the whole block geometry — the number of super blocks, the coded order, and which chroma block
    // a macro block's motion vector reaches. An empty intra frame comes out mid-grey in all three.
    var options = new TheoraTestOptions { PixelFormat = pixelFormat };
    var frame = _DecodeOne(TheoraTestStream.EmptyIntraFrame(), options);

    Assert.That(frame.PixelData.Distinct().ToArray(), Is.EqualTo(new byte[] { 130 }));
  }

  [Test]
  [Category("Unit")]
  public void ThePictureIsCroppedOutOfTheCodedFrame() {
    // Theora codes a whole number of macro blocks and carries a smaller picture region inside it.
    // The samples outside the picture are real coded samples that later frames predict from, so they
    // are decoded like any other and dropped only when a picture is handed out.
    var options = new TheoraTestOptions {
      MacroBlocksWide = 2,
      MacroBlocksHigh = 2,
      PictureWidth = 20,
      PictureHeight = 12,
      PictureX = 4,
      PictureY = 8,
    };

    var frame = _DecodeOne(TheoraTestStream.EmptyIntraFrame(), options);

    Assert.That(frame.Width, Is.EqualTo(20));
    Assert.That(frame.Height, Is.EqualTo(12));
    Assert.That(frame.PixelData.Length, Is.EqualTo(20 * 12 * 3));
  }

  [Test]
  [Category("Unit")]
  public void TheLoopFilterLeavesAFlatPictureAlone() {
    // Every difference the filter measures across a block edge is zero, so every adjustment it
    // computes is zero. A filter that moved a flat picture would be moving every picture.
    var filtered = _DecodeOne(TheoraTestStream.EmptyIntraFrame(), new() { LoopFilterLimit = 63, MacroBlocksWide = 3, MacroBlocksHigh = 2 });
    var unfiltered = _DecodeOne(TheoraTestStream.EmptyIntraFrame(), new() { MacroBlocksWide = 3, MacroBlocksHigh = 2 });

    Assert.That(filtered.PixelData, Is.EqualTo(unfiltered.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void AnInterFrameWithNothingCodedRepeatsThePreviousPicture() {
    // Not a fallback: an inter frame that codes no block reconstructs by copying every block from
    // the previous reference, which is what the format means by it.
    var decoder = _Decoder();
    var first = _Decode(decoder, TheoraTestStream.IntraFrameWithDirectCurrent(9));
    var second = _Decode(decoder, TheoraTestStream.EmptyInterFrame(_SUPER_BLOCKS));

    Assert.That(second.PixelData, Is.EqualTo(first.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void AZeroLengthPacketIsADuplicateFrame() {
    // Section 7.11 defines a zero-length packet as an inter frame with no coded blocks, which is to
    // say a duplicate. It arrives at that picture by the ordinary reconstruction path rather than by
    // being handed back a copy of the last one.
    var decoder = _Decoder();
    var first = _Decode(decoder, TheoraTestStream.IntraFrameWithDirectCurrent(9));
    var second = _Decode(decoder, []);

    Assert.That(second.PixelData, Is.EqualTo(first.PixelData));
  }

  // ============================================================================================
  // Refusals in the headers
  // ============================================================================================

  [TestCase(2, 2)]
  [TestCase(3, 1)]
  [TestCase(4, 2)]
  [Category("Unit")]
  public void ABitstreamVersionOtherThanThreeTwoIsRefusedByName(int major, int minor) {
    var failure = Assert.Throws<NotSupportedException>(
      () => _Decoder(new() { VersionMajor = major, VersionMinor = minor }));

    Assert.That(failure!.Message, Does.Contain($"version {major}.{minor}").And.Contain("3.2"));
  }

  [Test]
  [Category("Unit")]
  public void TheReservedPixelFormatIsRefusedByName() {
    var failure = Assert.Throws<NotSupportedException>(() => _Decoder(new() { PixelFormat = 1 }));

    Assert.That(failure!.Message, Does.Contain("pixel format 1").And.Contain("6.4"));
  }

  [Test]
  [Category("Unit")]
  public void AReservedBitThatIsSetIsRefusedByName() {
    // The specification requires a decoder to refuse rather than ignore these: they are place
    // holders for features a future version may add without changing the version number, so a
    // decoder that read past them would silently misread the first stream to use one.
    var failure = Assert.Throws<NotSupportedException>(() => _Decoder(new() { ReservedBits = 1 }));

    Assert.That(failure!.Message, Does.Contain("reserved"));
  }

  [Test]
  [Category("Unit")]
  public void HeaderPacketsWithoutTheMagicAreRefused() {
    var priv = TheoraTestStream.CodecPrivateData();

    // The 'h' of 'theora' in the identification header, which sits after the count byte and two
    // lacing bytes.
    priv[4] = (byte)'x';

    Assert.That(Assert.Throws<InvalidDataException>(() => _DecoderFrom(priv))!.Message, Does.Contain("theora"));
  }

  [Test]
  [Category("Unit")]
  public void PrivateDataStatingFewerThanThreePacketsIsRefused() {
    var priv = TheoraTestStream.CodecPrivateData();
    priv[0] = 0;

    Assert.That(Assert.Throws<InvalidDataException>(() => _DecoderFrom(priv))!.Message,
      Does.Contain("three header packets"));
  }

  [Test]
  [Category("Unit")]
  public void PrivateDataThatEndsInsideItsPacketsIsRefused() {
    // Cut off half way through the setup header. Reads past the end of a packet come back as zeroes
    // and a zero is a perfectly good tree node, so the Huffman table reader would descend for ever
    // — which is exactly what the depth bound the specification asks for is there to stop, and what
    // this refusal names.
    var priv = TheoraTestStream.CodecPrivateData();

    Assert.That(Assert.Throws<InvalidDataException>(() => _DecoderFrom(priv[..(priv.Length / 2)]))!.Message,
      Does.Contain("setup header"));
  }

  [Test]
  [Category("Unit")]
  public void AHuffmanTableWithMoreEntriesThanTheFormatAllowsIsRefusedByName() {
    var failure = Assert.Throws<InvalidDataException>(() => _Decoder(new() { OversizedHuffmanTable = true }));

    Assert.That(failure!.Message, Does.Contain("Huffman table").And.Contain("32 entries"));
  }

  [Test]
  [Category("Unit")]
  public void QuantRangesThatOverrunTheScaleAreRefusedByName() {
    var failure = Assert.Throws<InvalidDataException>(() => _Decoder(new() { OverlongQuantRanges = true }));

    Assert.That(failure!.Message, Does.Contain("quant ranges").And.Contain("63"));
  }

  // ============================================================================================
  // Refusals in a frame
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AHeaderPacketOfferedAsAFrameIsRefusedByName() {
    var decoder = _Decoder();

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(decoder, TheoraTestStream.CommentHeader()))!.Message,
      Does.Contain("header rather than a frame"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamThatBeginsWithAnInterFrameIsRefusedByName() {
    // An inter frame is a difference from reference frames that do not exist yet, and inventing them
    // is how a decoder produces a plausible wrong picture rather than an error.
    var decoder = _Decoder();

    Assert.That(
      Assert.Throws<InvalidDataException>(() => _Decode(decoder, TheoraTestStream.EmptyInterFrame(_SUPER_BLOCKS)))!.Message,
      Does.Contain("inter frame").And.Contain("intra frame"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamThatBeginsWithADuplicateFrameIsRefusedByName() {
    var decoder = _Decoder();

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(decoder, []))!.Message,
      Does.Contain("zero-length").And.Contain("does not exist yet"));
  }

  [Test]
  [Category("Unit")]
  public void AnIntraFrameWithAReservedBitSetIsRefusedByName() {
    var frame = TheoraTestStream.EmptyIntraFrame();

    // The three reserved bits sit after the frame type and the quantisation index, which is bits 2
    // to 7 of the first byte; the ninth bit of the packet is the first of them.
    frame[1] |= 0b0100_0000;

    var decoder = _Decoder();
    Assert.That(Assert.Throws<NotSupportedException>(() => _Decode(decoder, frame))!.Message,
      Does.Contain("reserved"));
  }

  [Test]
  [Category("Unit")]
  public void APacketThatEndsPartWayThroughItsFrameIsRefusedByName() {
    // Bits read past the end of a packet come back as zeroes, and zeroes are a perfectly valid
    // bitstream — so without the end-of-packet check a truncated packet becomes a picture with
    // nothing to say anything went wrong.
    var decoder = _Decoder();

    Assert.That(Assert.Throws<InvalidDataException>(() => _Decode(decoder, [0x00]))!.Message,
      Does.Contain("ends part way"));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Stream(
    string? codecId = null, string? code = null, MediaStreamKind kind = MediaStreamKind.Video,
    byte[]? privateData = null)
    => new() {
      Index = 0,
      Kind = kind,
      CodecId = codecId,
      Codec = code == null ? CodecTag.None : CodecTag.FromCharacters(code),
      CodecPrivateData = privateData ?? ReadOnlyMemory<byte>.Empty,
    };

  private static TheoraVideoDecoder _Decoder(TheoraTestOptions? options = null)
    => _DecoderFrom(TheoraTestStream.CodecPrivateData(options));

  private static TheoraVideoDecoder _DecoderFrom(byte[] privateData)
    => TheoraVideoDecoder.Create(_Stream(codecId: "theora", privateData: privateData));

  /// <summary>Decodes one packet through the public codec, as a container would.</summary>
  private static RawImage _Decode(TheoraVideoDecoder decoder, byte[] packet) {
    Assert.That(decoder.TryDecode(new(0, packet), out var picture), Is.True, "the frame was not shown");
    return picture;
  }

  private static RawImage _DecodeOne(byte[] packet, TheoraTestOptions? options = null)
    => _Decode(_Decoder(options), packet);
}
