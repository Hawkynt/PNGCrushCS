using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The Electronic Arts CMV decoder's block encodings, its two-picture motion reference and its
/// palette handling, on pictures built here byte by byte.
/// </summary>
/// <remarks>
/// The one sample known to exist for this codec, <c>TITLE.CMV</c> from
/// <c>samples.ffmpeg.org/game-formats/ea-cmv/</c> — 200x200, 194 pictures across two runs back to
/// back, the second opening with a fresh palette — was decoded here and by ffmpeg and compared sample
/// for sample against ffmpeg's own <c>rgb24</c> output: every picture is identical. What that
/// comparison cannot reach on demand is exercised here instead: each of the three block encodings in
/// isolation, the bootstrap that copies the first picture into both reference slots, a motion vector
/// reaching outside the picture, and every refusal this decoder makes by name.
/// </remarks>
[TestFixture]
public sealed class EaCmvVideoDecoderTests {

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheEaCmvCodeIsTaken()
    => Assert.That(EaCmvVideoDecoder.Accepts(_Stream()), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsCodeIsNotTaken() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("tgv ") };

    Assert.That(EaCmvVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsNotTaken() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("cmv ") };

    Assert.That(EaCmvVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream();

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Electronic Arts CMV"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<EaCmvVideoDecoder>());
  }

  // ============================================================================================
  // Intra pictures
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnIntraPictureIsARawRasterOfPaletteIndices() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Header(4, 4)), out _);

    var raster = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
    Assert.That(decoder.TryDecode(new(0, _IntraFrame(raster)), out var picture), Is.True);

    Assert.That(picture.PixelData, Is.EqualTo(raster));
    Assert.That(picture.Width, Is.EqualTo(4));
    Assert.That(picture.Height, Is.EqualTo(4));
    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Indexed8));
  }

  [Test]
  [Category("Unit")]
  public void ThePaletteIsPlainEightBitRgbInTheOrderTheHeaderStatesIt() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    var colours = new byte[768];
    colours[0] = 10; // red
    colours[1] = 20; // green
    colours[2] = 30; // blue
    decoder.TryDecode(new(0, _Header(4, 4, palStart: 0, palCount: 256, colours)), out _);

    decoder.TryDecode(new(0, _IntraFrame(_Fill(4, 4, 0))), out var picture);

    Assert.That(picture.Palette![0], Is.EqualTo(10));
    Assert.That(picture.Palette[1], Is.EqualTo(20));
    Assert.That(picture.Palette[2], Is.EqualTo(30));
  }

  [Test]
  [Category("Unit")]
  public void AHeaderMayRestateOnlyPartOfThePaletteLeavingTheRestAlone() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    var first = new byte[768];
    first[0] = 111;
    decoder.TryDecode(new(0, _Header(4, 4, palStart: 0, palCount: 256, first)), out _);

    var partial = new byte[3]; // one colour, at index 5 only
    partial[0] = 222;
    decoder.TryDecode(new(0, _Header(4, 4, palStart: 5, palCount: 1, partial)), out _);

    decoder.TryDecode(new(0, _IntraFrame(_Fill(4, 4, 0))), out var untouched);
    Assert.That(untouched.Palette![0], Is.EqualTo(111));

    decoder.TryDecode(new(0, _IntraFrame(_Fill(4, 4, 5))), out var restated);
    Assert.That(restated.Palette![15], Is.EqualTo(222));
  }

  // ============================================================================================
  // Inter pictures: the three block encodings
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AMotionByteThatIsNotAnEscapeCopiesFromTheLastPicture() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Header(4, 4)), out _);
    decoder.TryDecode(new(0, _IntraFrame(_Fill(4, 4, 9))), out _);

    // Zero motion: low nibble 7 minus 7, high nibble 7 minus 7.
    decoder.TryDecode(new(0, _InterFrame(motion: [0x77], escapes: [])), out var picture);

    Assert.That(picture.PixelData, Is.EqualTo(_Fill(4, 4, 9)));
  }

  [Test]
  [Category("Unit")]
  public void AnEscapeThatIsNotItselfAnEscapeCopiesFromTheSecondLastPicture() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Header(4, 4)), out _);
    decoder.TryDecode(new(0, _IntraFrame(_Fill(4, 4, 1))), out _); // A: both reference slots become 1

    // B, via a raw (double-escape) block rather than motion compensation, so its value is unrelated
    // to A's: after this, last = B (2), second-last = A (1) — the two slots now genuinely differ.
    var raw = _Fill(4, 4, 2);
    decoder.TryDecode(new(0, _InterFrame(motion: [0xFF], escapes: [.. new byte[] { 0xFF }, .. raw])), out _);

    // C reaches for "second-last" with a zero vector, which should read A (1) and not B (2).
    decoder.TryDecode(new(0, _InterFrame(motion: [0xFF], escapes: [0x77])), out var viaSecondLast);

    Assert.That(viaSecondLast.PixelData, Is.EqualTo(_Fill(4, 4, 1)));
  }

  [Test]
  [Category("Unit")]
  public void ADoubleEscapeReadsSixteenRawPixels() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Header(4, 4)), out _);
    decoder.TryDecode(new(0, _IntraFrame(_Fill(4, 4, 0))), out _);

    var raw = Enumerable.Range(100, 16).Select(i => (byte)i).ToArray();
    decoder.TryDecode(new(0, _InterFrame(motion: [0xFF], escapes: [.. new byte[] { 0xFF }, .. raw])), out var picture);

    Assert.That(picture.PixelData, Is.EqualTo(raw));
  }

  [Test]
  [Category("Unit")]
  public void TheFirstPictureIsCopiedIntoBothReferenceSlots() {
    // A picture whose very first inter frame reaches for "second-last" has nothing before the intra
    // picture to reach for, so it should read the intra picture itself rather than fail or read zero.
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Header(4, 4)), out _);
    decoder.TryDecode(new(0, _IntraFrame(_Fill(4, 4, 42))), out _);

    decoder.TryDecode(new(0, _InterFrame(motion: [0xFF], escapes: [0x77])), out var viaSecondLast);

    Assert.That(viaSecondLast.PixelData, Is.EqualTo(_Fill(4, 4, 42)));
  }

  [Test]
  [Category("Unit")]
  public void AMotionVectorReachingOutsideThePictureReadsZero() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Header(4, 4)), out _);
    decoder.TryDecode(new(0, _IntraFrame(_Fill(4, 4, 9))), out _);

    // Low nibble 0 minus 7 = -7: every source column falls outside the picture.
    decoder.TryDecode(new(0, _InterFrame(motion: [0x70], escapes: [])), out var picture);

    Assert.That(picture.PixelData, Is.EqualTo(_Fill(4, 4, 0)));
  }

  // ============================================================================================
  // Malformed packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APacketShorterThanAChunkHeaderRefuses() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[4]), out _));
  }

  [Test]
  [Category("Unit")]
  public void AnUnknownChunkKindRefuses() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    var packet = new byte[8];
    System.Text.Encoding.ASCII.GetBytes("XXXX").CopyTo(packet, 0);

    Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  [Test]
  [Category("Unit")]
  public void APictureArrivingBeforeAnyHeaderRefuses() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _IntraFrame([0])), out _));
  }

  [Test]
  [Category("Unit")]
  public void AnInterPictureArrivingBeforeAnyIntraPictureRefuses() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Header(4, 4)), out _);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _InterFrame(motion: [0x77], escapes: [])), out _));
  }

  [Test]
  [Category("Unit")]
  public void APictureSizeNotAWholeNumberOfFourPixelBlocksRefuses() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());

    var failure = Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, _Header(5, 4)), out _));
    Assert.That(failure!.Message, Does.Contain("4-pixel blocks"));
  }

  [Test]
  [Category("Unit")]
  public void APictureSizeThatChangesPartWayThroughAStreamRefuses() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Header(4, 4)), out _);

    Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, _Header(8, 8)), out _));
  }

  [Test]
  [Category("Unit")]
  public void AnIntraRasterShorterThanThePictureRefuses() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Header(4, 4)), out _);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _IntraFrame(new byte[4])), out _));
  }

  [Test]
  [Category("Unit")]
  public void AnEscapeBufferRunningOutMidBlockRefuses() {
    var decoder = EaCmvVideoDecoder.Create(_Stream());
    decoder.TryDecode(new(0, _Header(4, 4)), out _);
    decoder.TryDecode(new(0, _IntraFrame(_Fill(4, 4, 0))), out _);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _InterFrame(motion: [0xFF], escapes: [])), out _));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Stream() => new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("cmv ") };

  private static byte[] _Fill(int width, int height, byte value) {
    var buffer = new byte[width * height];
    Array.Fill(buffer, value);
    return buffer;
  }

  private static byte[] _Chunk(string fourCc, byte[] payload) {
    var chunk = new byte[8 + payload.Length];
    System.Text.Encoding.ASCII.GetBytes(fourCc).CopyTo(chunk, 0);
    BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)chunk.Length);
    payload.CopyTo(chunk, 8);
    return chunk;
  }

  private static byte[] _Header(int width, int height, int palStart = 0, int palCount = 0, byte[]? colours = null) {
    colours ??= [];
    var payload = new byte[0x10 + colours.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(4), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(6), (ushort)height);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(10), 10);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(12), (ushort)palStart);
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(14), (ushort)palCount);
    colours.CopyTo(payload, 0x10);
    return _Chunk("MVIh", payload);
  }

  private static byte[] _IntraFrame(byte[] raster) {
    var payload = new byte[2 + raster.Length];
    raster.CopyTo(payload, 2);
    return _Chunk("MVIf", payload);
  }

  private static byte[] _InterFrame(byte[] motion, byte[] escapes) {
    var payload = new byte[2 + motion.Length + escapes.Length];
    BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(0), 1);
    motion.CopyTo(payload, 2);
    escapes.CopyTo(payload, 2 + motion.Length);
    return _Chunk("MVIf", payload);
  }
}
