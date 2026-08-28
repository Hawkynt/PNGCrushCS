using System;
using System.IO;
using FileFormat.Codecs.Hap;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The Hap decoder, on frames built here byte by byte.
/// </summary>
/// <remarks>
/// The decoder as a whole was measured over six streams and six hundred frames against ffmpeg's own
/// Hap decode, plane by plane, at max delta 0 — see <see cref="HapDecoder"/>'s own remarks for that
/// comparison. What these tests add is what six streams built by one encoder cannot reach: the
/// four-byte section header form, which ffmpeg never writes; the "consult decode instructions" form
/// with an explicit chunk offset table, which ffmpeg's encoder never emits either; the Hap Q Alpha
/// multiple-image combination, which ffmpeg cannot encode at all; and every refusal, none of which a
/// well-formed oracle stream can be made to reach.
/// </remarks>
[TestFixture]
public class HapDecoderTests {

  // ============================================================================================
  // Building blocks
  // ============================================================================================

  private static ushort Rgb565(int r5, int g6, int b5) => (ushort)((r5 << 11) | (g6 << 5) | b5);

  private static byte[] Header4(int size, byte type) => [
    (byte)size, (byte)(size >> 8), (byte)(size >> 16), type,
  ];

  private static byte[] Header8(int size, byte type) => [
    0, 0, 0, type, (byte)size, (byte)(size >> 8), (byte)(size >> 16), (byte)(size >> 24),
  ];

  /// <summary>An eight-byte DXT1/DXT5-colour block whose sixteen pixels are all index 0 — the block's
  /// first colour, unmixed, and nothing else about the block's contents matters.</summary>
  private static byte[] SolidColourBlock(ushort c0, ushort c1) => [
    (byte)c0, (byte)(c0 >> 8), (byte)c1, (byte)(c1 >> 8), 0, 0, 0, 0,
  ];

  /// <summary>A DXT1/DXT5-colour block whose sixteen pixels cycle through all four palette entries,
  /// column by column, so every one of a block's four colours lands at a known pixel.</summary>
  private static byte[] FourColourBlock(ushort c0, ushort c1) => [
    (byte)c0, (byte)(c0 >> 8), (byte)c1, (byte)(c1 >> 8), 0xE4, 0xE4, 0xE4, 0xE4,
  ];

  private static byte[] Cat(params byte[][] parts) {
    var length = 0;
    foreach (var part in parts)
      length += part.Length;

    var result = new byte[length];
    var at = 0;
    foreach (var part in parts) {
      part.CopyTo(result, at);
      at += part.Length;
    }

    return result;
  }

  private static MediaStreamInfo Stream(string code, int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(code),
    Width = width,
    Height = height,
  };

  private static RawImage Decode(string code, int width, int height, byte[] frame) {
    var decoder = HapDecoder.Create(Stream(code, width, height));
    Assert.That(decoder.TryDecode(new(0, frame), out var picture), Is.True);
    return picture;
  }

  // ============================================================================================
  // The section header, both its forms
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFourByteSectionHeaderIsRead() {
    // ffmpeg's own Hap encoder never writes this form — every top-level section it produced across
    // the whole corpus used the eight-byte one regardless of how small the section was — so this is
    // reached only here.
    var block = SolidColourBlock(Rgb565(31, 63, 31), 0);
    var frame = Cat(Header4(block.Length, 0xAB), block);

    var picture = Decode("Hap1", 4, 4, frame);

    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(picture.PixelData, Is.EqualTo(_Flat(4 * 4, 255, 255, 255)));
  }

  [Test]
  [Category("Unit")]
  public void AnEightByteSectionHeaderIsRead() {
    var block = SolidColourBlock(Rgb565(31, 0, 0), 0);
    var frame = Cat(Header8(block.Length, 0xAB), block);

    var picture = Decode("Hap1", 4, 4, frame);

    Assert.That(picture.PixelData, Is.EqualTo(_Flat(4 * 4, 255, 0, 0)));
  }

  // ============================================================================================
  // The colour-endpoint expansion table, pinned against a value the oracle measured
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheFiveBitEndpointExpansionIsTheMeasuredTableAndNotARoundedOrTruncatedDivision() {
    // Two values, each ruling out one of the two closed forms — neither alone is enough, because
    // each formula matches the measured table most of the time and only the two together pin down
    // that it is neither. r5 = 29: rounding v*255/31 gives 239, truncating gives 238, and ffmpeg's
    // own decode gives 238 — indistinguishable from truncation on this value alone. r5 = 3: rounding
    // gives 25, truncating gives 24, and ffmpeg's decode gives 25 — indistinguishable from rounding
    // on this value alone, and the opposite of what r5 = 29 says about truncation. A decoder that
    // truncated everywhere would pass the first assertion and fail the second; one that rounded
    // everywhere would pass the second and fail the first.
    var block29 = SolidColourBlock(Rgb565(29, 0, 0), 0);
    var frame29 = Cat(Header4(block29.Length, 0xAB), block29);
    Assert.That(Decode("Hap1", 4, 4, frame29).PixelData[0], Is.EqualTo(238));

    var block3 = SolidColourBlock(Rgb565(3, 0, 0), 0);
    var frame3 = Cat(Header4(block3.Length, 0xAB), block3);
    Assert.That(Decode("Hap1", 4, 4, frame3).PixelData[0], Is.EqualTo(25));
  }

  [Test]
  [Category("Unit")]
  public void TheFourColourBlockInterpolatesByPlainDivisionWithNoRoundingTerm() {
    // c0 = (255,255,255), c1 = (8,4,8) — chosen so the third colour's red channel does not divide
    // evenly by 3: (2*255+8)/3 is 172 by plain division and 173 by the shared decoders' rounded one,
    // (518+1)/3 landing on the far side of the same remainder that made 172 the floor. A test built on
    // white and black endpoints would not tell the two formulas apart at all, since both of their
    // interpolated colours divide evenly and no rounding term has anything to round.
    var c0 = Rgb565(31, 63, 31);
    var c1 = Rgb565(1, 1, 1);
    var block = FourColourBlock(c0, c1);
    var frame = Cat(Header4(block.Length, 0xAB), block);

    var picture = Decode("Hap1", 4, 1, frame);

    // columns 0..3 of row 0 are index 0 (255,255,255), 1 (8,4,8),
    // 2 ((2*255+8)/3, (2*255+4)/3, (2*255+8)/3) = (172,171,172),
    // 3 ((255+2*8)/3, (255+2*4)/3, (255+2*8)/3) = (90,87,90)
    Assert.That(picture.PixelData, Is.EqualTo(new byte[] {
      255, 255, 255, 8, 4, 8, 172, 171, 172, 90, 87, 90,
    }));
  }

  [Test]
  [Category("Unit")]
  public void TheTwoColourBranchsThirdEntryIsBlackWithNoAlphaChannelToCarryItsTransparency() {
    // c0 <= c1 switches DXT1 into the three-colour-plus-black mode S3TC defines; Hap's "RGB" pixel
    // format has no alpha channel for that transparency to occupy, so the fourth palette entry comes
    // out as plain black, indistinguishable from an opaque black pixel.
    var block = FourColourBlock(0, Rgb565(31, 63, 31));
    var frame = Cat(Header4(block.Length, 0xAB), block);

    var picture = Decode("Hap1", 4, 1, frame);

    Assert.That(picture.PixelData[9], Is.EqualTo(0), "R of the fourth (transparent-in-DXT1) entry");
    Assert.That(picture.PixelData[10], Is.EqualTo(0), "G of the fourth entry");
    Assert.That(picture.PixelData[11], Is.EqualTo(0), "B of the fourth entry");
  }

  // ============================================================================================
  // RGBA DXT5
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ADxt5FrameCarriesRealAlpha() {
    var c0 = Rgb565(31, 0, 0);
    var block = new byte[] {
      200, 200, // alpha0, alpha1 (equal, so every index reproduces 200)
      0, 0, 0, 0, 0, 0, // alpha indices, all zero
      (byte)c0, (byte)(c0 >> 8), 0, 0, // colour block: solid red
      0, 0, 0, 0,
    };
    var frame = Cat(Header4(block.Length, 0xAE), block);

    var picture = Decode("Hap5", 4, 4, frame);

    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(picture.PixelData, Is.EqualTo(_Flat(4 * 4, 255, 0, 0, 200)));
  }

  // ============================================================================================
  // Second-stage compressors
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ASnappyLiteralBlockDecompressesToTheSameFrame() {
    var block = SolidColourBlock(Rgb565(0, 63, 0), 0);
    var snappy = _SnappyLiteral(block);
    var frame = Cat(Header4(snappy.Length, 0xBB), snappy);

    var picture = Decode("Hap1", 4, 4, frame);

    Assert.That(picture.PixelData, Is.EqualTo(_Flat(4 * 4, 0, 255, 0)));
  }

  [Test]
  [Category("Unit")]
  public void SnappyTwoByteOffsetCopiesReadBackAcrossTheOutputBufferAlreadyWritten() {
    // "A" once, then a copy of length 7 at offset 1 — the overlapping back-reference every LZ77
    // format uses for a run, decoded byte by byte rather than as a memcpy.
    var snappy = new byte[] { 8, 0x00, 0x41, 0x1A, 0x01, 0x00 };

    var decoded = HapSnappyDecoder.Decompress(snappy);

    Assert.That(decoded, Is.EqualTo(new byte[] { 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41 }));
  }

  [Test]
  [Category("Unit")]
  public void SnappyOneByteOffsetCopiesReadTheOffsetsThreeHighBitsFromTheTagByte() {
    // "AB" then a copy of length 4 at offset 2 -> "ABABAB" (6 bytes). Offset 2 fits the one-byte-offset
    // form's [0..2047] range, whose eleven bits split three-in-the-tag, eight-in-the-next-byte.
    var literalTag = (byte)((2 - 1) << 2);
    var copyTag = (byte)((((4 - 4) & 0x7) << 2) | (((2 >> 8) & 0x7) << 5) | 1);
    var snappy = new byte[] { 6, literalTag, 0x41, 0x42, copyTag, 2 & 0xFF };

    var decoded = HapSnappyDecoder.Decompress(snappy);

    Assert.That(decoded, Is.EqualTo(new byte[] { 0x41, 0x42, 0x41, 0x42, 0x41, 0x42 }));
  }

  [Test]
  [Category("Unit")]
  public void ASnappyCopyBeforeTheStartOfTheOutputIsRefused() {
    var snappy = new byte[] { 4, 0x1A, 0x05, 0x00 }; // copy length 7 at offset 5 with nothing written yet

    var failure = Assert.Throws<InvalidDataException>(() => HapSnappyDecoder.Decompress(snappy));
    Assert.That(failure!.Message, Does.Contain("not a place already written"));
  }

  [Test]
  [Category("Unit")]
  public void ASnappyBlockProducingTheWrongLengthIsRefused() {
    var snappy = new byte[] { 4, 0x00, 0x41 }; // preamble says 4 bytes, one literal byte given

    var failure = Assert.Throws<InvalidDataException>(() => HapSnappyDecoder.Decompress(snappy));
    Assert.That(failure!.Message, Does.Contain("preamble states"));
  }

  // ============================================================================================
  // The "consult decode instructions" chunked form
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ChunksAreFoundByCumulativeSizeWhenNoOffsetTableIsPresent() {
    var left = SolidColourBlock(Rgb565(31, 0, 0), 0);
    var right = SolidColourBlock(Rgb565(0, 0, 31), 0);
    var frame = _ChunkedFrame(0xAB, [left, right], [0x0A, 0x0A], withOffsetTable: false);

    var picture = Decode("Hap1", 8, 4, frame);

    Assert.That(picture.PixelData, Is.EqualTo(_TwoBlocksSideBySide(255, 0, 0, 0, 0, 255)));
  }

  [Test]
  [Category("Unit")]
  public void AnExplicitChunkOffsetTableIsHonoured() {
    // ffmpeg's own encoder never writes one — every chunked frame it produced left the offsets to be
    // computed from the sizes — so this is the only place the table's own bytes are read.
    var left = SolidColourBlock(Rgb565(31, 0, 0), 0);
    var right = SolidColourBlock(Rgb565(0, 0, 31), 0);
    var frame = _ChunkedFrame(0xAB, [left, right], [0x0A, 0x0A], withOffsetTable: true);

    var picture = Decode("Hap1", 8, 4, frame);

    Assert.That(picture.PixelData, Is.EqualTo(_TwoBlocksSideBySide(255, 0, 0, 0, 0, 255)));
  }

  [Test]
  [Category("Unit")]
  public void AChunkMayBeSnappyCompressedWhileItsNeighbourIsNot() {
    var left = SolidColourBlock(Rgb565(31, 0, 0), 0);
    var right = _SnappyLiteral(SolidColourBlock(Rgb565(0, 0, 31), 0));
    var frame = _ChunkedFrame(0xAB, [left, right], [0x0A, 0x0B], withOffsetTable: false);

    var picture = Decode("Hap1", 8, 4, frame);

    Assert.That(picture.PixelData, Is.EqualTo(_TwoBlocksSideBySide(255, 0, 0, 0, 0, 255)));
  }

  [Test]
  [Category("Unit")]
  public void DecodeInstructionsMissingASizeTableAreRefused() {
    var compressorTable = new byte[] { 0x0A };
    var containerBody = Cat(Header4(compressorTable.Length, 0x02), compressorTable);
    var container = Cat(Header4(containerBody.Length, 0x01), containerBody);
    var frame = Cat(Header4(container.Length, 0xCB), container);

    var failure = Assert.Throws<InvalidDataException>(() => Decode("Hap1", 4, 4, frame));
    Assert.That(failure!.Message, Does.Contain("chunk compressor table and chunk size table"));
  }

  [Test]
  [Category("Unit")]
  public void AChunkNamingAnUnknownCompressorIsRefused() {
    var block = SolidColourBlock(0, 0);
    var frame = _ChunkedFrame(0xAB, [block], [0x99], withOffsetTable: false);

    var failure = Assert.Throws<NotSupportedException>(() => Decode("Hap1", 4, 4, frame));
    Assert.That(failure!.Message, Does.Contain("neither uncompressed nor Snappy"));
  }

  // ============================================================================================
  // Hap Q — Scaled YCoCg
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ScaledYCoCgReconstructionMatchesTheFormulaDerivedFromThePaper() {
    // c0 = 0 (raw red, green and blue all expand to 0): Co = 0-128 = -128, Cg = 0-128 = -128, scale =
    // 0/8+1 = 1. R = Y+Co-Cg = 100+(-128)-(-128) = 100. G = Y+Cg = 100-128 = -28, clamped to 0. B =
    // Y-Co-Cg = 100-(-128)-(-128) = 356, clamped to 255. The clamp is exercised on purpose: a block
    // this far from grey is exactly what a real encoder's scale factor exists to avoid, and a decoder
    // still has to do something coherent with one that never should have been written.
    var block = new byte[] {
      100, 100, 0, 0, 0, 0, 0, 0, // alpha (Y) = 100 on every pixel
      0, 0, 0, 0, // colour block: c0 = c1 = 0
      0, 0, 0, 0,
    };
    var frame = Cat(Header4(block.Length, 0xAF), block);

    var picture = Decode("HapY", 4, 4, frame);

    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(picture.PixelData, Is.EqualTo(_Flat(4 * 4, 100, 0, 255)));
  }

  [Test]
  [Category("Unit")]
  public void ScaledYCoCgConversionIsExactAtZeroChroma() {
    // Called directly with already-expanded bytes, sidestepping the five- and six-bit endpoint table:
    // red and green at exactly 128 is Co = Cg = 0, which the block encoding above cannot reach because
    // neither table contains 128.
    var (r, g, b) = HapYCoCgConversion.ToRgb(128, 128, 0, 100);

    Assert.That((r, g, b), Is.EqualTo(((byte)100, (byte)100, (byte)100)));
  }

  [Test]
  [Category("Unit")]
  public void ScaledYCoCgConversionScalesChromaByTheBlueChannelsField() {
    // Co = 132-128 = 4, Cg = 0, scale = 8/8+1 = 2, so Co is halved before it reaches R and B.
    var (r, g, b) = HapYCoCgConversion.ToRgb(132, 128, 8, 100);

    Assert.That((r, g, b), Is.EqualTo(((byte)102, (byte)100, (byte)98)));
  }

  // ============================================================================================
  // Hap Q Alpha — the multiple-image combination, unreachable through ffmpeg's encoder
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void HapQAlphaCombinesAScaledYCoCgImageWithASeparateRgtc1AlphaImage() {
    var colourBlock = new byte[] { 100, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }; // Y=100, c0=c1=0
    var alphaBlock = new byte[] { 200, 200, 0, 0, 0, 0, 0, 0 };

    var colourSection = Cat(Header4(colourBlock.Length, 0xAF), colourBlock);
    var alphaSection = Cat(Header4(alphaBlock.Length, 0xA1), alphaBlock);
    var multi = Cat(colourSection, alphaSection);
    var frame = Cat(Header4(multi.Length, 0x0D), multi);

    var picture = Decode("HapM", 4, 4, frame);

    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgba32));
    // colour comes from the c0=0 case above: R=100, G=0, B=255
    Assert.That(picture.PixelData, Is.EqualTo(_Flat(4 * 4, 100, 0, 255, 200)));
  }

  [Test]
  [Category("Unit")]
  public void TheOrderOfTheTwoImagesInAHapQAlphaFrameDoesNotMatter() {
    var colourBlock = new byte[] { 100, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    var alphaBlock = new byte[] { 200, 200, 0, 0, 0, 0, 0, 0 };

    var alphaSection = Cat(Header4(alphaBlock.Length, 0xA1), alphaBlock);
    var colourSection = Cat(Header4(colourBlock.Length, 0xAF), colourBlock);
    var multi = Cat(alphaSection, colourSection); // alpha first this time
    var frame = Cat(Header4(multi.Length, 0x0D), multi);

    var picture = Decode("HapM", 4, 4, frame);

    Assert.That(picture.PixelData, Is.EqualTo(_Flat(4 * 4, 100, 0, 255, 200)));
  }

  [Test]
  [Category("Unit")]
  public void AMultipleImageSectionCombiningTwoColourImagesIsRefused() {
    var a = Cat(Header4(8, 0xAB), SolidColourBlock(0, 0));
    var b = Cat(Header4(8, 0xAB), SolidColourBlock(0, 0));
    var multi = Cat(a, b);
    var frame = Cat(Header4(multi.Length, 0x0D), multi);

    var failure = Assert.Throws<NotSupportedException>(() => Decode("HapM", 4, 4, frame));
    Assert.That(failure!.Message, Does.Contain("only combination"));
  }

  // ============================================================================================
  // Hap R — BC7
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void HapRDecodesBc7IntoRgba() {
    // A mode-6 BC7 block with all endpoint and index bits zero. The existing BC7 decoder tests pin
    // this block to sixteen transparent-black pixels; this test pins the Hap section/type plumbing.
    var block = new byte[16];
    block[0] = 0x40;
    var frame = Cat(Header4(block.Length, 0xAC), block);

    var picture = Decode("Hap7", 4, 4, frame);

    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(picture.PixelData, Is.EqualTo(new byte[4 * 4 * 4]));
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ATopLevelTypeByteNamingNoPixelFormatIsRefused() {
    var frame = Cat(Header4(4, 0x77), new byte[] { 0, 0, 0, 0 });

    var failure = Assert.Throws<NotSupportedException>(() => Decode("Hap1", 4, 4, frame));
    Assert.That(failure!.Message, Does.Contain("names no pixel format"));
  }

  [Test]
  [Category("Unit")]
  public void ATextureOfTheWrongByteCountForItsPictureSizeIsRefused() {
    var frame = Cat(Header4(4, 0xAB), new byte[] { 0, 0, 0, 0 }); // one DXT1 block is 8 bytes, not 4

    var failure = Assert.Throws<InvalidDataException>(() => Decode("Hap1", 4, 4, frame));
    Assert.That(failure!.Message, Does.Contain("needs exactly"));
  }

  [Test]
  [Category("Unit")]
  public void ASectionSizeRunningPastTheEndOfTheDataIsRefused() {
    var frame = Cat(Header4(999, 0xAB), SolidColourBlock(0, 0));

    var failure = Assert.Throws<InvalidDataException>(() => Decode("Hap1", 4, 4, frame));
    Assert.That(failure!.Message, Does.Contain("runs past the end"));
  }

  [Test]
  [Category("Unit")]
  public void HapHRefusesAtCreateBecauseHdrCannotBeRepresentedExactly() {
    var hapH = Assert.Throws<NotSupportedException>(() => HapDecoder.Create(Stream("HapH", 4, 4)));
    Assert.That(hapH!.Message, Does.Contain("floating-point"));
    Assert.That(hapH.Message, Does.Contain("dynamic range"));
  }

  // ============================================================================================
  // Which codes this decoder answers for
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheCodecAnswersForEveryHapCode() {
    foreach (var code in new[] { "Hap1", "Hap5", "HapY", "HapM", "HapA", "Hap7", "HapH" })
      Assert.That(HapDecoder.Accepts(Stream(code, 4, 4)), Is.True, code);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecDoesNotAnswerForAnotherCode() {
    Assert.That(HapDecoder.Accepts(Stream("MJPG", 4, 4)), Is.False);
    Assert.That(HapDecoder.Accepts(Stream("FFV1", 4, 4)), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void EveryDecodableCodeIsReachableThroughTheRegistry() {
    foreach (var code in new[] { "Hap1", "Hap5", "HapY", "HapM", "HapA", "Hap7" })
      Assert.That(
        Hawkynt.FileFormats.Video.VideoFormatRegistry.CanDecode(Stream(code, 4, 4)), Is.True, code);
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static byte[] _SnappyLiteral(byte[] payload) {
    var tag = (byte)((payload.Length - 1) << 2);
    var result = new byte[1 + 1 + payload.Length];
    result[0] = (byte)payload.Length;
    result[1] = tag;
    payload.CopyTo(result, 2);
    return result;
  }

  private static byte[] _ChunkedFrame(byte pixelType, byte[][] chunks, byte[] compressors, bool withOffsetTable) {
    var sizeTable = new byte[chunks.Length * 4];
    var offsetTable = new byte[chunks.Length * 4];
    var running = 0;
    for (var i = 0; i < chunks.Length; ++i) {
      var size = chunks[i].Length;
      sizeTable[i * 4] = (byte)size;
      sizeTable[i * 4 + 1] = (byte)(size >> 8);
      offsetTable[i * 4] = (byte)running;
      offsetTable[i * 4 + 1] = (byte)(running >> 8);
      running += size;
    }

    var compressorSection = Cat(Header4(compressors.Length, 0x02), compressors);
    var sizeSection = Cat(Header4(sizeTable.Length, 0x03), sizeTable);
    var containerBody = withOffsetTable
      ? Cat(compressorSection, sizeSection, Cat(Header4(offsetTable.Length, 0x04), offsetTable))
      : Cat(compressorSection, sizeSection);

    var container = Cat(Header4(containerBody.Length, 0x01), containerBody);
    var frameData = new byte[0];
    foreach (var chunk in chunks)
      frameData = Cat(frameData, chunk);

    var sectionBody = Cat(container, frameData);
    return Cat(Header4(sectionBody.Length, (byte)(0xC0 | (pixelType & 0x0F))), sectionBody);
  }

  private static byte[] _Flat(int pixels, params byte[] rgb) {
    var result = new byte[pixels * rgb.Length];
    for (var i = 0; i < pixels; ++i)
      rgb.CopyTo(result, i * rgb.Length);

    return result;
  }

  /// <summary>Two 4x4 blocks side by side in an 8x4 picture, each solid, as Rgb24.</summary>
  private static byte[] _TwoBlocksSideBySide(byte r0, byte g0, byte b0, byte r1, byte g1, byte b1) {
    var result = new byte[8 * 4 * 3];
    for (var y = 0; y < 4; ++y) {
      for (var x = 0; x < 4; ++x) {
        var left = (y * 8 + x) * 3;
        result[left] = r0; result[left + 1] = g0; result[left + 2] = b0;
        var right = (y * 8 + x + 4) * 3;
        result[right] = r1; result[right + 1] = g1; result[right + 2] = b1;
      }
    }

    return result;
  }
}