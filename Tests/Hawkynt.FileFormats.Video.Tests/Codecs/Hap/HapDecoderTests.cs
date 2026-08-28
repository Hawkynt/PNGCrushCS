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
    var c0 = Rgb565(31, 63, 31);
    var c1 = Rgb565(1, 1, 1);
    var block = FourColourBlock(c0, c1);
    var frame = Cat(Header4(block.Length, 0xAB), block);

    var picture = Decode("Hap1", 4, 1, frame);

    Assert.That(picture.PixelData, Is.EqualTo(new byte[] {
      255, 255, 255, 8, 4, 8, 172, 171, 172, 90, 87, 90,
    }));
  }

  [Test]
  [Category("Unit")]
  public void TheTwoColourBranchsThirdEntryIsBlackWithNoAlphaChannelToCarryItsTransparency() {
    var block = FourColourBlock(0, Rgb565(31, 63, 31));
    var frame = Cat(Header4(block.Length, 0xAB), block);

    var picture = Decode("Hap1", 4, 1, frame);

    Assert.That(picture.PixelData[9], Is.EqualTo(0));
    Assert.That(picture.PixelData[10], Is.EqualTo(0));
    Assert.That(picture.PixelData[11], Is.EqualTo(0));
  }

  // ============================================================================================
  // RGBA DXT5
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ADxt5FrameCarriesRealAlpha() {
    var c0 = Rgb565(31, 0, 0);
    var block = new byte[] {
      200, 200,
      0, 0, 0, 0, 0, 0,
      (byte)c0, (byte)(c0 >> 8), 0, 0,
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
    var snappy = new byte[] { 8, 0x00, 0x41, 0x1A, 0x01, 0x00 };
    var decoded = HapSnappyDecoder.Decompress(snappy);
    Assert.That(decoded, Is.EqualTo(new byte[] { 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41, 0x41 }));
  }

  [Test]
  [Category("Unit")]
  public void SnappyOneByteOffsetCopiesReadTheOffsetsThreeHighBitsFromTheTagByte() {
    var literalTag = (byte)((2 - 1) << 2);
    var copyTag = (byte)((((4 - 4) & 0x7) << 2) | (((2 >> 8) & 0x7) << 5) | 1);
    var snappy = new byte[] { 6, literalTag, 0x41, 0x42, copyTag, 2 & 0xFF };
    var decoded = HapSnappyDecoder.Decompress(snappy);
    Assert.That(decoded, Is.EqualTo(new byte[] { 0x41, 0x42, 0x41, 0x42, 0x41, 0x42 }));
  }

  [Test]
  [Category("Unit")]
  public void ASnappyCopyBeforeTheStartOfTheOutputIsRefused() {
    var snappy = new byte[] { 4, 0x1A, 0x05, 0x00 };
    var failure = Assert.Throws<InvalidDataException>(() => HapSnappyDecoder.Decompress(snappy));
    Assert.That(failure!.Message, Does.Contain("not a place already written"));
  }

  [Test]
  [Category("Unit")]
  public void ASnappyBlockProducingTheWrongLengthIsRefused() {
    var snappy = new byte[] { 4, 0x00, 0x41 };
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
    var block = new byte[] {
      100, 100, 0, 0, 0, 0, 0, 0,
      0, 0, 0, 0,
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
    var (r, g, b) = HapYCoCgConversion.ToRgb(128, 128, 0, 100);
    Assert.That((r, g, b), Is.EqualTo(((byte)100, (byte)100, (byte)100)));
  }

  [Test]
  [Category("Unit")]
  public void ScaledYCoCgConversionScalesChromaByTheBlueChannelsField() {
    var (r, g, b) = HapYCoCgConversion.ToRgb(132, 128, 8, 100);
    Assert.That((r, g, b), Is.EqualTo(((byte)102, (byte)100, (byte)98)));
  }

  // ============================================================================================
  // Hap Q Alpha
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void HapQAlphaCombinesAScaledYCoCgImageWithASeparateRgtc1AlphaImage() {
    var colourBlock = new byte[] { 100, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    var alphaBlock = new byte[] { 200, 200, 0, 0, 0, 0, 0, 0 };

    var colourSection = Cat(Header4(colourBlock.Length, 0xAF), colourBlock);
    var alphaSection = Cat(Header4(alphaBlock.Length, 0xA1), alphaBlock);
    var multi = Cat(colourSection, alphaSection);
    var frame = Cat(Header4(multi.Length, 0x0D), multi);

    var picture = Decode("HapM", 4, 4, frame);

    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(picture.PixelData, Is.EqualTo(_Flat(4 * 4, 100, 0, 255, 200)));
  }

  [Test]
  [Category("Unit")]
  public void TheOrderOfTheTwoImagesInAHapQAlphaFrameDoesNotMatter() {
    var colourBlock = new byte[] { 100, 100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    var alphaBlock = new byte[] { 200, 200, 0, 0, 0, 0, 0, 0 };

    var alphaSection = Cat(Header4(alphaBlock.Length, 0xA1), alphaBlock);
    var colourSection = Cat(Header4(colourBlock.Length, 0xAF), colourBlock);
    var multi = Cat(alphaSection, colourSection);
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
    var block = new byte[16];
    block[0] = 0x40;
    var frame = Cat(Header4(block.Length, 0xAC), block);

    var picture = Decode("Hap7", 4, 4, frame);

    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(picture.PixelData, Is.EqualTo(new byte[4 * 4 * 4]));
  }

  // ============================================================================================
  // Hap HDR — unsigned and signed BC6H
  // ============================================================================================

  [TestCase(0xA2)]
  [TestCase(0xA3)]
  [Category("Unit")]
  public void HapHdrPreservesBc6AsHalfFloatRgb(int typeByte) {
    // 0x13 in the low five bits is a reserved BC6H mode. The format defines a reserved mode as zero,
    // which makes this a stable plumbing vector for both the unsigned and signed Hap HDR pixel types
    // without manufacturing expected HDR values in the test itself.
    var block = new byte[16];
    block[0] = 0x13;
    var frame = Cat(Header4(block.Length, (byte)typeByte), block);

    var picture = Decode("HapH", 4, 4, frame);

    Assert.That(picture.Format, Is.EqualTo(PixelFormat.RgbF16));
    Assert.That(picture.PixelData, Has.Length.EqualTo(4 * 4 * 3 * 2));
    Assert.That(picture.PixelData, Is.All.Zero);
    Assert.That(picture.ColorInfo!.Range, Is.EqualTo(RawColorRange.Full));
    Assert.That(picture.ColorInfo.Matrix, Is.EqualTo(RawMatrixCoefficients.Identity));
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
    var frame = Cat(Header4(4, 0xAB), new byte[] { 0, 0, 0, 0 });

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
    foreach (var code in new[] { "Hap1", "Hap5", "HapY", "HapM", "HapA", "Hap7", "HapH" })
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
    var frameData = Array.Empty<byte>();
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