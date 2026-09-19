using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class FlashSv2VideoDecoderRegressionTests {

  private static readonly CodecTag _Fsv2 = CodecTag.FromCharacters("FSV2");

  private static MediaStreamInfo _Stream() => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = _Fsv2,
  };

  private static byte[] _GridHeader(
    int blockWidth, int imageWidth, int blockHeight, int imageHeight, byte flags = 0) {
    return [
      (byte)(((blockWidth / 16 - 1) << 4) | (imageWidth >> 8)),
      (byte)imageWidth,
      (byte)(((blockHeight / 16 - 1) << 4) | (imageHeight >> 8)),
      (byte)imageHeight,
      flags,
    ];
  }

  private static byte[] _Zlib(ReadOnlySpan<byte> raw) {
    using var output = new MemoryStream();
    using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
      zlib.Write(raw);

    return output.ToArray();
  }

  private static byte[] _Block(byte format, ReadOnlySpan<byte> raw, byte? rowStart = null, byte? rowCount = null) {
    byte[] compressed = raw.IsEmpty ? [] : _Zlib(raw);
    var headerLength = rowStart.HasValue ? 3 : 1;
    var payload = new byte[headerLength + compressed.Length];
    payload[0] = format;
    if (rowStart.HasValue) {
      payload[1] = rowStart.Value;
      payload[2] = rowCount.GetValueOrDefault();
    }
    compressed.CopyTo(payload.AsSpan(headerLength));
    return _LengthPrefixed(payload);
  }

  private static byte[] _LengthPrefixed(ReadOnlySpan<byte> payload) {
    var result = new byte[payload.Length + 2];
    result[0] = (byte)(payload.Length >> 8);
    result[1] = (byte)payload.Length;
    payload.CopyTo(result.AsSpan(2));
    return result;
  }

  private static byte[] _Concat(params byte[][] parts) {
    var length = 0;
    foreach (var part in parts)
      length += part.Length;

    var result = new byte[length];
    var offset = 0;
    foreach (var part in parts) {
      part.CopyTo(result, offset);
      offset += part.Length;
    }

    return result;
  }

  private static byte[] _SolidBgr(int width, int height, byte blue, byte green, byte red) {
    var result = new byte[width * height * 3];
    for (var offset = 0; offset < result.Length; offset += 3) {
      result[offset] = blue;
      result[offset + 1] = green;
      result[offset + 2] = red;
    }
    return result;
  }

  private static byte[] _SolidIndex(int width, int height, byte index) {
    var result = new byte[width * height];
    Array.Fill(result, index);
    return result;
  }

  [Test]
  [Category("Unit")]
  public void A24BitKeyBlockDecodesBgrBytesDirectly() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());
    var raw = _SolidBgr(16, 16, 0x11, 0x22, 0x33);
    var packet = new CodedPacket(
      0,
      _Concat(_GridHeader(16, 16, 16, 16), _Block(0x00, raw)),
      IsKeyFrame: true);

    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Bgr24));
    Assert.That(frame.PixelData[0..3], Is.EqualTo(new byte[] { 0x11, 0x22, 0x33 }));
    Assert.That(frame.PixelData[^3..], Is.EqualTo(new byte[] { 0x11, 0x22, 0x33 }));
  }

  [Test]
  [Category("Unit")]
  public void A24BitDiffBlockComposesOntoTheKeyFrameReference() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());
    var header = _GridHeader(16, 16, 16, 16);
    var key = new CodedPacket(0, _Concat(header, _Block(0x00, _SolidBgr(16, 16, 1, 2, 3))), IsKeyFrame: true);
    Assert.That(decoder.TryDecode(key, out _), Is.True);

    var changedBottomRow = _SolidBgr(16, 1, 0xAA, 0xBB, 0xCC);
    var delta = new CodedPacket(0, _Concat(header, _Block(0x04, changedBottomRow, rowStart: 0, rowCount: 1)));
    Assert.That(decoder.TryDecode(delta, out var frame), Is.True);

    Assert.That(frame.PixelData[0..3], Is.EqualTo(new byte[] { 1, 2, 3 }), "untouched top row comes from the key frame");
    Assert.That(frame.PixelData[^3..], Is.EqualTo(new byte[] { 0xAA, 0xBB, 0xCC }), "bottom row is replaced by the diff");
  }

  [Test]
  [Category("Unit")]
  public void ChangingTheHybridPaletteDoesNotRecolorTheStoredKeyFrame() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());
    var keyHeader = _GridHeader(16, 16, 16, 16);
    var key = new CodedPacket(
      0,
      _Concat(keyHeader, _Block(0x10, _SolidIndex(16, 16, 1))),
      IsKeyFrame: true);
    Assert.That(decoder.TryDecode(key, out var keyFrame), Is.True);
    Assert.That(keyFrame.PixelData[0..3], Is.EqualTo(new byte[] { 0x33, 0x33, 0x33 }));

    var palette = new byte[128 * 3];
    palette[1 * 3] = 0x03;
    palette[1 * 3 + 1] = 0x02;
    palette[1 * 3 + 2] = 0x01;
    palette[2 * 3] = 0xCC;
    palette[2 * 3 + 1] = 0xBB;
    palette[2 * 3 + 2] = 0xAA;

    var paletteBlock = _LengthPrefixed(_Zlib(palette));
    var deltaHeader = _GridHeader(16, 16, 16, 16, flags: 0x01);
    var delta = new CodedPacket(
      0,
      _Concat(deltaHeader, paletteBlock, _Block(0x14, _SolidIndex(16, 1, 2), rowStart: 0, rowCount: 1)));

    Assert.That(decoder.TryDecode(delta, out var frame), Is.True);
    Assert.That(frame.PixelData[0..3], Is.EqualTo(new byte[] { 0x33, 0x33, 0x33 }),
      "the untouched top row remains the color decoded at the key frame, not the new palette's index 1");
    Assert.That(frame.PixelData[^3..], Is.EqualTo(new byte[] { 0xCC, 0xBB, 0xAA }));
  }

  [Test]
  [Category("Unit")]
  public void AKeyFrameMayEstablishANewBlockGrid() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());

    var first = new CodedPacket(
      0,
      _Concat(_GridHeader(16, 16, 16, 16), _Block(0x00, _SolidBgr(16, 16, 1, 2, 3))),
      IsKeyFrame: true);
    Assert.That(decoder.TryDecode(first, out _), Is.True);

    var second = new CodedPacket(
      0,
      _Concat(_GridHeader(32, 32, 16, 16), _Block(0x00, _SolidBgr(32, 16, 4, 5, 6))),
      IsKeyFrame: true);
    Assert.That(decoder.TryDecode(second, out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That(frame.Width, Is.EqualTo(32));
      Assert.That(frame.Height, Is.EqualTo(16));
      Assert.That(frame.PixelData[0..3], Is.EqualTo(new byte[] { 4, 5, 6 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void AZeroLengthKeyBlockIsRejected() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());
    var packet = new CodedPacket(
      0,
      _Concat(_GridHeader(16, 16, 16, 16), [0x00, 0x00]),
      IsKeyFrame: true);

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("key frame").And.Contain("DataSize").And.Contain("0"));
  }

  [TestCase(128 * 3 - 1)]
  [TestCase(128 * 3 + 1)]
  [Category("Unit")]
  public void ACustomPaletteMustInflateToExactly128Entries(int decompressedBytes) {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());
    var palette = new byte[decompressedBytes];
    var packet = new CodedPacket(
      0,
      _Concat(_GridHeader(16, 16, 16, 16, flags: 0x01), _LengthPrefixed(_Zlib(palette))),
      IsKeyFrame: true);

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("384").And.Contain(decompressedBytes.ToString()));
  }

  [Test]
  [Category("Unit")]
  public void AnEmptyCustomPaletteBlockIsRejected() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());
    var packet = new CodedPacket(
      0,
      _Concat(_GridHeader(16, 16, 16, 16, flags: 0x01), [0x00, 0x00]),
      IsKeyFrame: true);

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("palette").And.Contain("DataSize").And.Contain("0"));
  }

  [Test]
  [Category("Unit")]
  public void ReservedGridHeaderBitsAreRejected() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());
    var packet = new CodedPacket(0, _GridHeader(16, 16, 16, 16, flags: 0x80), IsKeyFrame: true);

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("reserved"));
  }

  [Test]
  [Category("Unit")]
  public void ReservedBlockFormatBitsAreRejected() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());
    var packet = new CodedPacket(
      0,
      _Concat(_GridHeader(16, 16, 16, 16), _Block(0x80, _SolidBgr(16, 16, 1, 2, 3))),
      IsKeyFrame: true);

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("reserved"));
  }
}
