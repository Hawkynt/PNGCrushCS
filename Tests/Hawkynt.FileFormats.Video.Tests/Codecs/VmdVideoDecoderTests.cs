using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The Sierra VMD video decoder's two rendering methods, its LZSS variant and the palette it reads
/// from the container's own header — on pictures built here byte by byte.
/// </summary>
/// <remarks>
/// Four real files — 197 pictures in all, covering every path this decoder reads — were decoded here
/// and by ffmpeg and compared sample for sample against ffmpeg's own <c>pal8</c> output: every picture
/// is identical. What that comparison cannot reach on demand is exercised here instead: each rendering
/// method in isolation, the LZSS chunk's two literal forms and its back-reference chain, and the exact
/// refusals a codec version, a render method or a preload-marker-less LZ chunk this decoder does not
/// read produce.
/// </remarks>
[TestFixture]
public sealed class VmdVideoDecoderTests {

  private const int _HEADER_LENGTH = 816;
  private const byte _METHOD_ROW_RUN_LENGTH = 1;
  private const byte _METHOD_PLAIN_COPY = 2;
  private const byte _LZ_FLAG = 0x80;

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheSierraVmdVideoCodeIsTaken()
    => Assert.That(VmdVideoDecoder.Accepts(_Stream(4, 4)), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsCodeIsNotTaken() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("cvid") };

    Assert.That(VmdVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsNotTaken() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("VMDV") };

    Assert.That(VmdVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream(4, 4);

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Sierra VMD Video"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<VmdVideoDecoder>());
  }

  // ============================================================================================
  // Creation
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ACodecVersionOtherThanOneRefuses() {
    var stream = _Stream(4, 4, codecVersion: 7);

    Assert.Throws<NotSupportedException>(() => VmdVideoDecoder.Create(stream));
  }

  [Test]
  [Category("Unit")]
  public void ThePaletteWidensSixBitVgaByRepeatingTheTopBitsRatherThanShifting() {
    var stream = _Stream(4, 4, paletteEntry0: (63, 32, 0));
    var decoder = VmdVideoDecoder.Create(stream);
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 3, _PlainCopy(4, 4, Enumerable.Repeat((byte)0, 16).ToArray()))), out var picture);

    Assert.That(picture.Palette![0], Is.EqualTo(255), "63 repeats its top two bits into the bottom: 0xFF, not 0xFC");
    Assert.That(picture.Palette[1], Is.EqualTo(ChannelScaling.Expand6(32)));
    Assert.That(picture.Palette[2], Is.EqualTo(0));
  }

  // ============================================================================================
  // Method 2: plain row-major copy
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void MethodTwoCopiesTheRectangleRowMajor() {
    var decoder = VmdVideoDecoder.Create(_Stream(2, 2));
    var pixels = new byte[] { 1, 2, 3, 4 }; // top-left, top-right, bottom-left, bottom-right
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 1, 1, _PlainCopy(2, 2, pixels))), out var picture);

    Assert.That(picture.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void MethodTwoOnlyPaintsItsOwnRectangleLeavingTheRestOfTheCanvasAlone() {
    var decoder = VmdVideoDecoder.Create(_Stream(4, 2));
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 1, _PlainCopy(4, 2, Enumerable.Repeat((byte)9, 8).ToArray()))), out _);

    decoder.TryDecode(new(0, _VideoPacket(0, 0, 1, 0, _PlainCopy(2, 1, [5, 6]))), out var picture);

    Assert.That(picture.PixelData[0], Is.EqualTo(5));
    Assert.That(picture.PixelData[1], Is.EqualTo(6));
    Assert.That(picture.PixelData[2], Is.EqualTo(9), "outside the second rectangle, the first frame's paint is untouched");
  }

  // ============================================================================================
  // Method 1: row-based run length, literal and skip
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void MethodOneLiteralRunPaintsTheGivenBytes() {
    var decoder = VmdVideoDecoder.Create(_Stream(4, 1));
    // control 0x83 = literal, length (3 & 0x7F) + 1 = 4
    var body = new byte[] { 0x83, 10, 20, 30, 40 };
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 0, _Method1(body))), out var picture);

    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 10, 20, 30, 40 }));
  }

  [Test]
  [Category("Unit")]
  public void MethodOneSkipRunLeavesThePreviousCanvasUntouched() {
    var decoder = VmdVideoDecoder.Create(_Stream(4, 1));
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 0, _Method1([0x83, 10, 20, 30, 40]))), out _);

    // control 0x03 = skip, length (3 & 0x7F) + 1 = 4: the whole row is left as it was.
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 0, _Method1([0x03]))), out var picture);

    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 10, 20, 30, 40 }));
  }

  [Test]
  [Category("Unit")]
  public void MethodOneMixesLiteralAndSkipAcrossOneRow() {
    var decoder = VmdVideoDecoder.Create(_Stream(4, 1));
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 0, _Method1([0x83, 10, 20, 30, 40]))), out _);

    // skip 2 (control 0x01), then literal 2 (control 0x81, bytes 99 98).
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 0, _Method1([0x01, 0x81, 99, 98]))), out var picture);

    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 10, 20, 99, 98 }));
  }

  [Test]
  [Category("Unit")]
  public void MethodOneRunsAreNotResetBetweenRows() {
    var decoder = VmdVideoDecoder.Create(_Stream(2, 2));
    // row 0: literal 2 (0x81, bytes 1 2). row 1: literal 2 (0x81, bytes 3 4), continuing the same stream.
    var body = new byte[] { 0x81, 1, 2, 0x81, 3, 4 };
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 1, 1, _Method1(body))), out var picture);

    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
  }

  [Test]
  [Category("Unit")]
  public void ALiteralRunReachingPastTheRectangleWidthRefuses() {
    var decoder = VmdVideoDecoder.Create(_Stream(2, 1));
    // control 0x83 states a literal run of four, twice the row's own width of two.
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _VideoPacket(0, 0, 1, 0, _Method1([0x83, 1, 2, 3, 4]))), out _));
  }

  // ============================================================================================
  // Method 3, a new palette, and an empty rectangle all refuse
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void MethodThreeRefuses() {
    var decoder = VmdVideoDecoder.Create(_Stream(2, 2));
    Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, _VideoPacket(0, 0, 1, 1, [3])), out _));
  }

  [Test]
  [Category("Unit")]
  public void ANewPaletteFlagRefuses() {
    var decoder = VmdVideoDecoder.Create(_Stream(2, 2));
    Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, _VideoPacket(0, 0, 1, 1, _PlainCopy(2, 2, [1, 2, 3, 4]), newPalette: true)), out _));
  }

  [Test]
  [Category("Unit")]
  public void AnEmptyRectanglePayloadRefuses() {
    var decoder = VmdVideoDecoder.Create(_Stream(2, 2));
    Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, _VideoPacket(0, 0, 1, 1, [])), out _));
  }

  // ============================================================================================
  // LZ decompression
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnLzChunksEightLiteralShortcutCopiesEightBytesVerbatim() {
    var decoder = VmdVideoDecoder.Create(_Stream(4, 2));
    var lzBody = _Lz(outputLength: 8, tagBytesAndData: [0xFF, 1, 2, 3, 4, 5, 6, 7, 8]);
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 1, _CompressedMethod(_METHOD_PLAIN_COPY, lzBody))), out var picture);

    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
  }

  [Test]
  [Category("Unit")]
  public void AnLzChunksBackReferenceRepeatsAlreadyDecodedBytes() {
    var decoder = VmdVideoDecoder.Create(_Stream(6, 1));
    // Tag 0x01 (bit0 set): one literal byte (7), landing at the preload form's own starting queue
    // position, 0x111. Then bit1 clear: a back-reference to that same position for five bytes,
    // repeating the literal five times.
    var tag = (byte)0b0000_0001;
    var offsetLow = 0x11;
    var offsetHighAndLength = (byte)(0x10 | (5 - 3)); // upper nibble 0x1 (offset bits 11-8), giving 0x111; length 5
    var lzBody = _Lz(outputLength: 6, tagBytesAndData: [tag, 7, (byte)offsetLow, offsetHighAndLength]);
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 5, 0, _CompressedMethod(_METHOD_PLAIN_COPY, lzBody))), out var picture);

    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 7, 7, 7, 7, 7, 7 }));
  }

  [Test]
  [Category("Unit")]
  public void AnLzChunkWithoutThePreloadMarkerRefuses() {
    var decoder = VmdVideoDecoder.Create(_Stream(4, 1));
    var noMarker = new byte[8];
    BinaryPrimitives.WriteUInt32LittleEndian(noMarker, 4);
    // bytes 4..7 are not the 34 12 78 56 marker.

    Assert.Throws<NotSupportedException>(() => decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 0, _CompressedMethod(_METHOD_PLAIN_COPY, noMarker))), out _));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Stream(int width, int height, int codecVersion = 1, (byte R, byte G, byte B)? paletteEntry0 = null) {
    var header = new byte[_HEADER_LENGTH];
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), (ushort)codecVersion);
    if (paletteEntry0 is { } entry) {
      header[28] = entry.R;
      header[29] = entry.G;
      header[30] = entry.B;
    }

    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("VMDV"),
      Width = width,
      Height = height,
      CodecPrivateData = header,
    };
  }

  private static byte[] _VideoPacket(int left, int top, int right, int bottom, byte[] payload, bool newPalette = false) {
    var record = new byte[16 + payload.Length];
    record[0] = 2; // video
    BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(2), (uint)payload.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6), (ushort)left);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8), (ushort)top);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(10), (ushort)right);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(12), (ushort)bottom);
    record[15] = newPalette ? (byte)0x02 : (byte)0;
    payload.CopyTo(record, 16);
    return record;
  }

  private static byte[] _PlainCopy(int width, int height, byte[] pixels) => [_METHOD_PLAIN_COPY, .. pixels];

  private static byte[] _Method1(byte[] body) => [_METHOD_ROW_RUN_LENGTH, .. body];

  private static byte[] _CompressedMethod(byte method, byte[] lzChunk) => [(byte)(_LZ_FLAG | method), .. lzChunk];

  /// <summary>Builds a VMD LZ chunk: a four-byte little-endian output length, the four-byte preload
  /// marker, and the tag/literal/back-reference stream verbatim.</summary>
  private static byte[] _Lz(int outputLength, byte[] tagBytesAndData) {
    var chunk = new byte[8 + tagBytesAndData.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(chunk, (uint)outputLength);
    chunk[4] = 0x34;
    chunk[5] = 0x12;
    chunk[6] = 0x78;
    chunk[7] = 0x56;
    tagBytesAndData.CopyTo(chunk, 8);
    return chunk;
  }
}
