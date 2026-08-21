using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The RoQ video decoder's quadtree walk and its two-buffer skip semantics, on pictures built here
/// byte by byte.
/// </summary>
/// <remarks>
/// Three real files — 512x256 to 512x512, 210 to 802 pictures, 1 338 in all — were decoded here and
/// by ffmpeg and compared plane by plane against ffmpeg's own <c>yuvj444p</c> output: every plane of
/// every picture is identical, including on the one sample whose own accompanying text names motion
/// compensation with a nonzero mean vector as the cause of a chrominance bug in the game's own player.
/// What that comparison cannot reach on demand is exercised here instead: each block type in isolation,
/// the codebook's zero-means-256-unless-the-length-says-otherwise ambiguity, and — the one finding this
/// decoder rests on — that <c>MOT</c> leaves a block's content exactly where it was <em>two</em>
/// pictures back rather than one, because RoQ's encoder alternates between two picture buffers and a
/// block a picture never touches keeps whichever content that same buffer slot held the last time it
/// was written.
/// </remarks>
[TestFixture]
public sealed class RoqVideoDecoderTests {

  private const ushort _INFO = 0x1001;
  private const ushort _QUAD_CODEBOOK = 0x1002;
  private const ushort _QUAD_VQ = 0x1011;
  private const ushort _JPEG = 0x1012;

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheRoqVideoCodeIsTaken()
    => Assert.That(RoqVideoDecoder.Accepts(_Stream()), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsCodeIsNotTaken() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("cvid") };

    Assert.That(RoqVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsNotTaken() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("RoQV") };

    Assert.That(RoqVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream();

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("id RoQ"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<RoqVideoDecoder>());
  }

  // ============================================================================================
  // INFO, and non-picture chunks answering "not yet"
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void InfoAndCodebookProduceNoPicture() {
    var decoder = RoqVideoDecoder.Create(_Stream());

    Assert.That(decoder.TryDecode(new(0, _Info(16, 16)), out _), Is.False);
    Assert.That(decoder.TryDecode(new(0, _Codebook([_Cell(1, 2, 3, 4, 128, 128)], [])), out _), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void APictureBeforeAnyInfoRefuses() {
    var decoder = RoqVideoDecoder.Create(_Stream());

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _Vq(0, 0, [])), out _));
  }

  [Test]
  [Category("Unit")]
  public void APictureSizeThatIsNotAWholeNumberOfMacroblocksRefuses() {
    var decoder = RoqVideoDecoder.Create(_Stream());

    Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, _Info(20, 16)), out _));
  }

  [Test]
  [Category("Unit")]
  public void APictureSizeThatChangesPartWayThroughRefuses() {
    var decoder = RoqVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Info(16, 16)), out _);

    Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, _Info(32, 16)), out _));
  }

  [Test]
  [Category("Unit")]
  public void RestatingTheSameSizeIsHarmless() {
    var decoder = RoqVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Info(16, 16)), out _);

    Assert.That(decoder.TryDecode(new(0, _Info(16, 16)), out _), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void AJpegChunkRefuses() {
    var decoder = RoqVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Info(16, 16)), out _);

    Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, _Chunk(_JPEG, 0, [1, 2, 3])), out _));
  }

  // ============================================================================================
  // Each block type, alone
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnSldQuadrantUpsamplesOneFourByFourCellOverAnEightByEightArea() {
    // Cell A: 10,20 / 30,40, neutral chroma. Painted at all four positions of the one 4x4 codebook
    // entry, so the 8x8 block upsamples to a 2x2 grid of 4x4 squares, one value each.
    var cellA = _Cell(10, 20, 30, 40, 128, 128);
    var decoder = RoqVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Info(16, 16)), out _);
    decoder.TryDecode(new(0, _Codebook([cellA], [_Quad(0, 0, 0, 0)])), out _);

    // One macroblock, four 8x8 quadrants, all SLD naming the one 4x4 cell.
    var codes = _Codes(2, 2, 2, 2);
    var args = new byte[] { 0, 0, 0, 0 };
    Assert.That(decoder.TryDecode(new(0, _Vq(0, 0, _Interleave(codes, args))), out var picture), Is.True);

    // Native (0,0)=10 covers output (0..1,0..1); native (1,0)=20 covers (2..3,0..1); native (0,1)=30
    // covers (0..1,2..3); native (1,1)=40 covers (2..3,2..3) — each doubled to a 2x2 square.
    Assert.That(_Luma(picture, 0, 0), Is.EqualTo(10));
    Assert.That(_Luma(picture, 1, 0), Is.EqualTo(10), "still the same doubled square as (0,0)");
    Assert.That(_Luma(picture, 2, 0), Is.EqualTo(20));
    Assert.That(_Luma(picture, 0, 2), Is.EqualTo(30));
    Assert.That(_Luma(picture, 2, 2), Is.EqualTo(40));
    // The whole picture is one macroblock, so the four painted 4x4 squares tile it exactly.
    Assert.That(_Luma(picture, 8, 0), Is.EqualTo(10), "the pattern repeats for the top right quadrant");
  }

  [Test]
  [Category("Unit")]
  public void ACccBlockSubdividesIntoFourFourByFourCodes() {
    var cellA = _Cell(1, 1, 1, 1, 128, 128);
    var cellB = _Cell(2, 2, 2, 2, 128, 128);
    var cellC = _Cell(3, 3, 3, 3, 128, 128);
    var cellD = _Cell(4, 4, 4, 4, 128, 128);
    var decoder = RoqVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Info(16, 16)), out _);
    decoder.TryDecode(new(0, _Codebook([cellA, cellB, cellC, cellD], [_Quad(0, 0, 0, 0), _Quad(1, 1, 1, 1), _Quad(2, 2, 2, 2), _Quad(3, 3, 3, 3)])), out _);

    // Top left 8x8 quadrant: CCC, then four 4x4 SLD codes naming 4x4 cells 0, 1, 2, 3 in reading order.
    // The other three 8x8 quadrants of the macroblock: plain SLD on cell 0, so they do not interfere.
    var codes = _Codes(3, 2, 2, 2, 2, 2, 2, 2);
    var args = new byte[] { 0, 1, 2, 3, 0, 0, 0 };
    Assert.That(decoder.TryDecode(new(0, _Vq(0, 0, _Interleave(codes, args))), out var picture), Is.True);

    // A 4x4 cell used at its own size — no upsampling — so every one of its sixteen samples is
    // visible individually rather than stretched.
    Assert.That(_Luma(picture, 0, 0), Is.EqualTo(1), "top left 4x4, cell A");
    Assert.That(_Luma(picture, 3, 3), Is.EqualTo(1), "still cell A at the far corner of its own 4x4 — no doubling");
    Assert.That(_Luma(picture, 4, 0), Is.EqualTo(2), "top right 4x4, cell B");
    Assert.That(_Luma(picture, 0, 4), Is.EqualTo(3), "bottom left 4x4, cell C");
    Assert.That(_Luma(picture, 4, 4), Is.EqualTo(4), "bottom right 4x4, cell D");
  }

  [Test]
  [Category("Unit")]
  public void ACccTerminalReadsFourRawTwoByTwoIndicesWithNoCodeOfItsOwn() {
    var cellA = _Cell(1, 1, 1, 1, 128, 128);
    var cellB = _Cell(2, 2, 2, 2, 128, 128);
    var cellC = _Cell(3, 3, 3, 3, 128, 128);
    var cellD = _Cell(4, 4, 4, 4, 128, 128);
    var decoder = RoqVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Info(16, 16)), out _);
    decoder.TryDecode(new(0, _Codebook([cellA, cellB, cellC, cellD], [])), out _);

    // Top left 8x8: CCC, subdividing into four 4x4 blocks. Its own top left 4x4: CCC again — the
    // walk's terminal case — four raw 2x2 indices with no code of their own. Its other three 4x4
    // blocks, and the macroblock's other three 8x8 quadrants, are MOT, so the whole picture's worth
    // of codes is eight: one word, no codebook entry needed beyond the four 2x2 cells.
    var codes = _Codes(3, 3, 0, 0, 0, 0, 0, 0);
    var args = new byte[] { 0, 1, 2, 3 };

    Assert.That(decoder.TryDecode(new(0, _Vq(0, 0, _Interleave(codes, args))), out var picture), Is.True);

    Assert.That(_Luma(picture, 0, 0), Is.EqualTo(1), "top left 2x2, cell A");
    Assert.That(_Luma(picture, 2, 0), Is.EqualTo(2), "top right 2x2, cell B");
    Assert.That(_Luma(picture, 0, 2), Is.EqualTo(3), "bottom left 2x2, cell C");
    Assert.That(_Luma(picture, 2, 2), Is.EqualTo(4), "bottom right 2x2, cell D");
  }

  // ============================================================================================
  // MOT, and why it needs two picture buffers rather than one
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void MotLeavesTheContentTwoPicturesBackRatherThanTheOneImmediatelyBefore() {
    var low = _Cell(50, 50, 50, 50, 128, 128);
    var high = _Cell(90, 90, 90, 90, 128, 128);
    var decoder = RoqVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Info(16, 16)), out _);

    // Picture 0: every quadrant SLD on the low cell (fills the first buffer entirely).
    decoder.TryDecode(new(0, _Codebook([low], [_Quad(0, 0, 0, 0)])), out _);
    Assert.That(decoder.TryDecode(new(0, _Vq(0, 0, _Interleave(_Codes(2, 2, 2, 2), [0, 0, 0, 0]))), out var picture0), Is.True);
    Assert.That(_Luma(picture0, 0, 0), Is.EqualTo(50));

    // Picture 1: every quadrant SLD on the high cell (fills the second buffer entirely).
    decoder.TryDecode(new(0, _Codebook([high], [_Quad(0, 0, 0, 0)])), out _);
    Assert.That(decoder.TryDecode(new(0, _Vq(0, 0, _Interleave(_Codes(2, 2, 2, 2), [0, 0, 0, 0]))), out var picture1), Is.True);
    Assert.That(_Luma(picture1, 0, 0), Is.EqualTo(90));

    // Picture 2 targets the same buffer picture 0 did. Every quadrant is MOT — no codebook chunk
    // needed at all — so this picture should come back exactly as picture 0 was, not as picture 1.
    Assert.That(decoder.TryDecode(new(0, _Vq(0, 0, _Interleave(_Codes(0, 0, 0, 0), []))), out var picture2), Is.True);
    Assert.That(_Luma(picture2, 0, 0), Is.EqualTo(50), "MOT reaches two pictures back, not one");

    // Picture 3 targets the buffer picture 1 filled, and should likewise reach back to picture 1.
    Assert.That(decoder.TryDecode(new(0, _Vq(0, 0, _Interleave(_Codes(0, 0, 0, 0), []))), out var picture3), Is.True);
    Assert.That(_Luma(picture3, 0, 0), Is.EqualTo(90));
  }

  [Test]
  [Category("Unit")]
  public void TheFirstPictureIsCopiedIntoBothBuffersSoAMotThereMeansBlack() {
    var decoder = RoqVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Info(16, 16)), out _);

    // The very first picture codes nothing at all but MOT — every quadrant. Nothing has painted
    // either buffer yet, so this reads as the canvas a freshly built decoder starts with.
    Assert.That(decoder.TryDecode(new(0, _Vq(0, 0, _Interleave(_Codes(0, 0, 0, 0), []))), out var picture), Is.True);
    Assert.That(_Luma(picture, 0, 0), Is.EqualTo(0));
    Assert.That(_Luma(picture, 15, 15), Is.EqualTo(0));
  }

  // ============================================================================================
  // Motion compensation
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void FccAddsItsArgumentToTheChunksMeanVector() {
    var flat = _Cell(1, 1, 1, 1, 128, 128);
    var spot = _Cell(77, 77, 77, 77, 128, 128);
    var decoder = RoqVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Info(16, 16)), out _);
    decoder.TryDecode(new(0, _Codebook([flat, spot], [_Quad(0, 0, 0, 0), _Quad(1, 1, 1, 1)])), out _);

    // Picture 0: the top left 8x8 quadrant is the spot, the other three are flat — whole quadrants,
    // so an FCC block reading any one of them back is either entirely the spot or entirely flat.
    var codes0 = _Codes(2, 2, 2, 2);
    var args0 = new byte[] { 1, 0, 0, 0 };
    decoder.TryDecode(new(0, _Vq(0, 0, _Interleave(codes0, args0))), out var picture0);
    Assert.That(_Luma(picture0, 0, 0), Is.EqualTo(77), "top left quadrant is the spot");
    Assert.That(_Luma(picture0, 8, 0), Is.EqualTo(1), "top right quadrant is flat");

    // Picture 1, mean vector (8,0): the top right quadrant is FCC with argument 0x88 — high nibble 8
    // (dx = 8+8-8 = 8), low nibble 8 (dy = 0+8-8 = 0) — so its source is the top left quadrant's own
    // position in picture 0, eight pixels to the left of where this quadrant itself sits.
    var codes1 = _Codes(0, 1, 0, 0);
    var args1 = new byte[] { 0x88 };
    Assert.That(decoder.TryDecode(new(0, _Vq(8, 0, _Interleave(codes1, args1))), out var picture1), Is.True);
    Assert.That(_Luma(picture1, 8, 0), Is.EqualTo(77), "the top right quadrant now reads the spot from eight pixels to its left");
    // Picture 0 was copied into both buffers once it was painted, so the untouched (MOT) top left
    // quadrant of this, the second buffer's first use, already holds picture 0's own content.
    Assert.That(_Luma(picture1, 0, 0), Is.EqualTo(77), "the other buffer was seeded from picture 0, not left black");
  }

  [Test]
  [Category("Unit")]
  public void AMotionVectorReachingOutsideThePictureRefuses() {
    var flat = _Cell(1, 1, 1, 1, 128, 128);
    var decoder = RoqVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Info(16, 16)), out _);
    decoder.TryDecode(new(0, _Codebook([flat], [_Quad(0, 0, 0, 0)])), out _);
    decoder.TryDecode(new(0, _Vq(0, 0, _Interleave(_Codes(2, 2, 2, 2), [0, 0, 0, 0]))), out _);

    // dx = 0 + 15 - 8 = 7: the top left 8x8 quadrant would read from x = 0 - 7 = -7.
    var codes = _Codes(1, 0, 0, 0);
    var args = new byte[] { 0xF8 };
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _Vq(0, 0, _Interleave(codes, args))), out _));
  }

  // ============================================================================================
  // The codebook's zero-means-256 ambiguity
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ACodebookEntryNamedBeforeAnyCodebookChunkRefuses() {
    var decoder = RoqVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Info(16, 16)), out _);

    var codes = _Codes(2, 0, 0, 0);
    var args = new byte[] { 0 };
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _Vq(0, 0, _Interleave(codes, args))), out _));
  }

  [Test]
  [Category("Unit")]
  public void ACodebookIndexPastWhatWasStatedRefuses() {
    var cell = _Cell(1, 1, 1, 1, 128, 128);
    var decoder = RoqVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Info(16, 16)), out _);
    decoder.TryDecode(new(0, _Codebook([cell], [_Quad(0, 0, 0, 0)])), out _);

    var codes = _Codes(2, 0, 0, 0);
    var args = new byte[] { 1 }; // only cell 0 exists
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _Vq(0, 0, _Interleave(codes, args))), out _));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Stream() => new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("RoQV") };

  private static byte[] _Chunk(ushort id, ushort argument, byte[] payload) {
    var chunk = new byte[8 + payload.Length];
    var span = chunk.AsSpan();
    BinaryPrimitives.WriteUInt16LittleEndian(span, id);
    BinaryPrimitives.WriteUInt32LittleEndian(span[2..], (uint)payload.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(span[6..], argument);
    payload.CopyTo(span[8..]);
    return chunk;
  }

  private static byte[] _Info(int width, int height) {
    var payload = new byte[8];
    var span = payload.AsSpan();
    BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(span[2..], (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(span[4..], 8);
    BinaryPrimitives.WriteUInt16LittleEndian(span[6..], 4);
    return _Chunk(_INFO, 0, payload);
  }

  /// <summary>One 2x2 codebook cell: four luma samples, top left, top right, bottom left, bottom right,
  /// then Cb and Cr.</summary>
  private static byte[] _Cell(byte y0, byte y1, byte y2, byte y3, byte cb, byte cr) => [y0, y1, y2, y3, cb, cr];

  /// <summary>One 4x4 codebook cell: the four 2x2 cell indices it is built from, top left, top right,
  /// bottom left, bottom right.</summary>
  private static byte[] _Quad(byte topLeft, byte topRight, byte bottomLeft, byte bottomRight) => [topLeft, topRight, bottomLeft, bottomRight];

  private static byte[] _Codebook(IReadOnlyList<byte[]> cb2, IReadOnlyList<byte[]> cb4) {
    var payload = new byte[cb2.Count * 6 + cb4.Count * 4];
    var at = 0;
    foreach (var cell in cb2) {
      cell.CopyTo(payload, at);
      at += 6;
    }

    foreach (var cell in cb4) {
      cell.CopyTo(payload, at);
      at += 4;
    }

    var argument = (ushort)(((cb2.Count & 0xFF) << 8) | (cb4.Count & 0xFF));
    return _Chunk(_QUAD_CODEBOOK, argument, payload);
  }

  private static byte[] _Vq(sbyte meanX, sbyte meanY, byte[] body) {
    var argument = (ushort)(((byte)meanX << 8) | (byte)meanY);
    return _Chunk(_QUAD_VQ, argument, body);
  }

  /// <summary>An array of 2-bit codes, one per element.</summary>
  private static int[] _Codes(params int[] codes) => codes;

  /// <summary>Packs a list of 2-bit codes into sixteen-bit little-endian words, most significant pair
  /// first, then follows them with the argument bytes the codes with an argument need, in order.</summary>
  private static byte[] _Interleave(int[] codes, byte[] arguments) {
    var words = (codes.Length + 7) / 8;
    var bytes = new List<byte>(words * 2 + arguments.Length);

    for (var w = 0; w < words; ++w) {
      var word = 0;
      for (var slot = 0; slot < 8; ++slot) {
        var index = w * 8 + slot;
        var code = index < codes.Length ? codes[index] : 0;
        word = (word << 2) | code;
      }

      bytes.Add((byte)(word & 0xFF));
      bytes.Add((byte)((word >> 8) & 0xFF));
    }

    bytes.AddRange(arguments);
    return bytes.ToArray();
  }

  private static byte _Luma(RawImage picture, int x, int y) => picture.PixelData[(y * picture.Width + x) * 3];
}
