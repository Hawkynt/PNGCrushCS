using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The Apple Graphics (SMC) decoder, on chunks built here byte by byte.
/// </summary>
/// <remarks>
/// Eight real streams were settled against ffmpeg — 950 frames in all, none differing anywhere,
/// covering every opcode the format defines, both the colour and the greyscale standard tables, and
/// every one of the six real streams' own colour table descriptions, including a stream whose only
/// use of the "repeat previous 2 blocks" opcode straddles a row of blocks. What these tests add is
/// what a whole-stream comparison does not isolate on its own: the flag layouts one at a time, the
/// block order, the per-packet cache reset, the standard colour and greyscale tables at chosen
/// indices, and the refusals — none of which a real stream happens to exercise in isolation.
/// </remarks>
[TestFixture]
public sealed class AppleGraphicsDecoderTests {

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase("smc ")]
  [TestCase("SMC ")]
  public void EveryCodeThisCodecHasShippedUnderIsTaken(string code)
    => Assert.That(AppleGraphicsDecoder.Accepts(_Stream(4, 4, code: code)), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsCodeIsNotTaken()
    => Assert.That(AppleGraphicsDecoder.Accepts(_Stream(4, 4, code: "cvid")), Is.False);

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsNotTaken() {
    var sound = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("smc ") };

    Assert.That(AppleGraphicsDecoder.Accepts(sound), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream(4, 4);

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Apple Graphics (SMC)"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<AppleGraphicsDecoder>());
  }

  // ============================================================================================
  // Block order: left to right, top to bottom
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void BlocksRunLeftToRightThenTopToBottom() {
    var frame = _DecodeOne(
      _Stream(8, 8),
      _Header()
        .Concat(_OneColourInline(1, 10))
        .Concat(_OneColourInline(1, 20))
        .Concat(_OneColourInline(1, 30))
        .Concat(_OneColourInline(1, 40))
        .ToArray());

    Assert.That(_Index(frame, 0, 0), Is.EqualTo(10), "block 0 is top left");
    Assert.That(_Index(frame, 0, 4), Is.EqualTo(20), "block 1 is top right");
    Assert.That(_Index(frame, 4, 0), Is.EqualTo(30), "block 2 is bottom left");
    Assert.That(_Index(frame, 4, 4), Is.EqualTo(40), "block 3 is bottom right");
  }

  // ============================================================================================
  // One colour, both count forms
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void OneColourInlinePaintsARunOfBlocksTheSameIndex() {
    var frame = _DecodeOne(_Stream(8, 4), _Header().Concat(_OneColourInline(2, 77)).ToArray());

    Assert.That(_Index(frame, 0, 0), Is.EqualTo(77));
    Assert.That(_Index(frame, 3, 7), Is.EqualTo(77));
  }

  [Test]
  [Category("Unit")]
  public void OneColourByteCountsPastSixteenBlocks() {
    var frame = _DecodeOne(_Stream(80, 4), _Header().Concat(_OneColourByte(20, 5)).ToArray());

    Assert.That(_Index(frame, 0, 0), Is.EqualTo(5));
    Assert.That(_Index(frame, 0, 79), Is.EqualTo(5));
  }

  // ============================================================================================
  // Two colours: the flag byte layout and both the explicit and cached forms
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TwoColourFlagsCoverTwoRowsAByte() {
    // a = top two rows (0xF0 -> row0 all colour1, row1 all colour0); b = bottom two rows
    // (0x0F -> row2 all colour0, row3 all colour1).
    var frame = _DecodeOne(
      _Stream(4, 4),
      _Header().Concat(_TwoColourNew(1, 5, 9, [0xF0, 0x0F])).ToArray());

    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 9, 9, 9, 9 }));
    Assert.That(_Row(frame, 1), Is.EqualTo(new byte[] { 5, 5, 5, 5 }));
    Assert.That(_Row(frame, 2), Is.EqualTo(new byte[] { 5, 5, 5, 5 }));
    Assert.That(_Row(frame, 3), Is.EqualTo(new byte[] { 9, 9, 9, 9 }));
  }

  [Test]
  [Category("Unit")]
  public void TwoColourCachedReadsTheEntryANewOpcodeStoredEarlierInTheSamePacket() {
    var data = _Header()
      .Concat(_TwoColourNew(1, 3, 4, [0x00, 0x00])) // block 0, stores pair (3,4) at cache slot 0
      .Concat(_TwoColourCached(1, 0, [0xFF, 0xFF])); // block 1, cache index 0 -> (3,4), all colour1
    var frame = _DecodeOne(_Stream(8, 4), data.ToArray());

    Assert.That(_Index(frame, 0, 4), Is.EqualTo(4));
  }

  // ============================================================================================
  // Four colours: the flag byte layout, direct indexing, both forms
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void FourColourIndexesDirectlyIntoTheGivenColoursNoSwap() {
    // Row 0's flag byte 0b00_01_10_11 picks index 0, 1, 2, 3 left to right.
    var data = _Header().Concat(_FourColourNew(1, [20, 21, 22, 23], [0b00_01_10_11, 0xFF, 0xFF, 0xFF]));
    var frame = _DecodeOne(_Stream(4, 4), data.ToArray());

    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 20, 21, 22, 23 }));
    Assert.That(_Row(frame, 1), Is.EqualTo(new byte[] { 23, 23, 23, 23 }));
  }

  [Test]
  [Category("Unit")]
  public void FourColourCachedReadsAnEarlierEntry() {
    var data = _Header()
      .Concat(_FourColourNew(1, [1, 2, 3, 4], [0x00, 0x00, 0x00, 0x00])) // block 0, slot 0
      .Concat(_FourColourCached(1, 0, [0xFF, 0xFF, 0xFF, 0xFF])); // block 1, index 3 -> colour 4
    var frame = _DecodeOne(_Stream(8, 4), data.ToArray());

    Assert.That(_Index(frame, 0, 4), Is.EqualTo(4));
  }

  // ============================================================================================
  // Eight colours: the nibble-shuffled flag layout
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EightColourFlagsAreTheNibbleShuffleTheDocumentDescribes() {
    // The worked example from the format's own documentation: six bytes 01 23 45 67 89 AB, an octet
    // of eight distinguishable colours, and the sixteen resulting indices computed independently
    // (see the remark on AppleGraphicsDecoder._PaintEightColour for the nibble arithmetic).
    Span<byte> octet = [10, 11, 12, 13, 14, 15, 16, 17];
    var data = _Header().Concat(_EightColourNew(1, octet.ToArray(), [0x01, 0x23, 0x45, 0x67, 0x89, 0xAB]));
    var frame = _DecodeOne(_Stream(4, 4), data.ToArray());

    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 10, 10, 12, 12 }));
    Assert.That(_Row(frame, 1), Is.EqualTo(new byte[] { 12, 11, 12, 16 }));
    Assert.That(_Row(frame, 2), Is.EqualTo(new byte[] { 14, 12, 13, 12 }));
    Assert.That(_Row(frame, 3), Is.EqualTo(new byte[] { 11, 15, 17, 13 }));
  }

  [Test]
  [Category("Unit")]
  public void EightColourCachedReadsAnEarlierEntry() {
    Span<byte> octet = [1, 2, 3, 4, 5, 6, 7, 8];
    var data = _Header()
      .Concat(_EightColourNew(1, octet.ToArray(), [0, 0, 0, 0, 0, 0])) // block 0, slot 0, all index 0 -> colour 1
      .Concat(_EightColourCached(1, 0, [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF])); // block 1, all index 7 -> colour 8
    var frame = _DecodeOne(_Stream(8, 4), data.ToArray());

    Assert.That(_Index(frame, 0, 0), Is.EqualTo(1));
    Assert.That(_Index(frame, 0, 4), Is.EqualTo(8));
  }

  // ============================================================================================
  // Sixteen colours: raw, unshared, raster order
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void SixteenColourIsSixteenRawIndicesInRasterOrder() {
    byte[] raw = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];
    var frame = _DecodeOne(_Stream(4, 4), _Header().Concat(_Sixteen(1, raw)).ToArray());

    for (var row = 0; row < 4; ++row)
      Assert.That(_Row(frame, row), Is.EqualTo(raw.AsSpan(row * 4, 4).ToArray()), $"row {row}");
  }

  // ============================================================================================
  // The inter-frame coding: skip and repeat-one
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASkippedBlockIsLeftAsTheFrameBeforeLeftIt() {
    var stream = _Stream(8, 4);
    var frame1 = _Header().Concat(_OneColourInline(1, 7)).Concat(_OneColourInline(1, 8)).ToArray();
    var frame2 = _Header().Concat(_SkipInline(1)).Concat(_OneColourInline(1, 9)).ToArray();

    var frames = _Decode(stream, [frame1, frame2]);

    Assert.That(_Index(frames[1], 0, 0), Is.EqualTo(7), "the skipped block");
    Assert.That(_Index(frames[1], 0, 4), Is.EqualTo(9));
  }

  [Test]
  [Category("Unit")]
  public void ASkipOnTheVeryFirstFrameLeavesTheStartingCanvasAtPaletteIndexZero() {
    var frame = _DecodeOne(_Stream(4, 4), _Header().Concat(_SkipInline(1)).ToArray());

    Assert.That(_Index(frame, 0, 0), Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void SkipByteCountsPastSixteenBlocks() {
    var frame = _DecodeOne(_Stream(80, 4), _Header().Concat(_SkipByte(20)).ToArray());

    Assert.That(_Index(frame, 0, 0), Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void RepeatOneCopiesTheBlockImmediatelyBeforeIntoEachOfTheRunsPositions() {
    var data = _Header()
      .Concat(_OneColourInline(1, 42)) // block 0
      .Concat(_RepeatOneInline(2));    // blocks 1, 2 both become a copy of block 0
    var frame = _DecodeOne(_Stream(12, 4), data.ToArray());

    Assert.That(_Index(frame, 0, 4), Is.EqualTo(42));
    Assert.That(_Index(frame, 0, 8), Is.EqualTo(42));
  }

  [Test]
  [Category("Unit")]
  public void RepeatOneByteCountsPastSixteenBlocks() {
    var data = _Header()
      .Concat(_OneColourInline(1, 42))
      .Concat(_RepeatOneByte(20));
    var frame = _DecodeOne(_Stream(84, 4), data.ToArray());

    Assert.That(_Index(frame, 0, 83), Is.EqualTo(42));
  }

  [Test]
  [Category("Unit")]
  public void RepeatTwoCopiesTheTwoBlocksImmediatelyBeforeIntoEachPairOfTheRunsPositions() {
    // blocks 0, 1 = 11, 22; 0x40 (inline, one pair) repeats them into blocks 2, 3.
    var data = _Header()
      .Concat(_OneColourInline(1, 11))
      .Concat(_OneColourInline(1, 22))
      .Concat(_RepeatTwoInline(1));
    var frame = _DecodeOne(_Stream(16, 4), data.ToArray());

    Assert.That(_Index(frame, 0, 8), Is.EqualTo(11));
    Assert.That(_Index(frame, 0, 12), Is.EqualTo(22));
  }

  [Test]
  [Category("Unit")]
  public void RepeatTwoByteCountsPastSixteenPairs() {
    // Blocks 0, 1 painted directly; 0x50 with a byte count of 20 repeats them as 20 pairs, blocks
    // 2 to 41 — 42 blocks in all.
    var data = _Header()
      .Concat(_OneColourInline(1, 11))
      .Concat(_OneColourInline(1, 22))
      .Concat(_RepeatTwoByte(20));
    var frame = _DecodeOne(_Stream(168, 4), data.ToArray());

    Assert.That(_Index(frame, 0, 160), Is.EqualTo(11), "block 40, the first of the last repeated pair");
    Assert.That(_Index(frame, 0, 164), Is.EqualTo(22), "block 41, the second of the last repeated pair");
  }

  [Test]
  [Category("Unit")]
  public void RepeatTwoReadsItsSourceFromWhicheverBlockRowThePairFallsInEvenWhenItCrossesOne() {
    // 4 blocks across, 2 rows. Blocks 2, 3 (the last two of row 0) = 11, 22; 0x40 at block 4 (the
    // first of row 1) repeats them across the row boundary into blocks 4, 5.
    var data = _Header()
      .Concat(_SkipInline(2))          // blocks 0, 1: unpainted (black)
      .Concat(_OneColourInline(1, 11)) // block 2
      .Concat(_OneColourInline(1, 22)) // block 3
      .Concat(_RepeatTwoInline(1))     // blocks 4, 5, crossing into row 1
      .Concat(_SkipInline(2));         // blocks 6, 7
    var frame = _DecodeOne(_Stream(16, 8), data.ToArray());

    Assert.That(_Index(frame, 4, 0), Is.EqualTo(11), "row 1, first block, copied across the row boundary");
    Assert.That(_Index(frame, 4, 4), Is.EqualTo(22), "row 1, second block");
  }

  // ============================================================================================
  // The colour caches: reset before every packet
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ACachedReferenceDoesNotReachAnEntryFromTheFrameBefore() {
    var stream = _Stream(8, 4);
    var frame1 = _Header().Concat(_TwoColourNew(1, 7, 8, [0xFF, 0xFF])).Concat(_SkipInline(1)).ToArray();
    // frame2 names cache slot 0 without ever storing anything new this packet: the pair must read as
    // the reset value (0, 0), not the (7, 8) frame1 stored.
    var frame2 = _Header().Concat(_TwoColourCached(1, 0, [0xFF, 0xFF])).Concat(_SkipInline(1)).ToArray();

    var frames = _Decode(stream, [frame1, frame2]);

    Assert.That(_Index(frames[0], 0, 0), Is.EqualTo(8), "frame 1's own pair");
    Assert.That(_Index(frames[1], 0, 0), Is.EqualTo(0), "frame 2's cache was reset, not carried over");
  }

  // ============================================================================================
  // The standard colour and greyscale tables, used when the description states none of its own
  // ============================================================================================

  [Test]
  [Category("Unit")]
  [TestCase((ushort)0xFFFF, TestName = "colour table ID states \"no table\"")]
  [TestCase((ushort)8, TestName = "colour table ID names the stream's own depth")]
  public void TheStandardColourTableIsUsedWhenNoTableIsPresent(ushort colourTableId) {
    var stream = _NoTableStream(4, 4, depth: 8, colourTableId: colourTableId);
    var decoder = AppleGraphicsDecoder.Create(stream);
    decoder.TryDecode(new(0, _Header().Concat(_OneColourInline(1, 0)).ToArray()), out var frame);

    // Index 0 is white, computed as (5 - 0/36)*51 for each of red, green and blue.
    Assert.That(_PaletteColour(frame, 0), Is.EqualTo(new byte[] { 255, 255, 255 }));
  }

  [Test]
  [Category("Unit")]
  public void TheStandardColourTablesLastEntryIsBlack() {
    var stream = _NoTableStream(4, 4, depth: 8, colourTableId: 0xFFFF);
    var decoder = AppleGraphicsDecoder.Create(stream);
    decoder.TryDecode(new(0, _Header().Concat(_OneColourInline(1, 255)).ToArray()), out var frame);

    Assert.That(_PaletteColour(frame, 255), Is.EqualTo(new byte[] { 0, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void TheStandardColourTablesSupplementaryRangeShadesOneChannelAtATime() {
    // Index 215 is the first of the ten supplementary red shades: the highest of the sixteen levels
    // that is not a multiple of three (14), scaled to eight bits by *17.
    var stream = _NoTableStream(4, 4, depth: 8, colourTableId: 0xFFFF);
    var decoder = AppleGraphicsDecoder.Create(stream);
    decoder.TryDecode(new(0, _Header().Concat(_OneColourInline(1, 215)).ToArray()), out var frame);

    Assert.That(_PaletteColour(frame, 215), Is.EqualTo(new byte[] { 238, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void TheGreyscaleTableIsUsedAtDepthFortyWhiteAtIndexZeroBlackAtTheLast() {
    var stream = _NoTableStream(4, 4, depth: 40, colourTableId: 40);
    var decoder = AppleGraphicsDecoder.Create(stream);
    decoder.TryDecode(new(0, _Header().Concat(_OneColourInline(1, 0)).ToArray()), out var frame);

    Assert.That(_PaletteColour(frame, 0), Is.EqualTo(new byte[] { 255, 255, 255 }));
    Assert.That(_PaletteColour(frame, 255), Is.EqualTo(new byte[] { 0, 0, 0 }));
    Assert.That(_PaletteColour(frame, 128), Is.EqualTo(new byte[] { 127, 127, 127 }));
  }

  // ============================================================================================
  // The refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APictureSizeOfZeroIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(() => AppleGraphicsDecoder.Create(_Stream(0, 4)));

    Assert.That(failure!.Message, Does.Contain("0x4"));
  }

  [Test]
  [Category("Unit")]
  public void AColourTableIdNamingAResourceThisLibraryCannotLookUpIsRefused() {
    var stream = _NoTableStream(4, 4, depth: 8, colourTableId: 99);
    var failure = Assert.Throws<NotSupportedException>(() => AppleGraphicsDecoder.Create(stream));

    Assert.That(failure!.Message, Does.Contain("99"));
  }

  [Test]
  [Category("Unit")]
  public void ADepthOtherThanEightIsRefused() {
    var stream = new MediaStreamInfo {
      Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("smc "),
      Width = 4, Height = 4, BitsPerPixel = 4,
      CodecPrivateData = _SampleEntry(4, 4, _Palette(4)),
    };
    var failure = Assert.Throws<NotSupportedException>(() => AppleGraphicsDecoder.Create(stream));

    Assert.That(failure!.Message, Does.Contain("eight bits"));
  }

  [Test]
  [Category("Unit")]
  public void AChunkShorterThanTheFourByteHeaderIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(() => _DecodeOne(_Stream(4, 4), [0xE1, 0x00]));

    Assert.That(failure!.Message, Does.Contain("four-byte header"));
  }

  [Test]
  [Category("Unit")]
  public void AnOpcodesRunReachingPastTheLastBlockIsRefused() {
    var data = _Header().Concat(_OneColourInline(4, 1)).ToArray(); // 4 blocks, picture holds 1
    var failure = Assert.Throws<InvalidDataException>(() => _DecodeOne(_Stream(4, 4), data));

    Assert.That(failure!.Message, Does.Contain("reaches past the last of the 1"));
  }

  [Test]
  [Category("Unit")]
  public void AChunkThatStopsBeforeEveryBlockIsAccountedForIsRefused() {
    var data = _Header().Concat(_OneColourInline(1, 1)).ToArray(); // 1 of 2 blocks
    var failure = Assert.Throws<InvalidDataException>(() => _DecodeOne(_Stream(8, 4), data));

    Assert.That(failure!.Message, Does.Contain("ran out"));
  }

  [Test]
  [Category("Unit")]
  public void ARepeatOpcodeWithNothingBeforeItIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(() => _DecodeOne(_Stream(4, 4), _Header().Concat(_RepeatOneInline(1)).ToArray()));

    Assert.That(failure!.Message, Does.Contain("none there"));
  }

  [Test]
  [Category("Unit")]
  public void ARepeatTwoOpcodeWithFewerThanTwoBlocksBeforeItIsRefused() {
    // Total holds room enough for the run itself; only one block (block 0) precedes it.
    var data = _Header().Concat(_OneColourInline(1, 1)).Concat(_RepeatTwoInline(1)).Concat(_SkipInline(9));
    var failure = Assert.Throws<InvalidDataException>(() => _DecodeOne(_Stream(48, 4), data.ToArray()));

    Assert.That(failure!.Message, Does.Contain("is only one"));
  }

  [Test]
  [Category("Unit")]
  public void UndefinedOpcode0xF0IsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => _DecodeOne(_Stream(4, 4), _Header().Concat(new byte[] { 0xF0 }).ToArray()));

    Assert.That(failure!.Message, Does.Contain("0xF0"));
  }

  // ============================================================================================
  // Fixtures: opcode bytes
  // ============================================================================================

  private static byte[] _Header() => [0xE1, 0x00, 0x00, 0x00];

  private static byte[] _SkipInline(int count) => [(byte)(0x00 | (count - 1))];
  private static byte[] _SkipByte(int count) => [0x10, (byte)(count - 1)];
  private static byte[] _RepeatOneInline(int count) => [(byte)(0x20 | (count - 1))];
  private static byte[] _RepeatOneByte(int count) => [0x30, (byte)(count - 1)];
  private static byte[] _RepeatTwoInline(int pairs) => [(byte)(0x40 | (pairs - 1))];
  private static byte[] _RepeatTwoByte(int pairs) => [0x50, (byte)(pairs - 1)];
  private static byte[] _OneColourInline(int count, byte index) => [(byte)(0x60 | (count - 1)), index];
  private static byte[] _OneColourByte(int count, byte index) => [0x70, (byte)(count - 1), index];

  private static byte[] _TwoColourNew(int count, byte i0, byte i1, byte[] flagsPerBlock)
    => [(byte)(0x80 | (count - 1)), i0, i1, .. flagsPerBlock];

  private static byte[] _TwoColourCached(int count, byte cacheIndex, byte[] flagsPerBlock)
    => [(byte)(0x90 | (count - 1)), cacheIndex, .. flagsPerBlock];

  private static byte[] _FourColourNew(int count, byte[] colours, byte[] flagsPerBlock)
    => [(byte)(0xA0 | (count - 1)), .. colours, .. flagsPerBlock];

  private static byte[] _FourColourCached(int count, byte cacheIndex, byte[] flagsPerBlock)
    => [(byte)(0xB0 | (count - 1)), cacheIndex, .. flagsPerBlock];

  private static byte[] _EightColourNew(int count, byte[] colours, byte[] flagsPerBlock)
    => [(byte)(0xC0 | (count - 1)), .. colours, .. flagsPerBlock];

  private static byte[] _EightColourCached(int count, byte cacheIndex, byte[] flagsPerBlock)
    => [(byte)(0xD0 | (count - 1)), cacheIndex, .. flagsPerBlock];

  private static byte[] _Sixteen(int count, byte[] rawPerBlock) => [(byte)(0xE0 | (count - 1)), .. rawPerBlock];

  // ============================================================================================
  // Fixtures: streams
  // ============================================================================================

  private static byte[] _Palette(int entries) {
    var palette = new byte[entries * 3];
    for (var i = 0; i < entries; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(i * 3);
      palette[i * 3 + 2] = (byte)(i * 5);
    }

    return palette;
  }

  /// <summary>A visual sample entry for an Apple Graphics stream, with a colour table when one is given.</summary>
  private static byte[] _SampleEntry(int width, int height, byte[]? palette) {
    var body = new List<byte>();
    body.AddRange(new byte[6]);                 // reserved
    body.AddRange([0, 1]);                      // data reference index
    body.AddRange(new byte[16]);                // version, revision, vendor, two qualities
    body.AddRange([(byte)(width >> 8), (byte)width, (byte)(height >> 8), (byte)height]);
    body.AddRange(new byte[8]);                 // resolutions
    body.AddRange(new byte[4]);                 // data size
    body.AddRange([0, 1]);                      // frame count
    body.AddRange(new byte[32]);                // compressor name
    body.AddRange([0, 8]);                      // depth: 8

    if (palette == null)
      body.AddRange([0xFF, 0xFF]);
    else {
      body.AddRange([0, 0]);
      var entries = palette.Length / 3;
      body.AddRange(new byte[4]);               // seed
      body.AddRange([0, 0]);                    // flags
      body.AddRange([(byte)((entries - 1) >> 8), (byte)(entries - 1)]);
      for (var i = 0; i < entries; ++i) {
        body.AddRange([0, 0]);
        body.AddRange([palette[i * 3], palette[i * 3]]);
        body.AddRange([palette[i * 3 + 1], palette[i * 3 + 1]]);
        body.AddRange([palette[i * 3 + 2], palette[i * 3 + 2]]);
      }
    }

    var box = new byte[8 + body.Count];
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(box.AsSpan(0, 4), box.Length);
    "smc "u8.CopyTo(box.AsSpan(4));
    body.CopyTo(box, 8);
    return box;
  }

  private static MediaStreamInfo _Stream(int width, int height, string code = "smc ") => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(code),
    Width = width,
    Height = height,
    CodecPrivateData = _SampleEntry(width, height, _Palette(256)),
  };

  /// <summary>
  /// A stream whose sample description states a depth and a colour table identifier and carries no
  /// table bytes at all — the shape most real Apple Graphics streams are in.
  /// </summary>
  private static MediaStreamInfo _NoTableStream(int width, int height, ushort depth, ushort colourTableId) {
    var body = new List<byte>();
    body.AddRange(new byte[6]);
    body.AddRange([0, 1]);
    body.AddRange(new byte[16]);
    body.AddRange([(byte)(width >> 8), (byte)width, (byte)(height >> 8), (byte)height]);
    body.AddRange(new byte[8]);
    body.AddRange(new byte[4]);
    body.AddRange([0, 1]);
    body.AddRange(new byte[32]);
    body.AddRange([(byte)(depth >> 8), (byte)depth]);
    body.AddRange([(byte)(colourTableId >> 8), (byte)colourTableId]);

    var box = new byte[8 + body.Count];
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(box.AsSpan(0, 4), box.Length);
    "smc "u8.CopyTo(box.AsSpan(4));
    body.CopyTo(box, 8);

    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("smc "),
      Width = width,
      Height = height,
      BitsPerPixel = depth,
      CodecPrivateData = box,
    };
  }

  private static RawImage _DecodeOne(MediaStreamInfo stream, byte[] frame) => _Decode(stream, [frame])[0];

  private static IReadOnlyList<RawImage> _Decode(MediaStreamInfo stream, IReadOnlyList<byte[]> frames) {
    var decoder = AppleGraphicsDecoder.Create(stream);
    var pictures = new List<RawImage>(frames.Count);

    foreach (var frame in frames)
      if (decoder.TryDecode(new(0, frame), out var picture))
        pictures.Add(picture);

    return pictures;
  }

  /// <summary>One row of an Indexed8 picture, one byte a pixel, counted from the top.</summary>
  private static byte[] _Row(RawImage picture, int row)
    => picture.PixelData.AsSpan(row * picture.Width, picture.Width).ToArray();

  /// <summary>One pixel of an Indexed8 picture, as its palette index.</summary>
  private static byte _Index(RawImage picture, int row, int column)
    => picture.PixelData[row * picture.Width + column];

  /// <summary>One entry of an Indexed8 picture's palette, as red, green and blue.</summary>
  private static byte[] _PaletteColour(RawImage picture, int index)
    => picture.Palette!.AsSpan(index * 3, 3).ToArray();
}
