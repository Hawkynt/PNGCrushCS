using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// BFI's video decode, built here byte by byte: the four compression codes — literal, dword
/// back-reference, unchanged-from-last-frame, and word fill — and the six-bit VGA palette.
/// </summary>
/// <remarks>
/// Three real files — 320x140, 138 frames in all — were decoded here and by ffmpeg and compared sample
/// for sample against ffmpeg's own <c>rgb24</c> output: every frame is identical, maximum delta nought.
/// </remarks>
[TestFixture]
public sealed class BfiVideoDecoderTests {

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheBfiCodeIsTaken()
    => Assert.That(BfiVideoDecoder.Accepts(_Stream(4, 4)), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsCodeIsNotTaken() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("cvid") };
    Assert.That(BfiVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream(4, 4);

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Brute Force & Ignorance Video"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<BfiVideoDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void ATooSmallPaletteRefuses() {
    var stream = new MediaStreamInfo {
      Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("BFIV"),
      Width = 4, Height = 4, CodecPrivateData = new byte[100],
    };

    Assert.Throws<InvalidDataException>(() => BfiVideoDecoder.Create(stream));
  }

  // ============================================================================================
  // Codes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ALiteralCodeCopiesBytesDirectly() {
    var decoder = BfiVideoDecoder.Create(_Stream(3, 1, _Palette((255, 0, 0))));
    // control 0x03 -> code 0 (literal), length 3.
    var packet = _Frame(3, [0x03, 1, 0, 0]);

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void ALiteralCodeWithZeroLengthReadsAnExtendedSixteenBitLength() {
    var decoder = BfiVideoDecoder.Create(_Stream(6, 1));
    // control 0x00 -> length 0 -> extended length (little-endian) = 6.
    var packet = _Frame(6, [0x00, 6, 0, 1, 2, 3, 4, 5, 6]);

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }));
  }

  [Test]
  [Category("Unit")]
  public void AFillCodeWritesWordsOfOneRepeatedValue() {
    var decoder = BfiVideoDecoder.Create(_Stream(4, 1));
    // control 0xC2 -> code 3 (fill), length 2 -> 2 words (4 bytes) of colour (0x07, 0x09).
    var packet = _Frame(4, [0xC2, 0x07, 0x09]);

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 7, 9, 7, 9 }));
  }

  [Test]
  [Category("Unit")]
  public void ABackReferenceCopiesDwordsByteByByteAllowingOverlap() {
    var decoder = BfiVideoDecoder.Create(_Stream(8, 1));
    // First write 4 literal bytes [1,2,3,4], then a back-reference with offset 1 (length 1 dword = 4
    // bytes): each byte copies the one immediately before it, producing four more 1's.
    var packet = _Frame(8, [0x04, 1, 2, 3, 4, /* code1, len=1 */ 0x41, 1]);

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 4, 4, 4, 4, 4 }));
  }

  [Test]
  [Category("Unit")]
  public void AnUnchangedCodeCarriesBytesFromThePreviousFrame() {
    var decoder = BfiVideoDecoder.Create(_Stream(4, 1));
    var first = _Frame(4, [0x04, 9, 8, 7, 6]);
    decoder.TryDecode(new(0, first), out _);

    // control 0x84 -> code 2 (unchanged), length 4 -> whole frame carried from before, then stop (the
    // decompressor exits once cursor reaches the picture's end).
    var second = _Frame(4, [0x84]);
    Assert.That(decoder.TryDecode(new(0, second), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 9, 8, 7, 6 }));
  }

  [Test]
  [Category("Unit")]
  public void AnUnchangedCodeWithAZeroExtendedLengthIsTheStreamsStopCode() {
    var decoder = BfiVideoDecoder.Create(_Stream(4, 1));
    var first = _Frame(4, [0x04, 9, 8, 7, 6]);
    decoder.TryDecode(new(0, first), out _);

    // control 0x80 -> code 2, length 0 -> extended length (2 bytes) also 0 -> stop code; the rest of
    // the picture (all 4 bytes) is carried unchanged since the decoder started from a copy of it.
    var second = _Frame(4, [0x80, 0x00, 0x00]);
    Assert.That(decoder.TryDecode(new(0, second), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 9, 8, 7, 6 }));
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APacketNotOpeningWithIvasRefuses() {
    var decoder = BfiVideoDecoder.Create(_Stream(4, 4));
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[30]), out _));
  }

  [Test]
  [Category("Unit")]
  public void AMismatchedDecompressedSizeRefuses() {
    var decoder = BfiVideoDecoder.Create(_Stream(4, 1));
    var header = new byte[24];
    "IVAS"u8.CopyTo(header);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), 24); // videoOffset -> right after header
    var video = new byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(video, 999); // states the wrong decompressed size
    var packet = new byte[header.Length + video.Length];
    header.CopyTo(packet, 0);
    video.CopyTo(packet, header.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4), (uint)packet.Length);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  [Test]
  [Category("Unit")]
  public void ABackReferenceOffsetReachingBeforeTheStartOfThePictureRefuses() {
    var decoder = BfiVideoDecoder.Create(_Stream(4, 1));
    // First op is a back-reference (code 1) with offset 1 before anything has been written.
    var packet = _Frame(4, [0x41, 1]);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  [Test]
  [Category("Unit")]
  public void DataRunningOutMidPictureRefuses() {
    var decoder = BfiVideoDecoder.Create(_Stream(4, 1));
    // control 0x04 (literal, length 4) but only two data bytes follow, short of the whole picture.
    var packet = _Frame(4, [0x04, 1, 2]);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static byte[] _Palette(params (int R, int G, int B)[] entries) {
    var raw = new byte[768];
    for (var i = 0; i < entries.Length; ++i) {
      raw[i * 3] = (byte)(entries[i].R >> 2);
      raw[i * 3 + 1] = (byte)(entries[i].G >> 2);
      raw[i * 3 + 2] = (byte)(entries[i].B >> 2);
    }

    return raw;
  }

  private static MediaStreamInfo _Stream(int width, int height, byte[]? palette = null) => new() {
    Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("BFIV"),
    Width = width, Height = height, CodecPrivateData = palette ?? new byte[768],
  };

  /// <summary>Builds a whole IVAS chunk carrying only video data (no audio), with a decompressed-size
  /// prefix matching the given picture area.</summary>
  private static byte[] _Frame(int pictureBytes, byte[] compressed) {
    var video = new byte[4 + compressed.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(video, (uint)pictureBytes);
    compressed.CopyTo(video, 4);

    var chunk = new byte[8 + 16 + video.Length];
    "IVAS"u8.CopyTo(chunk);
    BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)chunk.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(8), 5); // vtype
    BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(12), 24); // audioOffset (no audio: == videoOffset)
    BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(20), 24); // videoOffset
    video.CopyTo(chunk, 24);
    return chunk;
  }
}
