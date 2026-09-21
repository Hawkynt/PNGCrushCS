using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The Interplay video decoder's 8x8 block encodings and its two-buffer skip semantics, on pictures
/// built here byte by byte.
/// </summary>
/// <remarks>
/// Two real files — 432x320 and 640x272, 225 and 330 pictures, 555 in all — were decoded here and by
/// ffmpeg and compared sample for sample against ffmpeg's own <c>pal8</c> output: every picture, index
/// and installed palette both, is identical. What that comparison cannot reach on demand is exercised
/// here instead: individual block encodings in isolation, the fourteen-byte header nothing published
/// states, the low-bit-first pattern reading the format's own description states the other way round,
/// and — the finding this decoder rests on, arrived at independently of RoQ's but landing in the same
/// place — that a skipped block reaches back two pictures, not one, because Interplay's own encoder
/// alternates between two picture buffers and a skip is a block the encoder wrote nothing for.
/// </remarks>
[TestFixture]
public sealed class MveVideoDecoderTests {

  private const byte _INIT_VIDEO_BUFFERS = 0x05;
  private const byte _SET_PALETTE = 0x0C;
  private const byte _DECODING_MAP = 0x0F;
  private const byte _VIDEO_DATA = 0x11;
  private const byte _SEND_BUFFER = 0x07;

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheInterplayVideoCodeIsTaken()
    => Assert.That(MveVideoDecoder.Accepts(_Stream()), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsCodeIsNotTaken() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("cvid") };

    Assert.That(MveVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsNotTaken() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("IMVE") };

    Assert.That(MveVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream();

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Interplay Video"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<MveVideoDecoder>());
  }

  // ============================================================================================
  // The opcodes that produce no picture
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void InitBuffersPaletteAndMapProduceNoPicture() {
    var decoder = MveVideoDecoder.Create(_Stream());

    Assert.That(decoder.TryDecode(new(0, _InitVideoBuffers(1, 1)), out _), Is.False);
    Assert.That(decoder.TryDecode(new(0, _Palette(0, [63, 0, 0])), out _), Is.False);
    Assert.That(decoder.TryDecode(new(0, _DecodingMap([0xE])), out _), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void APictureBeforeInitBuffersRefuses() {
    var decoder = MveVideoDecoder.Create(_Stream());

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _VideoData(1, 1, [0])), out _));
  }

  [Test]
  [Category("Unit")]
  public void APictureBeforeADecodingMapRefuses() {
    var decoder = MveVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _InitVideoBuffers(1, 1)), out _);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _VideoData(1, 1, [0])), out _));
  }

  /// <summary>
  /// A second INIT_VIDEO_BUFFERS restates the geometry, and the decoder starts again at it.
  /// </summary>
  /// <remarks>
  /// This used to require a refusal, on the reasoning that a film does not change size part way
  /// through. FFmpeg disagrees and is the reference: <c>ipmovie.c</c> compares the stated width and
  /// height against the ones it holds, counts a change and adopts it, and has no path that rejects
  /// the opcode for differing. Refusing therefore made this package stricter than the only decoder
  /// there is, on a case neither of us has a sample of — all four films on
  /// <c>samples.ffmpeg.org</c> carry exactly one INIT_VIDEO_BUFFERS — so the stricter reading could
  /// only ever have turned a file FFmpeg plays into an exception.
  /// <para/>
  /// Adopting the new geometry means starting over, not resizing: both page buffers, the decoding
  /// and skip maps, the pending picture and both history frames all belong to the old size and none
  /// of them can be carried across. The assertions below are that the next picture arrives at the
  /// new geometry rather than the old one.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void APictureSizeThatChangesPartWayThroughStartsAgainAtTheNewSize() {
    var decoder = MveVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _InitVideoBuffers(1, 1)), out _);
    decoder.TryDecode(new(0, _DecodingMap([0xE])), out _);
    Assert.That(decoder.TryDecode(new(0, _VideoData(1, 1, [42])), out var first), Is.True);
    Assert.That(first.Width, Is.EqualTo(8));

    Assert.That(decoder.TryDecode(new(0, _InitVideoBuffers(2, 1)), out _), Is.False);

    decoder.TryDecode(new(0, _DecodingMap([0xE, 0xE])), out _);
    Assert.That(decoder.TryDecode(new(0, _VideoData(2, 1, [7, 9])), out var second), Is.True);

    Assert.Multiple(() => {
      Assert.That(second.Width, Is.EqualTo(16));
      Assert.That(second.Height, Is.EqualTo(8));
      Assert.That(_Index(second, 0, 0), Is.EqualTo(7));
      Assert.That(_Index(second, 8, 0), Is.EqualTo(9));
    });
  }

  /// <summary>
  /// A version-2 buffer opcode whose true-colour flag is set gives an RGB picture, not a refusal.
  /// </summary>
  /// <remarks>
  /// Refusing here is what this decoder used to do, and it is why a true-colour film could not be
  /// read at all. The flag is the only thing that distinguishes the RGB555 form from the palettised
  /// one, and the form is real: <c>descent3-level5-16bit.mve</c> on <c>samples.ffmpeg.org</c> sets
  /// it, and every one of its 1,624 pictures now decodes here byte for byte as FFmpeg decodes them.
  /// So the flag is read rather than rejected, and what comes back is RGB rather than palette
  /// indices, because a true-colour stream carries no palette to index into.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void ATrueColourBufferDecodesAsRgbRatherThanRefusing() {
    var decoder = MveVideoDecoder.Create(_Stream());

    Assert.That(decoder.TryDecode(new(0, _TrueColourVideoBuffers(1, 1)), out _), Is.False);

    var encoder = MveVideoEncoder.Create(new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("IMVE"),
      Width = 8,
      Height = 8,
      BitsPerPixel = 16,
    });
    var red = new byte[8 * 8 * 3];
    for (var i = 0; i < 8 * 8; ++i)
      red[i * 3] = 0xFF;

    Assert.That(
      encoder.TryEncode(
        new() { Width = 8, Height = 8, Format = PixelFormat.Rgb24, PixelData = red }, 0, out var packet),
      Is.True);
    Assert.That(decoder.TryDecode(packet, out var picture), Is.True);

    Assert.Multiple(() => {
      Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(picture.Palette, Is.Null);
      Assert.That(picture.PixelData, Is.EqualTo(red));
    });
  }

  // ============================================================================================
  // Block encodings, one at a time
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASolidBlockFillsTheWholePictureWithOneValue() {
    var decoder = MveVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _InitVideoBuffers(1, 1)), out _);
    decoder.TryDecode(new(0, _DecodingMap([0xE])), out _);

    Assert.That(decoder.TryDecode(new(0, _VideoData(1, 1, [42])), out var picture), Is.True);

    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(picture.PixelData, Is.EqualTo(Enumerable.Repeat((byte)42, 64)));
  }

  [Test]
  [Category("Unit")]
  public void ADitheredCheckerboardAlternatesTwoValues() {
    var decoder = MveVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _InitVideoBuffers(1, 1)), out _);
    decoder.TryDecode(new(0, _DecodingMap([0xF])), out _);
    decoder.TryDecode(new(0, _VideoData(1, 1, [10, 20])), out var picture);

    Assert.That(_Index(picture, 0, 0), Is.EqualTo(10));
    Assert.That(_Index(picture, 1, 0), Is.EqualTo(20));
    Assert.That(_Index(picture, 0, 1), Is.EqualTo(20));
    Assert.That(_Index(picture, 1, 1), Is.EqualTo(10));
  }

  [Test]
  [Category("Unit")]
  public void ARawBlockIsSixtyFourBytesOnePerPixelRowMajor() {
    var decoder = MveVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _InitVideoBuffers(1, 1)), out _);
    decoder.TryDecode(new(0, _DecodingMap([0xB])), out _);

    var pixels = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
    decoder.TryDecode(new(0, _VideoData(1, 1, pixels)), out var picture);

    Assert.That(_Index(picture, 0, 0), Is.EqualTo(0));
    Assert.That(_Index(picture, 7, 0), Is.EqualTo(7));
    Assert.That(_Index(picture, 0, 1), Is.EqualTo(8));
    Assert.That(_Index(picture, 7, 7), Is.EqualTo(63));
  }

  [Test]
  [Category("Unit")]
  public void A2x2RawCellFillsFourPixelsPerByte() {
    var decoder = MveVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _InitVideoBuffers(1, 1)), out _);
    decoder.TryDecode(new(0, _DecodingMap([0xC])), out _);

    // Sixteen cells, top left to bottom right: the first names the top left 2x2 corner.
    var cells = new byte[16];
    cells[0] = 77;
    decoder.TryDecode(new(0, _VideoData(1, 1, cells)), out var picture);

    Assert.That(_Index(picture, 0, 0), Is.EqualTo(77));
    Assert.That(_Index(picture, 1, 0), Is.EqualTo(77));
    Assert.That(_Index(picture, 0, 1), Is.EqualTo(77));
    Assert.That(_Index(picture, 1, 1), Is.EqualTo(77));
    Assert.That(_Index(picture, 2, 0), Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void A4x4RawCellFillsSixteenPixelsPerByte() {
    var decoder = MveVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _InitVideoBuffers(1, 1)), out _);
    decoder.TryDecode(new(0, _DecodingMap([0xD])), out _);

    var cells = new byte[] { 11, 22, 33, 44 }; // top left, top right, bottom left, bottom right
    decoder.TryDecode(new(0, _VideoData(1, 1, cells)), out var picture);

    Assert.That(_Index(picture, 0, 0), Is.EqualTo(11));
    Assert.That(_Index(picture, 3, 3), Is.EqualTo(11), "the whole 4x4 top left cell shares one value");
    Assert.That(_Index(picture, 4, 0), Is.EqualTo(22));
    Assert.That(_Index(picture, 0, 4), Is.EqualTo(33));
    Assert.That(_Index(picture, 4, 4), Is.EqualTo(44));
  }

  [Test]
  [Category("Unit")]
  public void ATwoColourBlockReadsBitsLowBitOfEachByteFirst() {
    // The format's own description states the opposite for this exact case; measured against real
    // files, low bit first is what reproduces them.
    var decoder = MveVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _InitVideoBuffers(1, 1)), out _);
    decoder.TryDecode(new(0, _DecodingMap([0x7])), out _);

    // P0=1 <= P1=9, so eight bytes follow, one a row. 0x01 in row 0 sets only the lowest bit.
    var body = new byte[] { 1, 9, 0b0000_0001, 0, 0, 0, 0, 0, 0, 0 };
    decoder.TryDecode(new(0, _VideoData(1, 1, body)), out var picture);

    Assert.That(_Index(picture, 0, 0), Is.EqualTo(9), "the low bit of the row's byte is the leftmost pixel");
    Assert.That(_Index(picture, 1, 0), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void EncodingSixRefuses() {
    var decoder = MveVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _InitVideoBuffers(1, 1)), out _);
    decoder.TryDecode(new(0, _DecodingMap([0x6])), out _);

    Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, _VideoData(1, 1, [])), out _));
  }

  // ============================================================================================
  // Motion compensation
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EncodingFourCopiesFromTheReferencePictureAtASymmetricOffset() {
    var decoder = MveVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _InitVideoBuffers(2, 1)), out _); // 16x8, two blocks side by side

    // Picture 0: left block value 5, right block value 9.
    decoder.TryDecode(new(0, _DecodingMap([0xE, 0xE])), out _);
    decoder.TryDecode(new(0, _VideoData(2, 1, [5, 9])), out var picture0);
    Assert.That(_Index(picture0, 0, 0), Is.EqualTo(5));
    Assert.That(_Index(picture0, 8, 0), Is.EqualTo(9));

    // Picture 1: left block is fresh solid 1; right block is encoding 4 with argument 0x87 — high
    // nibble 8 (dy = -8+8 = 0), low nibble 7 (dx = -8+7 = -1) — reading eight pixels starting one to
    // the left of where this block itself sits, straddling the boundary between the two blocks of
    // picture 0.
    decoder.TryDecode(new(0, _DecodingMap([0xE, 0x4])), out _);
    decoder.TryDecode(new(0, _VideoData(2, 1, [1, 0x87])), out var picture1);
    Assert.That(_Index(picture1, 8, 0), Is.EqualTo(5), "one pixel left of the block boundary is still picture 0's left block");
    Assert.That(_Index(picture1, 9, 0), Is.EqualTo(9), "the rest of the copied row is picture 0's right block");
  }

  [Test]
  [Category("Unit")]
  public void AMotionVectorReachingOutsideThePictureRefuses() {
    var decoder = MveVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _InitVideoBuffers(1, 1)), out _);
    decoder.TryDecode(new(0, _DecodingMap([0xE])), out _);
    decoder.TryDecode(new(0, _VideoData(1, 1, [7])), out _);

    // Encoding 4, argument 0x00: dx = -8+(0&0xF) = -8, dy = -8+(0>>4) = -8. Source (-8,-8): outside.
    decoder.TryDecode(new(0, _DecodingMap([0x4])), out _);
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _VideoData(1, 1, [0x00])), out _));
  }

  // ============================================================================================
  // Two picture buffers, and why a skip reaches back two pictures
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EncodingOneLeavesTheContentTwoPicturesBackRatherThanTheOneImmediatelyBefore() {
    var decoder = MveVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _InitVideoBuffers(1, 1)), out _);

    // Picture 0: solid 50 (fills the first buffer entirely).
    decoder.TryDecode(new(0, _DecodingMap([0xE])), out _);
    decoder.TryDecode(new(0, _VideoData(1, 1, [50])), out var picture0);
    Assert.That(_Index(picture0, 0, 0), Is.EqualTo(50));

    // Picture 1: solid 90 (fills the second buffer entirely).
    decoder.TryDecode(new(0, _DecodingMap([0xE])), out _);
    decoder.TryDecode(new(0, _VideoData(1, 1, [90])), out var picture1);
    Assert.That(_Index(picture1, 0, 0), Is.EqualTo(90));

    // Picture 2 targets the same buffer picture 0 did. Encoding 1 writes nothing at all, so this
    // should come back exactly as picture 0 was, not as picture 1.
    decoder.TryDecode(new(0, _DecodingMap([0x1])), out _);
    decoder.TryDecode(new(0, _VideoData(1, 1, [])), out var picture2);
    Assert.That(_Index(picture2, 0, 0), Is.EqualTo(50), "encoding 1 reaches two pictures back, not one");

    // Picture 3 targets the buffer picture 1 filled, and should likewise reach back to picture 1.
    decoder.TryDecode(new(0, _DecodingMap([0x1])), out _);
    decoder.TryDecode(new(0, _VideoData(1, 1, [])), out var picture3);
    Assert.That(_Index(picture3, 0, 0), Is.EqualTo(90));
  }

  [Test]
  [Category("Unit")]
  public void TheFirstPictureIsCopiedIntoBothBuffersSoEncodingOneThereMeansBlack() {
    var decoder = MveVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _InitVideoBuffers(1, 1)), out _);

    decoder.TryDecode(new(0, _DecodingMap([0x1])), out _);
    decoder.TryDecode(new(0, _VideoData(1, 1, [])), out var picture);

    Assert.That(_Index(picture, 0, 0), Is.EqualTo(0));
    Assert.That(_Index(picture, 7, 7), Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void EncodingZeroExplicitlyCopiesFromTheReferencePicture() {
    var decoder = MveVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _InitVideoBuffers(1, 1)), out _);

    decoder.TryDecode(new(0, _DecodingMap([0xE])), out _);
    decoder.TryDecode(new(0, _VideoData(1, 1, [33])), out _);

    decoder.TryDecode(new(0, _DecodingMap([0x0])), out _);
    decoder.TryDecode(new(0, _VideoData(1, 1, [])), out var picture);

    Assert.That(_Index(picture, 0, 0), Is.EqualTo(33));
  }

  // ============================================================================================
  // The palette
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void PaletteEntriesAreSixBitVgaWidenedByRepeatingTheTopBitsRatherThanShifting() {
    var decoder = MveVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _InitVideoBuffers(1, 1)), out _);
    decoder.TryDecode(new(0, _Palette(5, [63, 32, 0])), out _);
    decoder.TryDecode(new(0, _DecodingMap([0xE])), out _);
    decoder.TryDecode(new(0, _VideoData(1, 1, [5])), out var picture);

    Assert.That(picture.Palette![15], Is.EqualTo(255), "63 repeats its top two bits into the bottom: 0xFF, not 0xFC");
    Assert.That(picture.Palette[16], Is.EqualTo(ChannelScaling.Expand6(32)));
    Assert.That(picture.Palette[17], Is.EqualTo(0));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Stream() => new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("IMVE") };

  private static byte[] _Opcode(byte type, byte version, byte[] payload) {
    var packet = new byte[4 + payload.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(packet, (ushort)payload.Length);
    packet[2] = type;
    packet[3] = version;
    payload.CopyTo(packet, 4);
    return packet;
  }

  private static byte[] _InitVideoBuffers(int widthBlocks, int heightBlocks) {
    var payload = new byte[4];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)widthBlocks);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), (ushort)heightBlocks);
    return _Opcode(_INIT_VIDEO_BUFFERS, 0, payload);
  }

  /// <summary>A version-2 buffer opcode with the true-colour flag set.</summary>
  private static byte[] _TrueColourVideoBuffers(int widthBlocks, int heightBlocks) {
    var payload = new byte[8];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)widthBlocks);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), (ushort)heightBlocks);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6), 1); // true_color
    return _Opcode(_INIT_VIDEO_BUFFERS, 2, payload);
  }

  private static byte[] _Palette(int start, byte[] sixBitRgbTriplets) {
    var count = sixBitRgbTriplets.Length / 3;
    var payload = new byte[4 + sixBitRgbTriplets.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(payload, (ushort)start);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(2), (ushort)count);
    sixBitRgbTriplets.CopyTo(payload, 4);
    return _Opcode(_SET_PALETTE, 0, payload);
  }

  /// <summary>Packs one four-bit block encoding per element into a decoding map, low nibble first.</summary>
  private static byte[] _DecodingMap(int[] types) {
    var payload = new byte[(types.Length + 1) / 2];
    for (var i = 0; i < types.Length; ++i) {
      if (i % 2 == 0)
        payload[i / 2] |= (byte)(types[i] & 0xF);
      else
        payload[i / 2] |= (byte)((types[i] & 0xF) << 4);
    }

    return _Opcode(_DECODING_MAP, 0, payload);
  }

  /// <summary>
  /// A video-data opcode followed by the SEND_BUFFER that displays what it built, which is what a
  /// real MVE video chunk carries.
  /// </summary>
  /// <remarks>
  /// VIDEO_DATA reconstructs a page; it does not present one. Opcode 0x07 SEND_BUFFER is the display
  /// boundary in the public MVE description, in FFmpeg's demuxer and in ScummVM, and the decoder now
  /// returns a picture only when that opcode arrives — which is what lets a chunk hold a palette, a
  /// skip map, a decoding map and its data and still present exactly once.
  /// <para/>
  /// Every real file agrees: across the four samples on <c>samples.ffmpeg.org</c> there is exactly
  /// one SEND_BUFFER per video chunk and never one inside a chunk that also holds a second picture.
  /// So the opcode belongs here, in the helper that stands for "a chunk that codes a picture",
  /// rather than in each test. What each test asserts about the pixels it gets back is untouched.
  /// </remarks>
  private static byte[] _VideoData(int widthBlocks, int heightBlocks, byte[] blockData) {
    var payload = new byte[14 + blockData.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(8), (ushort)widthBlocks);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10), (ushort)heightBlocks);
    blockData.CopyTo(payload, 14);
    return [.. _Opcode(_VIDEO_DATA, 0, payload), .. _Opcode(_SEND_BUFFER, 1, [])];
  }

  private static byte _Index(RawImage picture, int x, int y) => picture.PixelData[y * picture.Width + x];
}
