using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>Classic Sierra VMD method, palette, reference-picture and LZ behavior on byte-built packets.</summary>
[TestFixture]
public sealed class VmdVideoDecoderTests {

  private const int _HEADER_LENGTH = 816;
  private const byte _METHOD_ROW_RUN_LENGTH = 1;
  private const byte _METHOD_PLAIN_COPY = 2;
  private const byte _METHOD_PAIR_RLE = 3;
  private const byte _LZ_FLAG = 0x80;

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

  [Test]
  [Category("Unit")]
  public void ACodecVersionOtherThanOneRefuses() {
    var stream = _Stream(4, 4, codecVersion: 13);
    var failure = Assert.Throws<NotSupportedException>(() => VmdVideoDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("version 1"));
  }

  [Test]
  [Category("Unit")]
  public void ThePaletteWidensSixBitVgaByRepeatingTheTopBitsRatherThanShifting() {
    var stream = _Stream(4, 4, paletteEntry0: (63, 32, 0));
    var decoder = VmdVideoDecoder.Create(stream);
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 3, _PlainCopy(Enumerable.Repeat((byte)0, 16).ToArray()))), out var picture);

    Assert.That(picture.Palette![0], Is.EqualTo(255));
    Assert.That(picture.Palette[1], Is.EqualTo(ChannelScaling.Expand6(32)));
    Assert.That(picture.Palette[2], Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void MethodTwoCopiesTheRectangleRowMajor() {
    var decoder = VmdVideoDecoder.Create(_Stream(2, 2));
    var pixels = new byte[] { 1, 2, 3, 4 };
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 1, 1, _PlainCopy(pixels))), out var picture);
    Assert.That(picture.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void MethodTwoOnlyPaintsItsOwnRectangleLeavingTheRestOfTheCanvasAlone() {
    var decoder = VmdVideoDecoder.Create(_Stream(4, 2));
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 1, _PlainCopy(Enumerable.Repeat((byte)9, 8).ToArray()))), out _);
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 1, 0, _PlainCopy([5, 6]))), out var picture);

    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 5, 6, 9, 9, 9, 9, 9, 9 }));
  }

  [Test]
  [Category("Unit")]
  public void MethodOneLiteralRunPaintsTheGivenBytes() {
    var decoder = VmdVideoDecoder.Create(_Stream(4, 1));
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 0, _Method1([0x83, 10, 20, 30, 40]))), out var picture);
    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 10, 20, 30, 40 }));
  }

  [Test]
  [Category("Unit")]
  public void MethodOneSkipRunCopiesThePreviousPicture() {
    var decoder = VmdVideoDecoder.Create(_Stream(4, 1));
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 0, _PlainCopy([10, 20, 30, 40]))), out _);
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 0, _Method1([0x03]))), out var picture);
    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 10, 20, 30, 40 }));
  }

  [Test]
  [Category("Unit")]
  public void MethodOneFirstFrameSkipRefusesMissingReference() {
    var decoder = VmdVideoDecoder.Create(_Stream(4, 1));
    Assert.Throws<InvalidDataException>(
      () => decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 0, _Method1([0x03]))), out _));
  }

  [Test]
  [Category("Unit")]
  public void MethodOneMixesLiteralAndPreviousPictureRuns() {
    var decoder = VmdVideoDecoder.Create(_Stream(4, 1));
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 0, _PlainCopy([10, 20, 30, 40]))), out _);
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 0, _Method1([0x01, 0x81, 99, 98]))), out var picture);
    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 10, 20, 99, 98 }));
  }

  [Test]
  [Category("Unit")]
  public void MethodOneContinuesTheByteStreamAcrossRows() {
    var decoder = VmdVideoDecoder.Create(_Stream(2, 2));
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 1, 1, _Method1([0x81, 1, 2, 0x81, 3, 4]))), out var picture);
    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
  }

  [Test]
  [Category("Unit")]
  public void MethodThreePairRleRepeatsTwoByteValues() {
    var decoder = VmdVideoDecoder.Create(_Stream(4, 1));
    // literal span four, 0xFF selects pair RLE, command 2 repeats little-endian pair 7,8 twice.
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 0, _Method3([0x83, 0xFF, 0x02, 7, 8]))), out var picture);
    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 7, 8, 7, 8 }));
  }

  [Test]
  [Category("Unit")]
  public void MethodThreeCanMixPreviousPictureAndPairRle() {
    var decoder = VmdVideoDecoder.Create(_Stream(6, 1));
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 5, 0, _PlainCopy([1, 2, 3, 4, 5, 6]))), out _);
    // skip two, then four output bytes as the pair 9,10 twice.
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 5, 0, _Method3([0x01, 0x83, 0xFF, 0x02, 9, 10]))), out var picture);
    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 1, 2, 9, 10, 9, 10 }));
  }

  [Test]
  [Category("Unit")]
  public void ANewPaletteUpdatesOnlyItsDeclaredRange() {
    var decoder = VmdVideoDecoder.Create(_Stream(2, 1, paletteEntry0: (1, 2, 3)));
    var paletteRecord = new byte[770];
    paletteRecord[0] = 1; // first index
    paletteRecord[1] = 0; // count is stored minus one => one entry
    paletteRecord[2] = 63;
    paletteRecord[3] = 32;
    paletteRecord[4] = 0;
    var payload = paletteRecord.Concat(_PlainCopy([0, 1])).ToArray();

    decoder.TryDecode(new(0, _VideoPacket(0, 0, 1, 0, payload, newPalette: true)), out var picture);

    Assert.Multiple(() => {
      Assert.That(picture.Palette![0], Is.EqualTo(ChannelScaling.Expand6(1)), "entry zero is untouched");
      Assert.That(picture.Palette[3], Is.EqualTo(255));
      Assert.That(picture.Palette[4], Is.EqualTo(ChannelScaling.Expand6(32)));
      Assert.That(picture.Palette[5], Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void AFullPictureAtANonZeroLogicalOriginNormalizesItsFollowingRectangles() {
    var decoder = VmdVideoDecoder.Create(_Stream(2, 1));
    decoder.TryDecode(new(0, _VideoPacket(10, 20, 11, 20, _PlainCopy([1, 2]))), out _);
    decoder.TryDecode(new(0, _VideoPacket(11, 20, 11, 20, _PlainCopy([9]))), out var picture);
    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 1, 9 }));
  }

  [Test]
  [Category("Unit")]
  public void AnEmptyRectanglePayloadRefuses() {
    var decoder = VmdVideoDecoder.Create(_Stream(2, 2));
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _VideoPacket(0, 0, 1, 1, [])), out _));
  }

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
    var tag = (byte)0b0000_0001;
    var offsetLow = 0x11;
    var offsetHighAndLength = (byte)(0x10 | (5 - 3));
    var lzBody = _Lz(outputLength: 6, tagBytesAndData: [tag, 7, (byte)offsetLow, offsetHighAndLength]);
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 5, 0, _CompressedMethod(_METHOD_PLAIN_COPY, lzBody))), out var picture);
    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 7, 7, 7, 7, 7, 7 }));
  }

  [Test]
  [Category("Unit")]
  public void MarkerlessLzUsesThePlainQueueInitialization() {
    var decoder = VmdVideoDecoder.Create(_Stream(4, 1));
    var lzBody = _LzWithoutMarker(outputLength: 4, tagBytesAndData: [0x0F, 1, 2, 3, 4]);
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 0, _CompressedMethod(_METHOD_PLAIN_COPY, lzBody))), out var picture);
    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
  }

  [Test]
  [Category("Unit")]
  public void MarkerlessLzBackReferencesItsFeeStartingPosition() {
    var decoder = VmdVideoDecoder.Create(_Stream(4, 1));
    // bit 0 literal 7 is written at 0xFEE; bit 1 then references 0xFEE for three bytes.
    var lzBody = _LzWithoutMarker(outputLength: 4, tagBytesAndData: [0x01, 7, 0xEE, 0xF0]);
    decoder.TryDecode(new(0, _VideoPacket(0, 0, 3, 0, _CompressedMethod(_METHOD_PLAIN_COPY, lzBody))), out var picture);
    Assert.That(picture.PixelData, Is.EqualTo(new byte[] { 7, 7, 7, 7 }));
  }

  private static MediaStreamInfo _Stream(
    int width, int height, int codecVersion = 1, (byte R, byte G, byte B)? paletteEntry0 = null) {
    var header = new byte[_HEADER_LENGTH];
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), (ushort)codecVersion);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(800), checked((uint)(width * height * 4 + 1024)));
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
    record[0] = 2;
    BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(2), (uint)payload.Length);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6), (ushort)left);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(8), (ushort)top);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(10), (ushort)right);
    BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(12), (ushort)bottom);
    record[15] = newPalette ? (byte)0x02 : (byte)0;
    payload.CopyTo(record, 16);
    return record;
  }

  private static byte[] _PlainCopy(byte[] pixels) => [_METHOD_PLAIN_COPY, .. pixels];
  private static byte[] _Method1(byte[] body) => [_METHOD_ROW_RUN_LENGTH, .. body];
  private static byte[] _Method3(byte[] body) => [_METHOD_PAIR_RLE, .. body];
  private static byte[] _CompressedMethod(byte method, byte[] lzChunk) => [(byte)(_LZ_FLAG | method), .. lzChunk];

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

  private static byte[] _LzWithoutMarker(int outputLength, byte[] tagBytesAndData) {
    var chunk = new byte[4 + tagBytesAndData.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(chunk, (uint)outputLength);
    tagBytesAndData.CopyTo(chunk, 4);
    return chunk;
  }
}
