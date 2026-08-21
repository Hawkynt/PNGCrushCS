using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.QuickTimeRle.Tests;

/// <summary>
/// The QuickTime Animation decoder, on frames built here opcode by opcode.
/// </summary>
/// <remarks>
/// The arithmetic was checked against ffmpeg over every depth its encoder can write — thirty-two bits
/// with alpha, twenty-four, sixteen and eight-bit greyscale — pixel for pixel on every frame of
/// seventeen streams, and against ffmpeg's decode of streams built here for the depths it cannot
/// write. What these tests add is what neither comparison reaches: the mid-line skip no encoder here
/// emits, the empty frame, and the refusals, which by definition no valid stream contains.
/// </remarks>
[TestFixture]
public class QuickTimeRleDecoderTests {

  // ============================================================================================
  // The depths
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TwentyFourBitsAreReadAsRedGreenBlue() {
    var frame = _Decode(
      QuickTimeRleTestStream.Stream(2, 1, 24),
      new QuickTimeRleTestStream().Frame().Skip(0).Copy(2, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60).EndLine().End());

    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60 }));
  }

  [Test]
  [Category("Unit")]
  public void ThirtyTwoBitsPutTheAlphaFirstInTheStreamAndLastInThePicture() {
    // QuickTime's thirty-two bit pixel is ARGB. A picture that carried it through unmoved would show
    // every colour shifted by a channel and every alpha as a red.
    var frame = _Decode(
      QuickTimeRleTestStream.Stream(2, 1, 32),
      new QuickTimeRleTestStream().Frame().Skip(0).Copy(2, 0x80, 0x11, 0x22, 0x33, 0x40, 0x44, 0x55, 0x66).EndLine().End());

    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0x11, 0x22, 0x33, 0x80, 0x44, 0x55, 0x66, 0x40 }));
  }

  [Test]
  [Category("Unit")]
  public void SixteenBitsAreFiveBitsAChannelExpandedToFill() {
    // 0x7FFF is every channel at its maximum, which must come out as white and not as 248.
    var frame = _Decode(
      QuickTimeRleTestStream.Stream(3, 1, 16),
      new QuickTimeRleTestStream().Frame().Skip(0).Copy(3, 0x7F, 0xFF, 0x7C, 0x00, 0x00, 0x1F).EndLine().End());

    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 255, 255, 255, 255, 0, 0, 0, 0, 255 }));
  }

  [Test]
  [Category("Unit")]
  public void EightBitsAreIndicesIntoTheColourTableTheDescriptionCarries() {
    byte[] palette = new byte[256 * 3];
    palette[3] = 10;
    palette[4] = 20;
    palette[5] = 30;

    var frame = _Decode(
      QuickTimeRleTestStream.Stream(4, 1, 8, palette),
      new QuickTimeRleTestStream().Frame().Skip(0).Copy(1, 1, 0, 1, 0).EndLine().End());

    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 0, 1, 0 }));
    Assert.That(frame.ToRgb24(), Is.EqualTo(new byte[] { 10, 20, 30, 0, 0, 0, 10, 20, 30, 0, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void FourBitsAreTwoIndicesAByteWithTheHighNibbleLeftmost() {
    var palette = new byte[16 * 3];
    for (var i = 0; i < 16; ++i)
      palette[i * 3] = (byte)(i * 16);

    var frame = _Decode(
      QuickTimeRleTestStream.Stream(8, 1, 4, palette),
      new QuickTimeRleTestStream().Frame().Skip(0).Copy(1, 0x01, 0x23, 0x45, 0x67).EndLine().End());

    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }));
  }

  [Test]
  [Category("Unit")]
  public void TwoBitsAreFourIndicesAByte() {
    var palette = new byte[4 * 3];
    var frame = _Decode(
      QuickTimeRleTestStream.Stream(16, 1, 2, palette),
      new QuickTimeRleTestStream().Frame().Skip(0).Copy(1, 0b00011011, 0, 0, 0b11100100).EndLine().End());

    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0, 1, 2, 3, 0, 0, 0, 0, 0, 0, 0, 0, 3, 2, 1, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void AGreyscaleDepthRunsFromWhiteAtIndexZeroDownToBlack() {
    // The Macintosh convention, and the opposite of what a reader taking the index for a luminance
    // would draw. Measured against ffmpeg, whose decode of an eight-bit greyscale sample puts index
    // 0xFF at black.
    var frame = _Decode(
      QuickTimeRleTestStream.Stream(4, 1, 40),
      new QuickTimeRleTestStream().Frame().Skip(0).Copy(1, 0, 1, 254, 255).EndLine().End());

    Assert.That(frame.ToRgb24(), Is.EqualTo(new byte[] { 255, 255, 255, 254, 254, 254, 1, 1, 1, 0, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void AGreyscaleDepthNeedsNoColourTableInTheFile() {
    var frame = _Decode(
      QuickTimeRleTestStream.Stream(16, 1, 34),
      new QuickTimeRleTestStream().Frame().Skip(0).Copy(1, 0b00011011, 0, 0, 0).EndLine().End());

    Assert.That(frame.ToRgb24()[..12], Is.EqualTo(new byte[] { 255, 255, 255, 170, 170, 170, 85, 85, 85, 0, 0, 0 }));
  }

  // ============================================================================================
  // The opcodes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ARunWritesOneUnitOverAndOver() {
    var frame = _Decode(
      QuickTimeRleTestStream.Stream(4, 1, 24),
      new QuickTimeRleTestStream().Frame().Skip(0).Run(4, 0x11, 0x22, 0x33).EndLine().End());

    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 0x11, 0x22, 0x33, 0x11, 0x22, 0x33, 0x11, 0x22, 0x33, 0x11, 0x22, 0x33 }));
  }

  [Test]
  [Category("Unit")]
  public void ASkipAtTheStartOfALineLeavesWhatWasThere() {
    var stream = QuickTimeRleTestStream.Stream(4, 1, 24);
    var decoder = QuickTimeRleDecoder.Create(stream);

    _Feed(decoder, new QuickTimeRleTestStream().Frame().Skip(0).Run(4, 9, 9, 9).EndLine().End());
    var frame = _Feed(decoder, new QuickTimeRleTestStream().Frame().Skip(2).Run(2, 1, 2, 3).EndLine().End());

    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 9, 9, 9, 9, 9, 9, 1, 2, 3, 1, 2, 3 }));
  }

  [Test]
  [Category("Unit")]
  public void ASkipInTheMiddleOfALineLeavesWhatWasThere() {
    // The zero opcode, which ffmpeg's encoder never writes and which no comparison against it can
    // therefore reach.
    var stream = QuickTimeRleTestStream.Stream(4, 1, 24);
    var decoder = QuickTimeRleDecoder.Create(stream);

    _Feed(decoder, new QuickTimeRleTestStream().Frame().Skip(0).Run(4, 9, 9, 9).EndLine().End());
    var frame = _Feed(decoder, new QuickTimeRleTestStream()
      .Frame().Skip(0).Copy(1, 1, 2, 3).SkipAgain(2).Copy(1, 4, 5, 6).EndLine().End());

    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 9, 9, 9, 9, 9, 9, 4, 5, 6 }));
  }

  [Test]
  [Category("Unit")]
  public void AFrameThatNamesABandOfLinesLeavesTheOthersAlone() {
    var stream = QuickTimeRleTestStream.Stream(1, 4, 24);
    var decoder = QuickTimeRleDecoder.Create(stream);

    var first = new QuickTimeRleTestStream().Frame();
    for (var y = 0; y < 4; ++y)
      first.Skip(0).Copy(1, (byte)(y + 1), 0, 0).EndLine();
    _Feed(decoder, first.End());

    var frame = _Feed(decoder, new QuickTimeRleTestStream()
      .Frame(startLine: 1, lines: 2).Skip(0).Copy(1, 9, 9, 9).EndLine().Skip(0).Copy(1, 8, 8, 8).EndLine().End());

    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 0, 0, 9, 9, 9, 8, 8, 8, 4, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void AFrameTooShortToHoldAHeaderRepeatsThePictureBeforeIt() {
    // A frame the file states as empty. That is a frame of the film and not a decode that failed.
    var stream = QuickTimeRleTestStream.Stream(2, 1, 24);
    var decoder = QuickTimeRleDecoder.Create(stream);

    var first = _Feed(decoder, new QuickTimeRleTestStream().Frame().Skip(0).Copy(2, 1, 2, 3, 4, 5, 6).EndLine().End());
    var second = _Feed(decoder, [0, 0, 0, 4]);

    Assert.That(second.PixelData, Is.EqualTo(first.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void APictureIsACopyAndNotTheCanvasTheNextFrameIsWrittenOver() {
    var stream = QuickTimeRleTestStream.Stream(2, 1, 24);
    var decoder = QuickTimeRleDecoder.Create(stream);

    var first = _Feed(decoder, new QuickTimeRleTestStream().Frame().Skip(0).Copy(2, 1, 2, 3, 4, 5, 6).EndLine().End());
    _Feed(decoder, new QuickTimeRleTestStream().Frame().Skip(0).Copy(2, 9, 9, 9, 9, 9, 9).EndLine().End());

    Assert.That(first.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }));
  }

  [Test]
  [Category("Unit")]
  public void AWidthThatIsNotAWholeNumberOfUnitsIsCodedPaddedAndShownCropped() {
    // Five pixels at eight bits is two units of four, and the last three of them are padding the
    // stream carries and the picture does not.
    var palette = new byte[256 * 3];
    var frame = _Decode(
      QuickTimeRleTestStream.Stream(5, 1, 8, palette),
      new QuickTimeRleTestStream().Frame().Skip(0).Copy(2, 1, 2, 3, 4, 5, 6, 7, 8).EndLine().End());

    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
  }

  // ============================================================================================
  // One bit, which is a different shape altogether
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void OneBitCarriesASkipWithEveryOpcodeAndStartsALineWithTheTopBit() {
    var palette = new byte[] { 0, 0, 0, 255, 255, 255 };
    var frame = _Decode(
      QuickTimeRleTestStream.Stream(32, 2, 1, palette),
      new QuickTimeRleTestStream()
        .Frame(0, 2)
        .OneBitOpcode(startsLine: true, units: 0, code: 2, 0b10000000, 0b00000001, 0b11110000, 0b00001111)
        .OneBitOpcode(startsLine: true, units: 1, code: 1, 0b10101010, 0b01010101)
        .Raw(0, 0)
        .End());

    var pixels = frame.PixelData;
    Assert.That(pixels[0], Is.EqualTo(1));
    Assert.That(pixels[7], Is.EqualTo(0));
    Assert.That(pixels[15], Is.EqualTo(1));
    Assert.That(pixels[16], Is.EqualTo(1));
    Assert.That(pixels[20], Is.EqualTo(0));

    // The second opcode starts line one and steps one unit in, so its first sixteen pixels are the
    // black the canvas began as and the pattern lands from column sixteen.
    Assert.That(pixels[32..48], Is.EqualTo(new byte[16]));
    Assert.That(pixels[48], Is.EqualTo(1));
    Assert.That(pixels[49], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void OneBitTreatsTheEndOfLineMarkerAsSayingNothing() {
    // Measured: a code of -1 at one bit writes no pixels and carries no unit with it, because the
    // skip beside it has already said where the next opcode goes. It still counts as the line its
    // skip named, so the opcode after it is the next line's. Had it swallowed two bytes, the opcode
    // after would have been read from the middle of itself and this picture would be another one.
    var palette = new byte[] { 0, 0, 0, 255, 255, 255 };
    var frame = _Decode(
      QuickTimeRleTestStream.Stream(16, 2, 1, palette),
      new QuickTimeRleTestStream()
        .Frame(0, 2)
        .OneBitOpcode(startsLine: true, units: 0, code: -1)
        .OneBitOpcode(startsLine: true, units: 0, code: 1, 0b10101010, 0b01010101)
        .End());

    Assert.That(frame.PixelData[..16], Is.EqualTo(new byte[16]));
    Assert.That(frame.PixelData[16..], Is.EqualTo(new byte[] { 1, 0, 1, 0, 1, 0, 1, 0, 0, 1, 0, 1, 0, 1, 0, 1 }));
  }

  [Test]
  [Category("Unit")]
  public void OneBitRunsRepeatAUnitOfSixteenPixels() {
    var palette = new byte[] { 0, 0, 0, 255, 255, 255 };
    var frame = _Decode(
      QuickTimeRleTestStream.Stream(32, 1, 1, palette),
      new QuickTimeRleTestStream()
        .Frame(0, 1)
        .OneBitOpcode(startsLine: true, units: 0, code: -2, 0b11111111, 0b00000000)
        .Raw(0, 0)
        .End());

    Assert.That(frame.PixelData[..8], Is.EqualTo(new byte[] { 1, 1, 1, 1, 1, 1, 1, 1 }));
    Assert.That(frame.PixelData[8..16], Is.EqualTo(new byte[8]));
    Assert.That(frame.PixelData[16..24], Is.EqualTo(new byte[] { 1, 1, 1, 1, 1, 1, 1, 1 }));
  }

  // ============================================================================================
  // What refuses
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ADepthTheCompressorDoesNotCodeIsRefusedByName() {
    var failure = Assert.Throws<NotSupportedException>(
      () => QuickTimeRleDecoder.Create(QuickTimeRleTestStream.Stream(4, 1, 12)));

    Assert.That(failure!.Message, Does.Contain("depth of 12"));
  }

  [Test]
  [Category("Unit")]
  public void AnIndexedDepthWithoutAColourTableIsRefusedByName() {
    // The Macintosh default palettes would fill the gap, and nothing here can check them against
    // anything. A picture drawn through a guessed table cannot be told from a right one.
    var failure = Assert.Throws<NotSupportedException>(
      () => QuickTimeRleDecoder.Create(QuickTimeRleTestStream.Stream(4, 1, 8)));

    Assert.That(failure!.Message, Does.Contain("no colour table"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamThatOpensWithAnEmptyFrameIsRefused() {
    var decoder = QuickTimeRleDecoder.Create(QuickTimeRleTestStream.Stream(2, 1, 24));

    var failure = Assert.Throws<InvalidDataException>(() => _Feed(decoder, [0, 0, 0, 4]));
    Assert.That(failure!.Message, Does.Contain("no frame before it"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamThatOpensWithAPartialBandIsRefused() {
    var decoder = QuickTimeRleDecoder.Create(QuickTimeRleTestStream.Stream(1, 4, 24));

    var failure = Assert.Throws<InvalidDataException>(
      () => _Feed(decoder, new QuickTimeRleTestStream().Frame(1, 2).Skip(0).Copy(1, 1, 1, 1).EndLine().Skip(0).Copy(1, 1, 1, 1).EndLine().End()));

    Assert.That(failure!.Message, Does.Contain("Decoding cannot begin"));
  }

  [Test]
  [Category("Unit")]
  public void ACountThatWouldRunPastTheEndOfALineIsRefused() {
    var decoder = QuickTimeRleDecoder.Create(QuickTimeRleTestStream.Stream(2, 1, 24));

    var failure = Assert.Throws<InvalidDataException>(
      () => _Feed(decoder, new QuickTimeRleTestStream().Frame().Skip(0).Copy(3, 1, 1, 1, 2, 2, 2, 3, 3, 3).EndLine().End()));

    Assert.That(failure!.Message, Does.Contain("pixels wide"));
  }

  [Test]
  [Category("Unit")]
  public void ABandThatReachesPastTheLastLineIsRefused() {
    var decoder = QuickTimeRleDecoder.Create(QuickTimeRleTestStream.Stream(1, 2, 24));

    var failure = Assert.Throws<InvalidDataException>(
      () => _Feed(decoder, new QuickTimeRleTestStream().Frame(1, 4).Skip(0).Copy(1, 1, 1, 1).EndLine().End()));

    Assert.That(failure!.Message, Does.Contain("lines tall"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameThatRunsOutOfBytesIsRefused() {
    var decoder = QuickTimeRleDecoder.Create(QuickTimeRleTestStream.Stream(4, 1, 24));

    var failure = Assert.Throws<InvalidDataException>(
      () => _Feed(decoder, new QuickTimeRleTestStream().Frame().Skip(0).Copy(4, 1, 1, 1).End()));

    Assert.That(failure!.Message, Does.Contain("coded unit"));
  }

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheCodecAnswersToItsCodeInEitherCase() {
    Assert.That(QuickTimeRleDecoder.Accepts(QuickTimeRleTestStream.Stream(2, 1, 24)), Is.True);

    var upper = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("RLE "),
      Width = 2,
      Height = 1,
      BitsPerPixel = 24,
    };
    Assert.That(QuickTimeRleDecoder.Accepts(upper), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecDoesNotAnswerForAnotherCode() {
    var other = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("MJPG"),
      Width = 2,
      Height = 1,
    };

    Assert.That(QuickTimeRleDecoder.Accepts(other), Is.False);
  }

  // ============================================================================================

  private static RawImage _Decode(MediaStreamInfo stream, byte[] frame) => _Feed(QuickTimeRleDecoder.Create(stream), frame);

  private static RawImage _Feed(QuickTimeRleDecoder decoder, byte[] frame) {
    Assert.That(decoder.TryDecode(new(0, frame), out var picture), Is.True);
    return picture;
  }
}
