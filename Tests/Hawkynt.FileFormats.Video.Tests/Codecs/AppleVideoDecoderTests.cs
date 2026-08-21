using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi.Tests;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The Apple Video (RPZA) decoder, on chunks built here byte by byte.
/// </summary>
/// <remarks>
/// Whole streams were settled against ffmpeg — every frame of eight streams, 924 in all, is
/// identical to its decode of the same file, across QuickTime and AVI, geometry that is and is not
/// a whole number of blocks, and every opcode including the one the format's own documentation
/// calls unused. What these tests add is what that comparison does not isolate on its own: which
/// byte decides the special opcode's two variants — the one place a plausible misreading produced a
/// picture rather than an error — the four-colour blend formula in isolation, the block order, and
/// the refusals, which no valid chunk produces.
/// </remarks>
[TestFixture]
public sealed class AppleVideoDecoderTests {

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase("rpza")]
  [TestCase("azpr")]
  [TestCase("RPZA")]
  [TestCase("AZPR")]
  public void EveryCodeThisCodecHasShippedUnderIsTaken(string code)
    => Assert.That(AppleVideoDecoder.Accepts(_Stream(4, 4, code)), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsCodeIsNotTaken()
    => Assert.That(AppleVideoDecoder.Accepts(_Stream(4, 4, "cvid")), Is.False);

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsNotTaken() {
    var sound = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("rpza") };

    Assert.That(AppleVideoDecoder.Accepts(sound), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream(4, 4, "rpza");

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Apple Video (RPZA)"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<AppleVideoDecoder>());
  }

  // ============================================================================================
  // Block order: left to right, top to bottom
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void BlocksRunLeftToRightThenTopToBottom() {
    // Four blocks, four different colours by single-colour opcodes: top-left, top-right,
    // bottom-left, bottom-right, in that order.
    var frame = _DecodeOne(
      _Stream(8, 8, "rpza"),
      _Header()
        .Concat(_Single(1, _Colour(31, 0, 0)))  // block 0: red
        .Concat(_Single(1, _Colour(0, 31, 0)))  // block 1: green
        .Concat(_Single(1, _Colour(0, 0, 31)))  // block 2: blue
        .Concat(_Single(1, _Colour(31, 31, 0))) // block 3: yellow
        .ToArray());

    Assert.That(_Pixel(frame, 0, 0), Is.EqualTo(new byte[] { 255, 0, 0 }), "block 0 is top left");
    Assert.That(_Pixel(frame, 0, 4), Is.EqualTo(new byte[] { 0, 255, 0 }), "block 1 is top right");
    Assert.That(_Pixel(frame, 4, 0), Is.EqualTo(new byte[] { 0, 0, 255 }), "block 2 is bottom left");
    Assert.That(_Pixel(frame, 4, 4), Is.EqualTo(new byte[] { 255, 255, 0 }), "block 3 is bottom right");
  }

  // ============================================================================================
  // The four-colour opcode: which colour is which, and the blend formula
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void FourColourIndexZeroIsColourBAndIndexThreeIsColourA() {
    // colourA is pure red, colourB is black. Index 3 (row 3, every column) must be colourA and
    // index 0 (row 0) must be colourB — the low end of the ramp is the colour given second.
    var data = _Header()
      .Concat(new byte[] {0xC0}) // 4-colour, 1 block
      .Concat(_ColourBytes(_Colour(31, 0, 0))) // colourA
      .Concat(_ColourBytes(_Colour(0, 0, 0)))  // colourB
      .Concat(new byte[] {0x00, 0x55, 0xAA, 0xFF});       // row0: idx0, row1: idx1, row2: idx2, row3: idx3
    var frame = _DecodeOne(_Stream(4, 4, "rpza"), data.ToArray());

    Assert.That(_Row(frame, 0), Is.EqualTo(_Repeat4(0, 0, 0)), "row 0: index 0 is colourB");
    Assert.That(_Row(frame, 3), Is.EqualTo(_Repeat4(255, 0, 0)), "row 3: index 3 is colourA");
  }

  [Test]
  [Category("Unit")]
  public void FourColourBlendsAreComputedAChannelAtATime() {
    // colourA red at full scale, colourB black: index 1 is (11*31+21*0)>>5 = 10, index 2 is
    // (21*31+11*0)>>5 = 20, each of the red channel and widened the same way every colour here is.
    var data = _Header()
      .Concat(new byte[] {0xC0})
      .Concat(_ColourBytes(_Colour(31, 0, 0)))
      .Concat(_ColourBytes(_Colour(0, 0, 0)))
      .Concat(new byte[] {0x00, 0x55, 0xAA, 0xFF});
    var frame = _DecodeOne(_Stream(4, 4, "rpza"), data.ToArray());

    Assert.That(_Row(frame, 1), Is.EqualTo(_Repeat4(_Widen(10), 0, 0)), "row 1: index 1 is the 11/21 blend");
    Assert.That(_Row(frame, 2), Is.EqualTo(_Repeat4(_Widen(20), 0, 0)), "row 2: index 2 is the 21/11 blend");
  }

  [Test]
  [Category("Unit")]
  public void FourColourFlagBytesAreOneARowMostSignificantPairFirst() {
    // Row 0's flag byte is 0b00_01_10_11: column 0 gets index 0, column 1 index 1, column 2 index
    // 2, column 3 index 3 — the top two bits of the byte are the leftmost pixel.
    var data = _Header()
      .Concat(new byte[] {0xC0})
      .Concat(_ColourBytes(_Colour(31, 0, 0)))
      .Concat(_ColourBytes(_Colour(0, 0, 0)))
      .Concat(new byte[] {0b00_01_10_11, 0xFF, 0xFF, 0xFF});
    var frame = _DecodeOne(_Stream(4, 4, "rpza"), data.ToArray());

    Assert.That(_Pixel(frame, 0, 0), Is.EqualTo(new byte[] { 0, 0, 0 }), "column 0: index 0");
    Assert.That(_Pixel(frame, 0, 1), Is.EqualTo(new byte[] { _Widen(10), 0, 0 }), "column 1: index 1");
    Assert.That(_Pixel(frame, 0, 2), Is.EqualTo(new byte[] { _Widen(20), 0, 0 }), "column 2: index 2");
    Assert.That(_Pixel(frame, 0, 3), Is.EqualTo(new byte[] { 255, 0, 0 }), "column 3: index 3");
  }

  // ============================================================================================
  // The special opcode — the byte that decides between its two variants
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheSpecialOpcodesBranchByteIsNotColourAsOwnLowByte() {
    // colourA's low byte has its own top bit set — the reading this decoder does not use would take
    // that as "four colours" and consume a completely different, shorter shape of data. The true
    // decider is the byte after it, which here has its top bit clear and selects sixteen colours;
    // every one of the fifteen extra colours is black, so only colourA's own pixel is not.
    var data = _Header()
      .Concat(new byte[] {0x00, 0x80}) // opcode 0x00 (special, colourA high=0x00), colourA low=0x80
      .Concat(new byte[30]); // fifteen more colours, all black — the first byte (0x00) is the true decider
    var frame = _DecodeOne(_Stream(4, 4, "rpza"), data.ToArray());

    Assert.That(_Pixel(frame, 0, 0), Is.EqualTo(new byte[] { 0, _Widen(4), 0 }), "colourA at the first pixel");
    Assert.That(_Pixel(frame, 0, 1), Is.EqualTo(new byte[] { 0, 0, 0 }), "every other pixel is black");
    Assert.That(_Pixel(frame, 3, 3), Is.EqualTo(new byte[] { 0, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void TheSpecialOpcodesFourColourVariantIsChosenByTheByteAfterColourA() {
    // colourA's own low byte has its top bit clear — the reading this decoder does not use would
    // take that as "sixteen colours". The true decider is the byte after it, here 0xFC, whose top
    // bit is set and which becomes the high byte of colourB (full-scale red).
    var data = _Header()
      .Concat(new byte[] {0x00, 0x00})          // opcode 0x00 (special, colourA high=0x00), colourA low=0x00 (black)
      .Concat(new byte[] {0xFC, 0x00})          // colourB: 0xFC00 = full-scale red
      .Concat(new byte[] {0b00_11_11_11, 0xFF, 0xFF, 0xFF}); // row0: col0 index0 (colourB), rest index3 (colourA)
    var frame = _DecodeOne(_Stream(4, 4, "rpza"), data.ToArray());

    Assert.That(_Pixel(frame, 0, 0), Is.EqualTo(new byte[] { 255, 0, 0 }), "index 0 is colourB, full-scale red");
    Assert.That(_Pixel(frame, 0, 1), Is.EqualTo(new byte[] { 0, 0, 0 }), "index 3 is colourA, black");
    Assert.That(_Pixel(frame, 3, 3), Is.EqualTo(new byte[] { 0, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void TheSpecialOpcodesSixteenColoursAreInRasterOrder() {
    var colours = new ushort[16];
    for (var i = 0; i < 16; ++i)
      colours[i] = _Colour(0, 0, (i + 1) % 32); // sixteen distinguishable colours, blue channel only

    var data = new List<byte>(_Header());
    data.Add((byte)(colours[0] >> 8));
    data.Add((byte)colours[0]);
    for (var i = 1; i < 16; ++i)
      data.AddRange(_ColourBytes(colours[i]));

    var frame = _DecodeOne(_Stream(4, 4, "rpza"), data.ToArray());

    for (var row = 0; row < 4; ++row)
    for (var column = 0; column < 4; ++column) {
      var index = row * 4 + column;
      Assert.That(
        _Pixel(frame, row, column)[2], Is.EqualTo(_Widen((index + 1) % 32)),
        $"pixel ({row},{column}) is colours[{index}]");
    }
  }

  // ============================================================================================
  // The inter-frame coding: skip, and the code point the documentation calls unused
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASkippedBlockIsLeftAsTheFrameBeforeLeftIt() {
    var stream = _Stream(8, 4, "rpza");
    var frame1 = _Header()
      .Concat(_Single(1, _Colour(31, 0, 0)))
      .Concat(_Single(1, _Colour(0, 31, 0)))
      .ToArray();
    var frame2 = _Header()
      .Concat(new byte[] {0x80}) // skip block 0
      .Concat(_Single(1, _Colour(0, 0, 31)))
      .ToArray();

    var frames = _Decode(stream, [frame1, frame2]);

    Assert.That(_Pixel(frames[1], 0, 0), Is.EqualTo(new byte[] { 255, 0, 0 }), "the skipped block");
    Assert.That(_Pixel(frames[1], 0, 4), Is.EqualTo(new byte[] { 0, 0, 255 }), "the repainted block");
  }

  [Test]
  [Category("Unit")]
  public void ASkipOnTheVeryFirstFrameLeavesTheStartingCanvasBlack() {
    var frame = _DecodeOne(_Stream(4, 4, "rpza"), _Header().Concat(new byte[] {0x80}).ToArray());

    Assert.That(_Pixel(frame, 0, 0), Is.EqualTo(new byte[] { 0, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void TheCodePointTheDocumentationCallsUnusedBehavesAsSkip() {
    var stream = _Stream(8, 4, "rpza");
    var frame1 = _Header().Concat(_Single(2, _Colour(31, 0, 0))).ToArray();
    var frame2 = _Header().Concat(new byte[] {0xE0}).Concat(_Single(1, _Colour(0, 31, 0))).ToArray();

    var frames = _Decode(stream, [frame1, frame2]);

    Assert.That(_Pixel(frames[1], 0, 0), Is.EqualTo(new byte[] { 255, 0, 0 }), "0xE0 leaves the block alone");
    Assert.That(_Pixel(frames[1], 0, 4), Is.EqualTo(new byte[] { 0, 255, 0 }));
  }

  // ============================================================================================
  // Geometry that is not a whole number of blocks
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APictureNotAWholeNumberOfBlocksIsPaddedAndCropped() {
    // 6x5: two blocks across, two rows, with the last column and row of each padding block cropped
    // off. All four blocks of the padded grid are painted so nothing is left unaccounted for.
    var data = _Header()
      .Concat(_Single(4, _Colour(31, 0, 0)))
      .ToArray();
    var frame = _DecodeOne(_Stream(6, 5, "rpza"), data);

    Assert.That(frame.Width, Is.EqualTo(6));
    Assert.That(frame.Height, Is.EqualTo(5));
    Assert.That(_Pixel(frame, 4, 5), Is.EqualTo(new byte[] { 255, 0, 0 }), "the last visible row and column");
  }

  // ============================================================================================
  // The refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APictureSizeOfZeroIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(() => AppleVideoDecoder.Create(_Stream(0, 4, "rpza")));

    Assert.That(failure!.Message, Does.Contain("0x4"));
  }

  [Test]
  [Category("Unit")]
  public void AChunkShorterThanTheFourByteHeaderIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(() => _DecodeOne(_Stream(4, 4, "rpza"), [0xE1, 0x00]));

    Assert.That(failure!.Message, Does.Contain("four-byte header"));
  }

  [Test]
  [Category("Unit")]
  public void AStandardOpcodesRunReachingPastTheLastBlockIsRefused() {
    var data = _Header().Concat(_Single(4, _Colour(0, 0, 0))).ToArray(); // 4 blocks, picture holds 1
    var failure = Assert.Throws<InvalidDataException>(() => _DecodeOne(_Stream(4, 4, "rpza"), data));

    Assert.That(failure!.Message, Does.Contain("reaches past the last of the 1"));
  }

  [Test]
  [Category("Unit")]
  public void AChunkThatStopsBeforeEveryBlockIsAccountedForIsRefused() {
    // Two blocks in the picture, one painted, nothing said about the other.
    var data = _Header().Concat(_Single(1, _Colour(0, 0, 0))).ToArray();
    var failure = Assert.Throws<InvalidDataException>(() => _DecodeOne(_Stream(8, 4, "rpza"), data));

    Assert.That(failure!.Message, Does.Contain("ran out"));
  }

  [Test]
  [Category("Unit")]
  public void ASpecialOpcodeRunningOutOfColoursIsRefused() {
    var data = _Header().Concat(new byte[] {0x00, 0x00}).Concat(new byte[10]).ToArray(); // 16-colour block, short
    var failure = Assert.Throws<InvalidDataException>(() => _DecodeOne(_Stream(4, 4, "rpza"), data));

    Assert.That(failure!.Message, Does.Contain("short of"));
  }

  // ============================================================================================
  // Through a container, end to end
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnAviOfAzprFramesDecodesThroughTheRegistry() {
    var frame = _Header().Concat(_Single(2, _Colour(0, 31, 0))).ToArray();
    var container = AviTestContainer.Build("azpr", 8, 4, 16, [frame]);

    var frames = VideoFormatRegistry.DecodeFrames(container).Select(f => f.Image).ToList();

    Assert.That(frames.Count, Is.EqualTo(1));
    Assert.That(_Pixel(frames[0], 0, 0), Is.EqualTo(new byte[] { 0, 255, 0 }));
    Assert.That(_Pixel(frames[0], 0, 7), Is.EqualTo(new byte[] { 0, 255, 0 }));
  }

  // ============================================================================================
  // Fixtures
  // ============================================================================================

  private static byte[] _Header() => [0xE1, 0x00, 0x00, 0x00];

  private static byte[] _Single(int count, ushort colour) {
    var result = new byte[3];
    result[0] = (byte)(0xA0 | (count - 1));
    result[1] = (byte)(colour >> 8);
    result[2] = (byte)colour;
    return result;
  }

  private static byte[] _ColourBytes(ushort colour) => [(byte)(colour >> 8), (byte)colour];

  private static ushort _Colour(int r, int g, int b) => (ushort)((r << 10) | (g << 5) | b);

  private static byte _Widen(int channel) => (byte)((channel << 3) | (channel >> 2));

  private static byte[] _Repeat4(byte r, byte g, byte b) => [r, g, b, r, g, b, r, g, b, r, g, b];

  private static MediaStreamInfo _Stream(int width, int height, string code) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(code),
    Width = width,
    Height = height,
  };

  private static RawImage _DecodeOne(MediaStreamInfo stream, byte[] frame) => _Decode(stream, [frame])[0];

  private static IReadOnlyList<RawImage> _Decode(MediaStreamInfo stream, IReadOnlyList<byte[]> frames) {
    var decoder = AppleVideoDecoder.Create(stream);
    var pictures = new List<RawImage>(frames.Count);

    foreach (var frame in frames)
      if (decoder.TryDecode(new(0, frame), out var picture))
        pictures.Add(picture);

    return pictures;
  }

  /// <summary>One row of an Rgb24 picture, three bytes a pixel, counted from the top.</summary>
  private static byte[] _Row(RawImage picture, int row)
    => picture.PixelData.AsSpan(row * picture.Width * 3, picture.Width * 3).ToArray();

  /// <summary>One pixel of an Rgb24 picture as red, green and blue.</summary>
  private static byte[] _Pixel(RawImage picture, int row, int column)
    => picture.PixelData.AsSpan((row * picture.Width + column) * 3, 3).ToArray();
}
