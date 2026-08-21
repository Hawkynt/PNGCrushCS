using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi.Tests;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using static FileFormat.Codecs.Cinepak.Tests.CinepakTestStream;

namespace FileFormat.Codecs.Cinepak.Tests;

/// <summary>
/// The Cinepak decoder, on frames built here byte by byte.
/// </summary>
/// <remarks>
/// The arithmetic was settled against ffmpeg over three hundred frames, so what these tests add is
/// what that comparison cannot reach: the two codebook chunk forms no encoder here writes, the
/// geometry stated one pixel at a time, and the refusals, which by definition no valid stream
/// produces.
/// <para/>
/// The expected samples are worked out from the format rather than recorded from a run, and the
/// arithmetic is in the comment beside each. Where one of these numbers disagrees with the decoder,
/// the comment says which of the two is wrong.
/// </remarks>
[TestFixture]
public sealed class CinepakVideoDecoderTests {

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase("cvid")]
  [TestCase("CVID")]
  public void TheCodeIsTakenInEitherSpelling(string code)
    => Assert.That(CinepakVideoDecoder.Accepts(_Stream(code)), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsCodeIsNotTaken()
    => Assert.That(CinepakVideoDecoder.Accepts(_Stream("MSVC")), Is.False);

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsNotTaken() {
    var sound = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("cvid") };

    Assert.That(CinepakVideoDecoder.Accepts(sound), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Cinepak"));
    Assert.That(VideoFormatRegistry.CanDecode(_Stream("cvid")), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(_Stream("cvid")), Is.InstanceOf<CinepakVideoDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void NothingIsReadFromTheStreamDescription() {
    // Every Cinepak frame states its own size, so a container's copy is a copy. A stream carrying no
    // description at all still decodes.
    var bare = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("cvid") };
    var frame = _DecodeOne(bare, _OneV1Block(8, 4, 0, 0));

    Assert.That(frame.Width, Is.EqualTo(4));
    Assert.That(frame.Height, Is.EqualTo(4));
  }

  // ============================================================================================
  // The colour space, which is Cinepak's own
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ChrominanceIsSignedAndNotBiased() {
    // u = 32, v = -16, luminance 100.
    //   r = y + 2v      = 100 - 32 =  68
    //   g = y - u/2 - v = 100 - 16 + 16 = 100
    //   b = y + 2u      = 100 + 64 = 164
    // Read as biased by 128 instead, u would be -96 and v -144 and none of the three would land here.
    var frame = _DecodeOne(_Stream("cvid"), _OneV1Block(100, 100, 32, -16));

    Assert.That(_Pixel(frame, 0, 0), Is.EqualTo(new byte[] { 68, 100, 164 }));
  }

  [Test]
  [Category("Unit")]
  public void TheHalvingOfTheBlueDifferenceTruncatesTowardZero() {
    // u = -1, v = 0, luminance 100.
    //   g = y - (-1 / 2) - 0, and -1 / 2 truncated toward zero is 0, so g = 100.
    // A right shift instead gives -1, so g would be 101. That one level is the whole difference
    // between the two rules and it is invisible in any single frame.
    var frame = _DecodeOne(_Stream("cvid"), _OneV1Block(100, 100, -1, 0));

    Assert.That(_Pixel(frame, 0, 0), Is.EqualTo(new byte[] { 100, 100, 98 }));
  }

  [Test]
  [Category("Unit")]
  public void ColoursOutsideTheRangeAreClampedAndNotWrapped() {
    var high = _DecodeOne(_Stream("cvid"), _OneV1Block(250, 250, 100, 100));
    var low = _DecodeOne(_Stream("cvid"), _OneV1Block(10, 10, -100, -100));

    // r = 250 + 200 = 450 and b = 250 + 200 = 450, both past the top of the range.
    Assert.That(_Pixel(high, 0, 0)[0], Is.EqualTo(255));
    Assert.That(_Pixel(high, 0, 0)[2], Is.EqualTo(255));

    // r = 10 - 200 and b = 10 - 200, both below the bottom of it.
    Assert.That(_Pixel(low, 0, 0)[0], Is.EqualTo(0));
    Assert.That(_Pixel(low, 0, 0)[2], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AGreyCodebookEntryIsTheLuminanceThreeTimesOver() {
    var frame = _DecodeOne(
      _Stream("cvid"),
      Frame(0x00, 4, 4,
        Strip(StripIntra, 0, 0, 4, 4,
          Codebook(0x2600, GreyEntry(10, 20, 30, 40)),
          V1Vectors(0))));

    Assert.That(_Pixel(frame, 0, 0), Is.EqualTo(new byte[] { 10, 10, 10 }));
    Assert.That(_Pixel(frame, 3, 3), Is.EqualTo(new byte[] { 40, 40, 40 }));
  }

  // ============================================================================================
  // How a block is laid out
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AV1BlockStretchesEachOfItsFourSamplesOverATwoByTwoSquare() {
    var frame = _DecodeOne(
      _Stream("cvid"),
      Frame(0x00, 4, 4,
        Strip(StripIntra, 0, 0, 4, 4,
          Codebook(0x2200, Entry(10, 20, 30, 40, 0, 0)),
          V1Vectors(0))));

    Assert.That(_Greys(frame), Is.EqualTo(new byte[] {
      10, 10, 20, 20,
      10, 10, 20, 20,
      30, 30, 40, 40,
      30, 30, 40, 40,
    }));
  }

  [Test]
  [Category("Unit")]
  public void AV4BlockTakesOneVectorPerQuadrantInReadingOrder() {
    // Four entries, each holding four samples of its own. The quadrants are top left, top right,
    // bottom left, bottom right in that order, and within a quadrant the entry's four samples are
    // laid out the same way. A decoder that transposed either level produces a picture that still
    // looks like a picture, which is why this is stated pixel by pixel.
    var frame = _DecodeOne(
      _Stream("cvid"),
      Frame(0x00, 4, 4,
        Strip(StripIntra, 0, 0, 4, 4,
          Codebook(0x2000,
            Entry(1, 2, 3, 4, 0, 0),
            Entry(11, 12, 13, 14, 0, 0),
            Entry(21, 22, 23, 24, 0, 0),
            Entry(31, 32, 33, 34, 0, 0)),
          IntraVectors([0, 1, 2, 3]))));

    Assert.That(_Greys(frame), Is.EqualTo(new byte[] {
      1, 2, 11, 12,
      3, 4, 13, 14,
      21, 22, 31, 32,
      23, 24, 33, 34,
    }));
  }

  [Test]
  [Category("Unit")]
  public void AV1OnlyVectorListCarriesOneReferencePerBlockAndNoFlags() {
    var frame = _DecodeOne(
      _Stream("cvid"),
      Frame(0x00, 8, 4,
        Strip(StripIntra, 0, 0, 4, 8,
          Codebook(0x2200, Entry(10, 10, 10, 10, 0, 0), Entry(90, 90, 90, 90, 0, 0)),
          V1Vectors(1, 0))));

    Assert.That(_Greys(frame).Take(8).ToArray(), Is.EqualTo(new byte[] { 90, 90, 90, 90, 10, 10, 10, 10 }));
  }

  [Test]
  [Category("Unit")]
  public void AnIntraVectorListMixesV1AndV4BlocksByItsFlagBits() {
    var frame = _DecodeOne(
      _Stream("cvid"),
      Frame(0x00, 8, 4,
        Strip(StripIntra, 0, 0, 4, 8,
          Codebook(0x2000, Entry(1, 2, 3, 4, 0, 0)),
          Codebook(0x2200, Entry(70, 70, 70, 70, 0, 0)),
          IntraVectors([0], [0, 0, 0, 0]))));

    Assert.That(_Greys(frame).Take(8).ToArray(), Is.EqualTo(new byte[] { 70, 70, 70, 70, 1, 2, 1, 2 }));
  }

  // ============================================================================================
  // Strips
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AStripAfterTheFirstIsPlacedUnderTheOneBeforeItWhenItStatesNoTop() {
    // Every encoder writes a later strip's top as zero and its bottom as its height. Read literally
    // that draws both strips across the top of the picture and leaves the bottom half untouched,
    // which is a picture rather than an error and so would never be noticed.
    var frame = _DecodeOne(
      _Stream("cvid"),
      Frame(0x00, 4, 8,
        Strip(StripIntra, 0, 0, 4, 4,
          Codebook(0x2200, Entry(10, 10, 10, 10, 0, 0)),
          V1Vectors(0)),
        Strip(StripIntra, 0, 0, 4, 4,
          Codebook(0x2200, Entry(90, 90, 90, 90, 0, 0)),
          V1Vectors(0))));

    Assert.That(_Greys(frame).Take(4).ToArray(), Is.EqualTo(new byte[] { 10, 10, 10, 10 }), "the first strip");
    Assert.That(_Greys(frame).Skip(16).Take(4).ToArray(), Is.EqualTo(new byte[] { 90, 90, 90, 90 }), "the second, below it");
  }

  [Test]
  [Category("Unit")]
  public void AStripThatStatesARealTopIsPlacedWhereItSaysIt() {
    var frame = _DecodeOne(
      _Stream("cvid"),
      Frame(0x00, 4, 8,
        Strip(StripIntra, 4, 0, 8, 4,
          Codebook(0x2200, Entry(90, 90, 90, 90, 0, 0)),
          V1Vectors(0))));

    Assert.That(_Greys(frame).Take(4).ToArray(), Is.EqualTo(new byte[] { 0, 0, 0, 0 }), "untouched above");
    Assert.That(_Greys(frame).Skip(16).Take(4).ToArray(), Is.EqualTo(new byte[] { 90, 90, 90, 90 }));
  }

  // ============================================================================================
  // Between frames: the codebooks and the picture both carry over
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ACodebookOutlivesTheFrameThatStatedIt() {
    // The second frame restates no codebook at all and refers to entry zero. If codebooks did not
    // carry over it would draw palette-black and look like a decode.
    var frames = _Decode(_Stream("cvid"), [
      Frame(0x00, 4, 4,
        Strip(StripIntra, 0, 0, 4, 4,
          Codebook(0x2200, Entry(77, 77, 77, 77, 0, 0)),
          V1Vectors(0))),
      Frame(0x01, 4, 4, Strip(StripInter, 0, 0, 4, 4, V1Vectors(0))),
    ]);

    Assert.That(_Pixel(frames[1], 0, 0), Is.EqualTo(new byte[] { 77, 77, 77 }));
  }

  [Test]
  [Category("Unit")]
  public void ACodebookStatedInFullReplacesOnlyAsManyEntriesAsItCarries() {
    // A chunk of one entry restates entry zero and says nothing about entry one, which keeps what the
    // frame before put in it. Clearing the remainder would throw away vectors still referred to.
    var frames = _Decode(_Stream("cvid"), [
      Frame(0x00, 8, 4,
        Strip(StripIntra, 0, 0, 4, 8,
          Codebook(0x2200, Entry(11, 11, 11, 11, 0, 0), Entry(22, 22, 22, 22, 0, 0)),
          V1Vectors(0, 1))),
      Frame(0x01, 8, 4,
        Strip(StripInter, 0, 0, 4, 8,
          Codebook(0x2200, Entry(99, 99, 99, 99, 0, 0)),
          V1Vectors(0, 1))),
    ]);

    Assert.That(_Greys(frames[1]).Take(8).ToArray(), Is.EqualTo(new byte[] { 99, 99, 99, 99, 22, 22, 22, 22 }));
  }

  [Test]
  [Category("Unit")]
  public void ASelectiveUpdateChangesTheEntriesItsFlagsNameAndLeavesTheRest() {
    // The chunk form no encoder measured against here writes. Its flags and its entry bodies are
    // interleaved: a word of flags, then the bodies of the entries that word named, then the next
    // word — so a reader that looked for a flag table ahead of the bodies would decode entry zero
    // out of four bytes of flags.
    var frames = _Decode(_Stream("cvid"), [
      Frame(0x00, 12, 4,
        Strip(StripIntra, 0, 0, 4, 12,
          Codebook(0x2200,
            Entry(11, 11, 11, 11, 0, 0),
            Entry(22, 22, 22, 22, 0, 0),
            Entry(33, 33, 33, 33, 0, 0)),
          V1Vectors(0, 1, 2))),
      Frame(0x01, 12, 4,
        Strip(StripInter, 0, 0, 4, 12,
          CodebookUpdate(0x2300, 3, e => e == 1 ? Entry(88, 88, 88, 88, 0, 0) : null),
          V1Vectors(0, 1, 2))),
    ]);

    Assert.That(_Greys(frames[1]).Take(12).ToArray(), Is.EqualTo(new byte[] {
      11, 11, 11, 11, 88, 88, 88, 88, 33, 33, 33, 33,
    }));
  }

  [Test]
  [Category("Unit")]
  public void ASkippedBlockIsLeftAsTheFrameBeforeLeftIt() {
    var frames = _Decode(_Stream("cvid"), [
      Frame(0x00, 8, 4,
        Strip(StripIntra, 0, 0, 4, 8,
          Codebook(0x2200, Entry(11, 11, 11, 11, 0, 0), Entry(22, 22, 22, 22, 0, 0)),
          V1Vectors(0, 1))),
      Frame(0x01, 8, 4,
        Strip(StripInter, 0, 0, 4, 8,
          InterVectors(null, [0]))),
    ]);

    Assert.That(_Greys(frames[1]).Take(8).ToArray(), Is.EqualTo(new byte[] { 11, 11, 11, 11, 11, 11, 11, 11 }));
  }

  [Test]
  [Category("Unit")]
  public void AnInterVectorListRunsItsBitsOnAcrossTheWordsOfFlags() {
    // Thirty-three blocks, so the last one's two bits cannot both be in the first word of flags. The
    // bits are a stream and the word boundary falls where it falls; a reader that started each block
    // on a fresh word would decode this one from the wrong bits.
    var blocks = new byte[]?[33];
    for (var block = 0; block < 33; ++block)
      blocks[block] = [(byte)(block == 32 ? 1 : 0)];

    var frames = _Decode(_Stream("cvid"), [
      Frame(0x00, 132, 4,
        Strip(StripIntra, 0, 0, 4, 132,
          Codebook(0x2200, Entry(11, 11, 11, 11, 0, 0), Entry(55, 55, 55, 55, 0, 0)),
          V1Vectors(new byte[33]))),
      Frame(0x01, 132, 4, Strip(StripInter, 0, 0, 4, 132, InterVectors(blocks))),
    ]);

    Assert.That(_Greys(frames[1])[0], Is.EqualTo(11));
    Assert.That(_Greys(frames[1])[128], Is.EqualTo(55), "the thirty-third block, whose bits straddle the words");
  }

  [Test]
  [Category("Unit")]
  public void AFrameThatDoesNotInheritStartsFromEmptyCodebooks() {
    // Without the flag the strips of this frame define what they use, so anything left over from the
    // frame before is not theirs. A block reaching an entry no chunk of this frame wrote gets black
    // rather than a vector from another picture.
    var frames = _Decode(_Stream("cvid"), [
      Frame(0x00, 4, 4,
        Strip(StripIntra, 0, 0, 4, 4,
          Codebook(0x2200, Entry(77, 77, 77, 77, 0, 0)),
          V1Vectors(0))),
      Frame(0x00, 4, 4, Strip(StripIntra, 0, 0, 4, 4, V1Vectors(0))),
    ]);

    Assert.That(_Pixel(frames[1], 0, 0), Is.EqualTo(new byte[] { 0, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void EachFrameIsItsOwnPictureAndNotAViewOfTheCanvas() {
    var frames = _Decode(_Stream("cvid"), [
      _OneV1Block(11, 11, 0, 0),
      _OneV1Block(99, 99, 0, 0),
    ]);

    Assert.That(_Pixel(frames[0], 0, 0), Is.EqualTo(new byte[] { 11, 11, 11 }));
    Assert.That(_Pixel(frames[1], 0, 0), Is.EqualTo(new byte[] { 99, 99, 99 }));
  }

  // ============================================================================================
  // The refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AStripThatIsNeitherIntraNorInterIsRefusedByName() {
    var failure = Assert.Throws<InvalidDataException>(
      () => _DecodeOne(_Stream("cvid"), Frame(0x00, 4, 4, Strip(0x1200, 0, 0, 4, 4, V1Vectors(0)))));

    Assert.That(failure!.Message, Does.Contain("0x1200"));
  }

  [Test]
  [Category("Unit")]
  public void AChunkTypeTheFormatDoesNotDefineIsRefusedByName() {
    var failure = Assert.Throws<NotSupportedException>(
      () => _DecodeOne(_Stream("cvid"), Frame(0x00, 4, 4, Strip(StripIntra, 0, 0, 4, 4, Chunk(0x4000, 1, 2, 3, 4)))));

    Assert.That(failure!.Message, Does.Contain("0x4000"));
  }

  [Test]
  [Category("Unit")]
  public void AStripReachingOutsideThePictureIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(
      () => _DecodeOne(_Stream("cvid"), Frame(0x00, 4, 4, Strip(StripIntra, 0, 0, 8, 4, V1Vectors(0, 0)))));

    Assert.That(failure!.Message, Does.Contain("not inside the 4x4 picture"));
  }

  [Test]
  [Category("Unit")]
  public void AStripThatIsNotAWholeNumberOfBlocksIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(
      () => _DecodeOne(_Stream("cvid"), Frame(0x00, 6, 4, Strip(StripIntra, 0, 0, 4, 6, V1Vectors(0, 0)))));

    Assert.That(failure!.Message, Does.Contain("4x4 blocks"));
  }

  [Test]
  [Category("Unit")]
  public void APictureSizeThatChangesPartWayThroughIsRefused() {
    var decoder = CinepakVideoDecoder.Create(_Stream("cvid"));
    decoder.TryDecode(new(0, _OneV1Block(10, 10, 0, 0)), out _);

    var failure = Assert.Throws<NotSupportedException>(
      () => decoder.TryDecode(new(0, Frame(0x00, 8, 8,
        Strip(StripIntra, 0, 0, 8, 8,
          Codebook(0x2200, Entry(10, 10, 10, 10, 0, 0)),
          V1Vectors(0, 0, 0, 0)))), out _));

    Assert.That(failure!.Message, Does.Contain("changes picture size from 4x4 to 8x8"));
  }

  [Test]
  [Category("Unit")]
  public void AVectorListThatEndsBeforeEveryBlockIsAccountedForIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(
      () => _DecodeOne(_Stream("cvid"),
        Frame(0x00, 8, 4,
          Strip(StripIntra, 0, 0, 4, 8,
            Codebook(0x2200, Entry(10, 10, 10, 10, 0, 0)),
            V1Vectors(0)))));

    Assert.That(failure!.Message, Does.Contain("ends before the block"));
  }

  [Test]
  [Category("Unit")]
  public void AChunkStatingMoreBytesThanTheStripHoldsIsRefused() {
    var strip = Strip(StripIntra, 0, 0, 4, 4, Chunk(0x3200, 0));
    strip[^3] = 0xFF; // the chunk's own stated length, made larger than what follows it

    var failure = Assert.Throws<InvalidDataException>(() => _DecodeOne(_Stream("cvid"), Frame(0x00, 4, 4, strip)));

    Assert.That(failure!.Message, Does.Contain("remain in the strip"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameShorterThanItsOwnHeaderIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(() => _DecodeOne(_Stream("cvid"), [0, 0, 0, 4]));

    Assert.That(failure!.Message, Does.Contain("its header alone is 10"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameStatingMoreBytesThanThePacketHoldsIsRefused() {
    var frame = _OneV1Block(10, 10, 0, 0);
    frame[2] = 0xFF;

    var failure = Assert.Throws<InvalidDataException>(() => _DecodeOne(_Stream("cvid"), frame));

    Assert.That(failure!.Message, Does.Contain("and the packet holds"));
  }

  [Test]
  [Category("Unit")]
  public void ACodebookUpdateNamingAnEntryItDoesNotCarryIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(
      () => _DecodeOne(_Stream("cvid"),
        Frame(0x00, 4, 4,
          Strip(StripIntra, 0, 0, 4, 4,
            Chunk(0x2300, 0x80, 0x00, 0x00, 0x00, 1, 2),
            V1Vectors(0)))));

    Assert.That(failure!.Message, Does.Contain("names entry 0 as changed"));
  }

  // ============================================================================================
  // Through a container, end to end
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnAviOfCinepakFramesDecodesThroughTheRegistry() {
    var container = AviTestContainer.Build(
      "cvid", 4, 4, 24,
      [
        _OneV1Block(40, 40, 0, 0),
        Frame(0x01, 4, 4, Strip(StripInter, 0, 0, 4, 4, InterVectors([null]))),
      ]);

    var frames = VideoFormatRegistry.DecodeFrames(container).Select(f => f.Image).ToList();

    Assert.That(frames.Count, Is.EqualTo(2));
    Assert.That(frames[0].Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(_Pixel(frames[0], 0, 0), Is.EqualTo(new byte[] { 40, 40, 40 }));
    Assert.That(_Pixel(frames[1], 0, 0), Is.EqualTo(new byte[] { 40, 40, 40 }), "a frame of nothing but a skip");
  }

  // ============================================================================================
  // Fixtures
  // ============================================================================================

  /// <summary>A whole 4x4 frame of one V1 block, with the codebook entry stated inline.</summary>
  private static byte[] _OneV1Block(byte first, byte rest, sbyte u, sbyte v)
    => Frame(0x00, 4, 4,
      Strip(StripIntra, 0, 0, 4, 4,
        Codebook(0x2200, Entry(first, rest, rest, rest, u, v)),
        V1Vectors(0)));

  private static MediaStreamInfo _Stream(string code) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(code),
    Width = 4,
    Height = 4,
  };

  private static RawImage _DecodeOne(MediaStreamInfo stream, byte[] frame) => _Decode(stream, [frame])[0];

  private static IReadOnlyList<RawImage> _Decode(MediaStreamInfo stream, IReadOnlyList<byte[]> frames) {
    var decoder = CinepakVideoDecoder.Create(stream);
    var pictures = new List<RawImage>(frames.Count);

    foreach (var frame in frames)
      if (decoder.TryDecode(new(0, frame), out var picture))
        pictures.Add(picture);

    return pictures;
  }

  private static byte[] _Pixel(RawImage picture, int row, int column)
    => picture.PixelData.AsSpan((row * picture.Width + column) * 3, 3).ToArray();

  /// <summary>
  /// Every pixel's red channel, for pictures whose codebooks carry no chrominance and are therefore
  /// grey.
  /// </summary>
  private static byte[] _Greys(RawImage picture) {
    var greys = new byte[picture.Width * picture.Height];
    for (var i = 0; i < greys.Length; ++i)
      greys[i] = picture.PixelData[i * 3];

    return greys;
  }
}
