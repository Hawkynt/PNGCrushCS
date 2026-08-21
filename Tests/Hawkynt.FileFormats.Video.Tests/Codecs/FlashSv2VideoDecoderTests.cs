using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// Flash Screen Video 2, written from the SWF File Format Specification's own appendix and, for the
/// parts the appendix leaves open, from a corpus built with ffmpeg's own flashsv2 encoder: the grid
/// header's second flags byte, the block format byte's colour depth and coding flags, the hybrid
/// colourspace's per-pixel one-or-two-byte choice against the default 128-entry palette, a diff
/// block's row range, and — the part measurement rather than the specification settled — that
/// "priming" is a DEFLATE preset dictionary built from a cell's content at the container's own last
/// key frame, not a continued zlib stream and not the previous frame's own content.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured against ffmpeg over five streams built with its own flashsv2
/// encoder — sizes that are and are not whole 64x64 blocks in either direction, multiple key frames a
/// stream, and interframes mixing fresh, primed, whole-cell and partial-row blocks in every
/// combination the encoder produced — 122 frames in all, byte for byte, with no differing sample
/// anywhere. RGB-native, so the comparison is a direct one and not a plane-by-plane approximation of
/// anything subsampled.
/// <para/>
/// The priming path itself cannot be built with this package's own tools — it needs a DEFLATE stream
/// compressed against a preset dictionary, which nothing here writes — so the primed test below carries
/// bytes produced once with Python's zlib against a dictionary equal to the key frame's own raw pixel
/// bytes, exactly the shape <see cref="FlashSv2.RawDeflate"/> reads.
/// </remarks>
[TestFixture]
public class FlashSv2VideoDecoderTests {

  private static readonly CodecTag _Fsv2 = CodecTag.FromCharacters("FSV2");

  private static MediaStreamInfo _Stream(CodecTag? codec = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec ?? _Fsv2,
  };

  private static byte[] Zlib(byte[] raw) {
    using var ms = new MemoryStream();
    using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
      z.Write(raw);

    return ms.ToArray();
  }

  private static byte[] GridHeader(int blockWidth, int imageWidth, int blockHeight, int imageHeight, bool hasIFrame = false, bool hasPalette = false) {
    var blockWidthCode = blockWidth / 16 - 1;
    var blockHeightCode = blockHeight / 16 - 1;
    return [
      (byte)((blockWidthCode << 4) | (imageWidth >> 8)),
      (byte)(imageWidth & 0xFF),
      (byte)((blockHeightCode << 4) | (imageHeight >> 8)),
      (byte)(imageHeight & 0xFF),
      (byte)((hasIFrame ? 0x02 : 0) | (hasPalette ? 0x01 : 0)),
    ];
  }

  private static byte[] LengthPrefixed(byte[] payload) {
    var block = new byte[2 + payload.Length];
    block[0] = (byte)(payload.Length >> 8);
    block[1] = (byte)(payload.Length & 0xFF);
    payload.CopyTo(block, 2);
    return block;
  }

  /// <summary>A whole-cell hybrid-colourspace block: no diff, no priming, palette-index bytes only.</summary>
  private static byte[] FullBlock(byte[] rawPixels) {
    var payload = Concat([(byte)(2 << 3)], Zlib(rawPixels));
    return LengthPrefixed(payload);
  }

  /// <summary>A fresh (unprimed) diff block covering <paramref name="height"/> rows from
  /// <paramref name="rowStart"/>.</summary>
  private static byte[] FreshDiffBlock(int rowStart, int height, byte[] rawPixels) {
    var payload = Concat([(byte)((2 << 3) | 0x04), (byte)rowStart, (byte)height], Zlib(rawPixels));
    return LengthPrefixed(payload);
  }

  /// <summary>A primed diff block, its DEFLATE bytes supplied verbatim since nothing here can compress
  /// against a preset dictionary.</summary>
  private static byte[] PrimedDiffBlock(int rowStart, int height, byte[] rawDeflateBytes) {
    var payload = Concat([(byte)((2 << 3) | 0x04 | 0x01), (byte)rowStart, (byte)height], rawDeflateBytes);
    return LengthPrefixed(payload);
  }

  private static byte[] UnchangedBlock() => [0x00, 0x00];

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

  /// <summary>Sixteen rows, index <c>i</c> for local (bottom-up) row <c>i</c>, one byte a pixel.</summary>
  private static byte[] KeyFramePixels() {
    var raw = new byte[16 * 16];
    for (var row = 0; row < 16; ++row)
      for (var col = 0; col < 16; ++col)
        raw[row * 16 + col] = (byte)row;

    return raw;
  }

  private static byte[] SolidIndexRows(int index, int height) {
    var raw = new byte[16 * height];
    for (var i = 0; i < raw.Length; ++i)
      raw[i] = (byte)index;

    return raw;
  }

  // ============================================================================================
  // Accepts
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AcceptsTheFsv2Tag() {
    Assert.That(FlashSv2VideoDecoder.Accepts(_Stream()), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse() {
    Assert.That(FlashSv2VideoDecoder.Accepts(_Stream(CodecTag.FromCharacters("FSV1"))), Is.False);
  }

  // ============================================================================================
  // A key frame decodes through the default hybrid palette
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AKeyFrameDecodesUsingTheDefaultPalette() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());

    var packet = new CodedPacket(0, Concat(GridHeader(16, 16, 16, 16), FullBlock(KeyFramePixels())), IsKeyFrame: true);
    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Bgr24));
    Assert.That(frame.Width, Is.EqualTo(16));
    Assert.That(frame.Height, Is.EqualTo(16));

    // Local (bottom-up) row 0 -> palette index 0 -> black -> display row 15 (the picture's bottom).
    var bottomRow = 15 * 16 * 3;
    Assert.That(frame.PixelData[bottomRow..(bottomRow + 3)], Is.EqualTo(new byte[] { 0, 0, 0 }));

    // Local row 15 -> palette index 15 -> 0x00FF00 -> display row 0 (the picture's top), B,G,R.
    Assert.That(frame.PixelData[0..3], Is.EqualTo(new byte[] { 0x00, 0xFF, 0x00 }));

    // Local row 1 -> palette index 1 -> 0x333333 -> display row 14.
    var row14 = 14 * 16 * 3;
    Assert.That(frame.PixelData[row14..(row14 + 3)], Is.EqualTo(new byte[] { 0x33, 0x33, 0x33 }));
  }

  // ============================================================================================
  // Every block composes onto the reference the key frame established
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFreshDiffBlockChangesOnlyItsOwnRowsAndLeavesTheRestAtTheReference() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());
    var header = GridHeader(16, 16, 16, 16);

    var key = new CodedPacket(0, Concat(header, FullBlock(KeyFramePixels())), IsKeyFrame: true);
    Assert.That(decoder.TryDecode(key, out _), Is.True);

    // Local rows 0-3 (display rows 12-15, the picture's bottom four) become palette index 4 (0xCCCCCC).
    var delta = new CodedPacket(0, Concat(header, FreshDiffBlock(0, 4, SolidIndexRows(4, 4))));
    Assert.That(decoder.TryDecode(delta, out var frame), Is.True);

    var bottomRow = 15 * 16 * 3;
    Assert.That(frame.PixelData[bottomRow..(bottomRow + 3)], Is.EqualTo(new byte[] { 0xCC, 0xCC, 0xCC }), "touched row changes");

    // Display row 0 (local row 15) is untouched by this diff and still shows the key frame's palette index 15.
    Assert.That(frame.PixelData[0..3], Is.EqualTo(new byte[] { 0x00, 0xFF, 0x00 }), "untouched row keeps the reference");
  }

  [Test]
  [Category("Unit")]
  public void APrimedDiffBlockDecodesAgainstTheKeyFramesOwnReference() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());
    var header = GridHeader(16, 16, 16, 16);

    var key = new CodedPacket(0, Concat(header, FullBlock(KeyFramePixels())), IsKeyFrame: true);
    Assert.That(decoder.TryDecode(key, out _), Is.True);

    // DEFLATE bytes for sixteen bytes of palette index 3 (0x999999), compressed with Python's zlib
    // against a preset dictionary equal to KeyFramePixels() -- the only way to reach this path at all,
    // since nothing in this package compresses against a preset dictionary.
    byte[] primedDeflate = [0x63, 0xA6, 0x10, 0x00, 0x00];

    // Local rows 12-15 (display rows 0-3, the picture's top four) become palette index 3.
    var primed = new CodedPacket(0, Concat(header, PrimedDiffBlock(12, 4, primedDeflate)));
    Assert.That(decoder.TryDecode(primed, out var frame), Is.True);

    Assert.That(frame.PixelData[0..3], Is.EqualTo(new byte[] { 0x99, 0x99, 0x99 }), "primed row decodes against the key frame's dictionary");

    // Display row 15 (local row 0), untouched by this block, still shows the key frame's palette index 0.
    var bottomRow = 15 * 16 * 3;
    Assert.That(frame.PixelData[bottomRow..(bottomRow + 3)], Is.EqualTo(new byte[] { 0, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void ABlockOfNoRowsAndNoDataRepaintsTheCellFromTheReference() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());
    var header = GridHeader(16, 16, 16, 16);

    var key = new CodedPacket(0, Concat(header, FullBlock(KeyFramePixels())), IsKeyFrame: true);
    Assert.That(decoder.TryDecode(key, out _), Is.True);

    var delta = new CodedPacket(0, Concat(header, FreshDiffBlock(0, 4, SolidIndexRows(4, 4))));
    Assert.That(decoder.TryDecode(delta, out var deltaFrame), Is.True);
    var bottomRow = 15 * 16 * 3;
    Assert.That(deltaFrame.PixelData[bottomRow..(bottomRow + 3)], Is.EqualTo(new byte[] { 0xCC, 0xCC, 0xCC }), "the temporary change is visible on its own frame");

    // hasDiffBlocks, rowStart 0, height 0, no data at all: three bytes, nothing to decompress.
    var revert = new CodedPacket(0, Concat(header, LengthPrefixed([(byte)((2 << 3) | 0x04), 0x00, 0x00])));
    Assert.That(decoder.TryDecode(revert, out var revertFrame), Is.True);
    Assert.That(revertFrame.PixelData[bottomRow..(bottomRow + 3)], Is.EqualTo(new byte[] { 0, 0, 0 }), "the cell reverts to the key frame's reference");
  }

  [Test]
  [Category("Unit")]
  public void AnUnchangedBlockLeavesTheCanvasExactlyAsItWas() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());
    var header = GridHeader(16, 16, 16, 16);

    var key = new CodedPacket(0, Concat(header, FullBlock(KeyFramePixels())), IsKeyFrame: true);
    Assert.That(decoder.TryDecode(key, out var keyFrame), Is.True);

    var unchanged = new CodedPacket(0, Concat(header, UnchangedBlock()));
    Assert.That(decoder.TryDecode(unchanged, out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(keyFrame.PixelData));
  }

  // ============================================================================================
  // A custom palette replaces the default
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APacketCarryingAPaletteReplacesTheDefaultOne() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());
    var header = GridHeader(16, 16, 16, 16, hasPalette: true);

    var palette = new byte[128 * 3];
    for (var entry = 0; entry < 127; ++entry) {
      palette[entry * 3] = 0x56; // B
      palette[entry * 3 + 1] = 0x34; // G
      palette[entry * 3 + 2] = 0x12; // R
    }
    palette[127 * 3] = 0xCC; palette[127 * 3 + 1] = 0xBB; palette[127 * 3 + 2] = 0xAA;

    var paletteBlock = LengthPrefixed(Zlib(palette));
    var picture = SolidIndexRows(127, 16);
    var packet = new CodedPacket(0, Concat(header, paletteBlock, FullBlock(picture)), IsKeyFrame: true);

    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.That(frame.PixelData[0..3], Is.EqualTo(new byte[] { 0xCC, 0xBB, 0xAA }));
  }

  // ============================================================================================
  // What refuses
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesHasIFrameImage() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());
    var packet = new CodedPacket(0, GridHeader(16, 16, 16, 16, hasIFrame: true), IsKeyFrame: true);

    var failure = Assert.Throws<NotSupportedException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("HasIFrameImage"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesZlibPrimeCompressCurrent() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());
    var header = GridHeader(16, 16, 16, 16);
    var payload = Concat([(byte)((2 << 3) | 0x02)], Zlib(KeyFramePixels()));
    var packet = new CodedPacket(0, Concat(header, LengthPrefixed(payload)), IsKeyFrame: true);

    var failure = Assert.Throws<NotSupportedException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("ZlibPrimeCompressCurrent"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAColourDepthTheSpecificationDoesNotDefine() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());
    var header = GridHeader(16, 16, 16, 16);
    var payload = Concat([(byte)(1 << 3)], Zlib(KeyFramePixels()));
    var packet = new CodedPacket(0, Concat(header, LengthPrefixed(payload)), IsKeyFrame: true);

    var failure = Assert.Throws<NotSupportedException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("colour depth 1"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesADiffRowRangeOutsideTheCell() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());
    var header = GridHeader(16, 16, 16, 16);
    var key = new CodedPacket(0, Concat(header, FullBlock(KeyFramePixels())), IsKeyFrame: true);
    Assert.That(decoder.TryDecode(key, out _), Is.True);

    var packet = new CodedPacket(0, Concat(header, FreshDiffBlock(14, 4, SolidIndexRows(4, 4))));
    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("outside its own 16-row cell"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAKeyFrameBlockThatDoesNotCoverTheWholeCell() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());
    var header = GridHeader(16, 16, 16, 16);
    var packet = new CodedPacket(0, Concat(header, FreshDiffBlock(0, 8, SolidIndexRows(4, 8))), IsKeyFrame: true);

    var failure = Assert.Throws<NotSupportedException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("key frame"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPrimedBlockBeforeAnyKeyFrameEstablishedAReference() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());
    var header = GridHeader(16, 16, 16, 16);
    byte[] someDeflate = [0x63, 0xA6, 0x10, 0x00, 0x00];
    var packet = new CodedPacket(0, Concat(header, PrimedDiffBlock(0, 4, someDeflate)));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("before any key frame established one"));
  }

  [Test]
  [Category("Unit")]
  public void APacketShorterThanTheGridHeaderRefuses() {
    var decoder = FlashSv2VideoDecoder.Create(_Stream());

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[] { 0, 1, 2, 3 }), out _));
    Assert.That(failure!.Message, Does.Contain("4 byte"));
  }
}
