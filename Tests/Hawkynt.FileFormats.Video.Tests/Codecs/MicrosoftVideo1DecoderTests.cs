using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi.Tests;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The Microsoft Video 1 decoder, on frames built here byte by byte.
/// </summary>
/// <remarks>
/// The sixteen-bit variant was settled against ffmpeg — every frame of every stream measured is
/// identical to its decode of the same file — so what these tests add is what that comparison cannot
/// reach: the geometry stated one pixel at a time, the eight-bit variant, which ffmpeg's own encoder
/// will not write, and the refusals, which no valid stream produces.
/// <para/>
/// The geometry tests are deliberately down to a single pixel of a single block. Block order, mask
/// layout and quad layout are four independent ways of getting a picture that looks decoded and is
/// mirrored, and a test that only checked "the block has the right colours in it" would pass for all
/// of them.
/// </remarks>
[TestFixture]
public sealed class MicrosoftVideo1DecoderTests {

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase("CRAM")]
  [TestCase("MSVC")]
  [TestCase("WHAM")]
  [TestCase("cram")]
  [TestCase("msvc")]
  public void EveryCodeThisCodecHasShippedUnderIsTaken(string code)
    => Assert.That(MicrosoftVideo1Decoder.Accepts(_Stream(16, code: code)), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsCodeIsNotTaken()
    => Assert.That(MicrosoftVideo1Decoder.Accepts(_Stream(16, code: "cvid")), Is.False);

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsNotTaken() {
    var sound = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("MSVC") };

    Assert.That(MicrosoftVideo1Decoder.Accepts(sound), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream(16);

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Microsoft Video 1"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<MicrosoftVideo1Decoder>());
  }

  // ============================================================================================
  // Where a block goes, and which pixel of it a mask bit is
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void BlocksAreCodedFromTheBottomOfThePictureUpwards() {
    // One block wide and two high. The first block coded is the bottom one, as a bitmap's first row
    // is its bottom row.
    var frame = _DecodeOne(_Stream(8, width: 4, height: 8), [7, 0x80, 9, 0x80]);

    Assert.That(_Row(frame, 7), Is.EqualTo(new byte[] { 7, 7, 7, 7 }), "the block coded first is the bottom one");
    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 9, 9, 9, 9 }));
  }

  [Test]
  [Category("Unit")]
  public void BlocksRunLeftToRightWithinARow() {
    var frame = _DecodeOne(_Stream(8, width: 8, height: 4), [7, 0x80, 9, 0x80]);

    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 7, 7, 7, 7, 9, 9, 9, 9 }));
  }

  [Test]
  [Category("Unit")]
  public void BitZeroOfTheFirstFlagByteIsTheBottomLeftPixelOfTheBlock() {
    // The one statement about the mask the format makes in so many words. Bit 0 set means the lower
    // left pixel of the block takes the first colour; everything else takes the second.
    var frame = _DecodeOne(_Stream(8, width: 4, height: 4), [0x01, 0x00, 3, 5]);

    Assert.That(_Row(frame, 3), Is.EqualTo(new byte[] { 3, 5, 5, 5 }));
    Assert.That(_Row(frame, 2), Is.EqualTo(new byte[] { 5, 5, 5, 5 }));
    Assert.That(_Row(frame, 1), Is.EqualTo(new byte[] { 5, 5, 5, 5 }));
    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 5, 5, 5, 5 }));
  }

  [Test]
  [Category("Unit")]
  public void TheMaskRunsBottomRowFirstFourBitsAtATime() {
    // The low nibble of the first byte is the block's bottom row, its high nibble the row above, and
    // the second byte carries the two rows above that. Written out as one row per nibble so that a
    // reader flipping or transposing the mask fails here rather than on a picture.
    var frame = _DecodeOne(_Stream(8, width: 4, height: 4), [0x21, 0x48, 3, 5]);

    Assert.That(_Row(frame, 3), Is.EqualTo(new byte[] { 3, 5, 5, 5 }), "low nibble of byte a: bit 0");
    Assert.That(_Row(frame, 2), Is.EqualTo(new byte[] { 5, 3, 5, 5 }), "high nibble of byte a: bit 5");
    Assert.That(_Row(frame, 1), Is.EqualTo(new byte[] { 5, 5, 5, 3 }), "low nibble of byte b: bit 11");
    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 5, 5, 3, 5 }), "high nibble of byte b: bit 14");
  }

  [Test]
  [Category("Unit")]
  public void AnEightColourBlockGivesEachQuadItsOwnPairOfColours() {
    // Quads are numbered from the bottom left the way the blocks are: 1 bottom left, 2 bottom right,
    // 3 top left, 4 top right, and their colours arrive in that order. The mask is 0x9000 — the top
    // row's leftmost and rightmost pixels take their quad's first colour, everything else the second.
    var frame = _DecodeOne(
      _Stream(8, width: 4, height: 4),
      [0x00, 0x90, 11, 12, 21, 22, 31, 32, 41, 42]);

    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 31, 32, 42, 41 }), "top row: quad 3 then quad 4");
    Assert.That(_Row(frame, 1), Is.EqualTo(new byte[] { 32, 32, 42, 42 }));
    Assert.That(_Row(frame, 2), Is.EqualTo(new byte[] { 12, 12, 22, 22 }), "bottom half: quads 1 and 2");
    Assert.That(_Row(frame, 3), Is.EqualTo(new byte[] { 12, 12, 22, 22 }));
  }

  [Test]
  [Category("Unit")]
  public void AFlagByteInTheGapsEitherSideOfTheSkipCodesIsASolidBlock() {
    foreach (var high in new byte[] { 0x80, 0x83, 0x88, 0x8F }) {
      var frame = _DecodeOne(_Stream(8, width: 4, height: 4), [6, high]);

      Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 6, 6, 6, 6 }), $"second flag byte 0x{high:X2}");
    }
  }

  // ============================================================================================
  // The inter-frame coding, which is the skip run and nothing else
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASkippedBlockIsLeftAsTheFrameBeforeLeftIt() {
    var frames = _Decode(
      _Stream(8, width: 8, height: 4),
      [
        [7, 0x80, 9, 0x80],
        [1, 0x84, 4, 0x80],
      ]);

    Assert.That(_Row(frames[0], 0), Is.EqualTo(new byte[] { 7, 7, 7, 7, 9, 9, 9, 9 }));
    Assert.That(_Row(frames[1], 0), Is.EqualTo(new byte[] { 7, 7, 7, 7, 4, 4, 4, 4 }));
  }

  [Test]
  [Category("Unit")]
  public void AFrameOfNothingButSkipsRepeatsTheOneBeforeIt() {
    var frames = _Decode(
      _Stream(8, width: 8, height: 4),
      [
        [7, 0x80, 9, 0x80],
        [2, 0x84],
      ]);

    Assert.That(_Row(frames[1], 0), Is.EqualTo(_Row(frames[0], 0)));
  }

  [Test]
  [Category("Unit")]
  public void ASkipRunCountsPastTwoHundredAndFiftySixWithItsSecondByte() {
    // 0x85 is the second page of the count, so this skips 256 blocks and not one.
    var stream = _Stream(8, width: 128, height: 32); // 32 blocks across, 8 rows: 256 blocks
    var frames = _Decode(stream, [_Solid(256, 7), [0x00, 0x85]]);

    Assert.That(frames[1].PixelData, Is.EqualTo(frames[0].PixelData));
  }

  [Test]
  [Category("Unit")]
  public void EachFrameIsItsOwnPictureAndNotAViewOfTheCanvas() {
    var frames = _Decode(_Stream(8, width: 4, height: 4), [[7, 0x80], [9, 0x80]]);

    Assert.That(_Row(frames[0], 0), Is.EqualTo(new byte[] { 7, 7, 7, 7 }));
    Assert.That(_Row(frames[1], 0), Is.EqualTo(new byte[] { 9, 9, 9, 9 }));
  }

  // ============================================================================================
  // Sixteen bits, where the colours are in the blocks
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASixteenBitSolidBlockIsFiveFiveFiveWithRedInTheHighBits() {
    // 0xFC00: bit 15 is the spare one this format spends on the eight-colour marker, and the five
    // bits below it are red at full scale. Widened by repeating the pattern, full scale is 255 and
    // not the 248 a plain shift would give.
    var frame = _DecodeOne(_Stream(16, width: 4, height: 4), [0x00, 0xFC]);

    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(_Pixel(frame, 0, 0), Is.EqualTo(new byte[] { 255, 0, 0 }));
    Assert.That(_Pixel(frame, 3, 3), Is.EqualTo(new byte[] { 255, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void ASixteenBitTwoColourBlockChoosesBetweenTwoLittleEndianColours() {
    // Flags 0x0001, then red (0x7C00) and blue (0x001F). The first colour's top bit is clear, which
    // is what makes this a two-colour block rather than an eight-colour one.
    var frame = _DecodeOne(_Stream(16, width: 4, height: 4), [0x01, 0x00, 0x00, 0x7C, 0x1F, 0x00]);

    Assert.That(_Pixel(frame, 3, 0), Is.EqualTo(new byte[] { 255, 0, 0 }), "bit 0 is the bottom left pixel");
    Assert.That(_Pixel(frame, 3, 1), Is.EqualTo(new byte[] { 0, 0, 255 }));
    Assert.That(_Pixel(frame, 0, 0), Is.EqualTo(new byte[] { 0, 0, 255 }));
  }

  [Test]
  [Category("Unit")]
  public void TheTopBitOfTheFirstColourIsWhatMakesASixteenBitBlockEightColour() {
    // The same flag byte as the two-colour block above; only the first colour's top bit differs, and
    // it turns four more colours into eight and the block from six bytes into eighteen.
    var frame = _DecodeOne(
      _Stream(16, width: 4, height: 4),
      [
        0x01, 0x00,
        0x00, 0xFC, 0x1F, 0x00, // quad 1: red (marked), blue
        0xE0, 0x03, 0xFF, 0x7F, // quad 2: green, white
        0x00, 0x00, 0x1F, 0x7C, // quad 3: black, magenta
        0xE0, 0x7F, 0xFF, 0x03, // quad 4: yellow, cyan
      ]);

    Assert.That(_Pixel(frame, 3, 0), Is.EqualTo(new byte[] { 255, 0, 0 }), "quad 1, first colour");
    Assert.That(_Pixel(frame, 3, 1), Is.EqualTo(new byte[] { 0, 0, 255 }), "quad 1, second colour");
    Assert.That(_Pixel(frame, 3, 2), Is.EqualTo(new byte[] { 255, 255, 255 }), "quad 2, second colour");
    Assert.That(_Pixel(frame, 0, 0), Is.EqualTo(new byte[] { 255, 0, 255 }), "quad 3, second colour");
    Assert.That(_Pixel(frame, 0, 3), Is.EqualTo(new byte[] { 0, 255, 255 }), "quad 4, second colour");
  }

  [Test]
  [Category("Unit")]
  public void TheEightColourMarkerNeverReachesASample() {
    // Bit 15 is set on the first colour of an eight-colour block and is not a channel. A reader that
    // masked to six bits somewhere would turn this full-scale red into something else.
    var frame = _DecodeOne(
      _Stream(16, width: 4, height: 4),
      [
        0xFF, 0x7F,
        0x00, 0xFC, 0x00, 0xFC,
        0x00, 0xFC, 0x00, 0xFC,
        0x00, 0xFC, 0x00, 0xFC,
        0x00, 0xFC, 0x00, 0xFC,
      ]);

    Assert.That(_Pixel(frame, 0, 0), Is.EqualTo(new byte[] { 255, 0, 0 }));
    Assert.That(_Pixel(frame, 3, 3), Is.EqualTo(new byte[] { 255, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void ASixteenBitStreamNeedsNoPalette() {
    var stream = _Stream(16, width: 4, height: 4, paletteEntries: 0);
    var frame = _DecodeOne(stream, [0x00, 0xFC]);

    Assert.That(frame.Palette, Is.Null);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
  }

  // ============================================================================================
  // The refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ADepthTheCodecIsNotDefinedAtIsRefusedByName() {
    var failure = Assert.Throws<NotSupportedException>(() => MicrosoftVideo1Decoder.Create(_Stream(24)));

    Assert.That(failure!.Message, Does.Contain("24 bits per pixel"));
  }

  [Test]
  [Category("Unit")]
  public void APictureThatIsNotAWholeNumberOfBlocksIsRefusedRatherThanCroppedOrPadded() {
    // The reference encoder refuses to write one, and the codec states nowhere which edge a partial
    // block would fall off. Choosing an edge here would be this decoder inventing the answer.
    var failure = Assert.Throws<NotSupportedException>(() => MicrosoftVideo1Decoder.Create(_Stream(16, width: 17, height: 12)));

    Assert.That(failure!.Message, Does.Contain("17x12"));
    Assert.That(failure.Message, Does.Contain("4x4 blocks"));
  }

  [Test]
  [Category("Unit")]
  public void AnEightBitStreamWithNoPaletteIsRefusedRatherThanDecodedToGrey() {
    var failure = Assert.Throws<InvalidOperationException>(
      () => MicrosoftVideo1Decoder.Create(_Stream(8, paletteEntries: 0)));

    Assert.That(failure!.Message, Does.Contain("no palette"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamFormatShorterThanABitmapHeaderIsRefused() {
    var failure = Assert.Throws<InvalidOperationException>(
      () => MicrosoftVideo1Decoder.Create(_WithFormat(_Stream(16), new byte[20])));

    Assert.That(failure!.Message, Does.Contain("BITMAPINFOHEADER"));
  }

  [Test]
  [Category("Unit")]
  public void ASkipRunReachingPastTheLastBlockIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(
      () => _DecodeOne(_Stream(8, width: 8, height: 4), [9, 0x84]));

    Assert.That(failure!.Message, Does.Contain("reaches past the last of the 2 blocks"));
  }

  [Test]
  [Category("Unit")]
  public void ASkipRunOfNoBlocksIsRefusedBecauseNobodyAgreesWhatOneMeans() {
    // Read as documented it is a two-byte no-op; ffmpeg instead abandons the rest of the frame there.
    // Both produce a picture, they differ across everything after the run, and the file does not say
    // which was meant. Measured: runs of 1, 256 and 512 blocks decode identically in both, so the
    // disagreement is about the count being zero and not about the two-byte form of the count.
    var failure = Assert.Throws<NotSupportedException>(
      () => _DecodeOne(_Stream(8, width: 8, height: 4), [0, 0x84, 7, 0x80, 9, 0x80]));

    Assert.That(failure!.Message, Does.Contain("skips no blocks at all"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameThatStopsBeforeEveryBlockIsAccountedForIsRefused() {
    // Leaving the rest as the previous frame would be indistinguishable from a frame that had said
    // so with a skip run, which is the one thing this codec's inter frames are made of.
    var failure = Assert.Throws<InvalidDataException>(
      () => _DecodeOne(_Stream(8, width: 8, height: 4), [7, 0x80]));

    Assert.That(failure!.Message, Does.Contain("at block 1"));
  }

  [Test]
  [Category("Unit")]
  public void ABlockWantingMoreColoursThanTheFrameHoldsIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(
      () => _DecodeOne(_Stream(8, width: 4, height: 4), [0x00, 0x90, 1, 2, 3]));

    Assert.That(failure!.Message, Does.Contain("eight-colour block's colours"));
  }

  // ============================================================================================
  // Through a container, end to end
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnAviOfVideo1FramesDecodesThroughTheRegistry() {
    var container = AviTestContainer.Build(
      "MSVC", 8, 4, 16,
      [
        [0x00, 0xFC, 0x00, 0x83],
        [0x01, 0x84, 0x1F, 0x80],
      ]);

    var frames = VideoFormatRegistry.DecodeFrames(container).Select(f => f.Image).ToList();

    Assert.That(frames.Count, Is.EqualTo(2));
    Assert.That(_Pixel(frames[0], 0, 0), Is.EqualTo(new byte[] { 255, 0, 0 }));
    Assert.That(_Pixel(frames[1], 0, 0), Is.EqualTo(new byte[] { 255, 0, 0 }), "the skipped block");
    Assert.That(_Pixel(frames[1], 0, 4), Is.EqualTo(new byte[] { 0, 0, 255 }));
  }

  // ============================================================================================
  // Fixtures
  // ============================================================================================

  private static byte[] _Solid(int blocks, byte colour) {
    var frame = new byte[blocks * 2];
    for (var block = 0; block < blocks; ++block) {
      frame[block * 2] = colour;
      frame[block * 2 + 1] = 0x80;
    }

    return frame;
  }

  private static MediaStreamInfo _Stream(
    int bitsPerPixel,
    int width = 4,
    int height = 4,
    int paletteEntries = -1,
    string code = "MSVC") {
    if (paletteEntries < 0)
      paletteEntries = bitsPerPixel == 8 ? 256 : 0;

    var format = new byte[40 + paletteEntries * 4];
    var span = format.AsSpan();
    BinaryPrimitives.WriteInt32LittleEndian(span, 40);
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], width);
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], height);
    BinaryPrimitives.WriteInt16LittleEndian(span[12..], 1);
    BinaryPrimitives.WriteInt16LittleEndian(span[14..], (short)bitsPerPixel);
    BinaryPrimitives.WriteInt32LittleEndian(span[16..], (int)CodecTag.FromCharacters(code).Value);
    BinaryPrimitives.WriteInt32LittleEndian(span[32..], paletteEntries);

    // A palette whose entries are all different, so a reader picking the wrong one is visible.
    for (var entry = 0; entry < paletteEntries; ++entry) {
      format[40 + entry * 4] = (byte)(entry * 5);
      format[40 + entry * 4 + 1] = (byte)(entry * 9);
      format[40 + entry * 4 + 2] = (byte)(entry * 13);
    }

    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters(code),
      Width = width,
      Height = height,
      BitsPerPixel = bitsPerPixel,
      CodecPrivateData = format,
    };
  }

  private static MediaStreamInfo _WithFormat(MediaStreamInfo stream, byte[] format) => new() {
    Index = stream.Index,
    Kind = stream.Kind,
    Codec = stream.Codec,
    Width = stream.Width,
    Height = stream.Height,
    BitsPerPixel = stream.BitsPerPixel,
    CodecPrivateData = format,
  };

  private static RawImage _DecodeOne(MediaStreamInfo stream, byte[] frame) => _Decode(stream, [frame])[0];

  private static IReadOnlyList<RawImage> _Decode(MediaStreamInfo stream, IReadOnlyList<byte[]> frames) {
    var decoder = MicrosoftVideo1Decoder.Create(stream);
    var pictures = new List<RawImage>(frames.Count);

    foreach (var frame in frames)
      if (decoder.TryDecode(new(0, frame), out var picture))
        pictures.Add(picture);

    return pictures;
  }

  /// <summary>One row of an eight-bit picture as palette indices, counted from the top.</summary>
  private static byte[] _Row(RawImage picture, int row)
    => picture.PixelData.AsSpan(row * picture.Width, picture.Width).ToArray();

  /// <summary>One pixel of a sixteen-bit picture as red, green and blue.</summary>
  private static byte[] _Pixel(RawImage picture, int row, int column)
    => picture.PixelData.AsSpan((row * picture.Width + column) * 3, 3).ToArray();
}
