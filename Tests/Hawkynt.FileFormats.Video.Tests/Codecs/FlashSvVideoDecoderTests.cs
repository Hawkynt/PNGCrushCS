using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// Flash Screen Video, written from the SWF File Format Specification's own appendix rather than from
/// any decoder's source: the grid header's bit packing, the block-count and partial-block arithmetic,
/// the "zero length means unchanged" convention, and the refusals a malformed packet has to reach.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg over five streams built with its own flashsv
/// encoder — sizes that are and are not whole 64x64 blocks in either direction, down to a single
/// partial block covering the whole picture — 106 frames in all, byte for byte in the B, G, R order
/// this format states, with no differing sample anywhere. RGB-native, so the comparison is a direct
/// one and not a plane-by-plane approximation of anything subsampled.
/// </remarks>
[TestFixture]
public class FlashSvVideoDecoderTests {

  private static readonly CodecTag _Fsv1 = CodecTag.FromCharacters("FSV1");

  private static MediaStreamInfo _Stream(CodecTag? codec = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec ?? _Fsv1,
  };

  private static byte[] Zlib(byte[] raw) {
    using var ms = new MemoryStream();
    using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
      z.Write(raw);

    return ms.ToArray();
  }

  private static byte[] Header(int blockWidth, int imageWidth, int blockHeight, int imageHeight) {
    var blockWidthCode = blockWidth / 16 - 1;
    var blockHeightCode = blockHeight / 16 - 1;
    return [
      (byte)((blockWidthCode << 4) | (imageWidth >> 8)),
      (byte)(imageWidth & 0xFF),
      (byte)((blockHeightCode << 4) | (imageHeight >> 8)),
      (byte)(imageHeight & 0xFF),
    ];
  }

  private static byte[] Block(byte[]? compressed) {
    if (compressed == null)
      return [0x00, 0x00];

    var length = compressed.Length;
    var block = new byte[2 + length];
    block[0] = (byte)(length >> 8);
    block[1] = (byte)(length & 0xFF);
    compressed.CopyTo(block, 2);
    return block;
  }

  private static byte[] Concat(params byte[][] parts) {
    var total = 0;
    foreach (var part in parts)
      total += part.Length;

    var result = new byte[total];
    var at = 0;
    foreach (var part in parts) {
      part.CopyTo(result, at);
      at += part.Length;
    }

    return result;
  }

  /// <summary>A flat-coloured block's own bottom-to-top, B/G/R pixel data.</summary>
  private static byte[] SolidBlock(int width, int height, byte b, byte g, byte r) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = b;
      pixels[i * 3 + 1] = g;
      pixels[i * 3 + 2] = r;
    }

    return pixels;
  }

  // ============================================================================================
  // Accepts
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AcceptsTheFsv1Tag() {
    Assert.That(FlashSvVideoDecoder.Accepts(_Stream()), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    Assert.That(FlashSvVideoDecoder.Accepts(_Stream(CodecTag.FromCharacters("FSV2"))), Is.False);
  }

  // ============================================================================================
  // A key frame: one whole block
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AKeyFrameOfOneWholeBlockDecodesToAFrame() {
    var decoder = FlashSvVideoDecoder.Create(_Stream());

    var packet = new CodedPacket(0, Concat(
      Header(16, 16, 16, 8),
      Block(Zlib(SolidBlock(16, 8, 0x10, 0x20, 0x30)))));

    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Bgr24));
    Assert.That(frame.Width, Is.EqualTo(16));
    Assert.That(frame.Height, Is.EqualTo(8));
    Assert.That(frame.PixelData, Is.EqualTo(SolidBlock(16, 8, 0x10, 0x20, 0x30)));
  }

  // ============================================================================================
  // The grid, partial blocks, and bottom-to-top block order
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APartialColumnAndRowAreSizedByWhatIsLeftOfThePicture() {
    // 20x10 picture, 16x16 cells: two columns (16 then 4 wide), one row (10 tall, itself partial).
    var decoder = FlashSvVideoDecoder.Create(_Stream());

    var left = SolidBlock(16, 10, 1, 2, 3);
    var right = SolidBlock(4, 10, 9, 8, 7);
    var packet = new CodedPacket(0, Concat(
      Header(16, 20, 16, 10),
      Block(Zlib(left)),
      Block(Zlib(right))));

    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.That(frame.Width, Is.EqualTo(20));
    Assert.That(frame.Height, Is.EqualTo(10));

    // Column 0, every row: (1,2,3). Column 16 (first pixel of the right block), every row: (9,8,7).
    for (var y = 0; y < 10; ++y) {
      var atLeft = (y * 20 + 0) * 3;
      Assert.That(frame.PixelData[atLeft..(atLeft + 3)], Is.EqualTo(new byte[] { 1, 2, 3 }), $"row {y}, column 0");
      var atRight = (y * 20 + 16) * 3;
      Assert.That(frame.PixelData[atRight..(atRight + 3)], Is.EqualTo(new byte[] { 9, 8, 7 }), $"row {y}, column 16");
    }
  }

  // ============================================================================================
  // An interframe: a zero-length block is left exactly as the canvas already holds it
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AZeroLengthBlockIsLeftAsTheCanvasAlreadyHoldsIt() {
    var decoder = FlashSvVideoDecoder.Create(_Stream());

    var key = Concat(
      Header(16, 32, 16, 16),
      Block(Zlib(SolidBlock(16, 16, 1, 1, 1))),
      Block(Zlib(SolidBlock(16, 16, 2, 2, 2))));
    Assert.That(decoder.TryDecode(new(0, key), out _), Is.True);

    // Second column restated with new pixels; first column's block length is zero, so it stays (1,1,1).
    var delta = Concat(
      Header(16, 32, 16, 16),
      Block(null),
      Block(Zlib(SolidBlock(16, 16, 5, 5, 5))));
    Assert.That(decoder.TryDecode(new(0, delta), out var frame), Is.True);

    Assert.That(frame.PixelData[0..3], Is.EqualTo(new byte[] { 1, 1, 1 }), "unchanged column stays what the canvas held");
    var atSecondColumn = 16 * 3;
    Assert.That(frame.PixelData[atSecondColumn..(atSecondColumn + 3)], Is.EqualTo(new byte[] { 5, 5, 5 }), "restated column takes the new pixels");
  }

  // ============================================================================================
  // What refuses
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APacketShorterThanTheGridHeaderRefuses() {
    var decoder = FlashSvVideoDecoder.Create(_Stream());

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[] { 0, 1, 2 }), out _));
    Assert.That(failure!.Message, Does.Contain("3 byte"));
  }

  [Test]
  [Category("Unit")]
  public void APictureOfNoPixelsRefuses() {
    var decoder = FlashSvVideoDecoder.Create(_Stream());

    var packet = new CodedPacket(0, Header(16, 0, 16, 16));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("0x16"));
  }

  [Test]
  [Category("Unit")]
  public void ABlockLengthRunningPastThePacketRefuses() {
    var decoder = FlashSvVideoDecoder.Create(_Stream());

    // 16x16 picture in one 16x16 cell, whose length claims far more than the packet holds.
    var packet = new CodedPacket(0, Concat(Header(16, 16, 16, 16), new byte[] { 0x7F, 0xFF }));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("compressed byte"));
  }

  [Test]
  [Category("Unit")]
  public void AZlibStreamThatDecompressesShortRefuses() {
    var decoder = FlashSvVideoDecoder.Create(_Stream());

    // A 16x16 cell needs 768 bytes decompressed; this block's zlib stream holds one byte's worth.
    var packet = new CodedPacket(0, Concat(Header(16, 16, 16, 16), Block(Zlib(new byte[] { 1, 2, 3 }))));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("768 byte"));
  }

  [Test]
  [Category("Unit")]
  public void AGeometryChangeMidStreamRefuses() {
    var decoder = FlashSvVideoDecoder.Create(_Stream());

    var first = new CodedPacket(0, Concat(Header(16, 16, 16, 16), Block(Zlib(SolidBlock(16, 16, 0, 0, 0)))));
    Assert.That(decoder.TryDecode(first, out _), Is.True);

    var second = new CodedPacket(0, Concat(Header(16, 32, 16, 16), Block(Zlib(SolidBlock(16, 16, 0, 0, 0))), Block(Zlib(SolidBlock(16, 16, 0, 0, 0)))));
    var failure = Assert.Throws<NotSupportedException>(() => decoder.TryDecode(second, out _));
    Assert.That(failure!.Message, Does.Contain("16x16"));
    Assert.That(failure.Message, Does.Contain("32x16"));
  }
}
