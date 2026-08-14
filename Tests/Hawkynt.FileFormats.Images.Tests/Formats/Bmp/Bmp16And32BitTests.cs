using System.IO;
using FileFormat.Core;

namespace FileFormat.Bmp.Tests;

/// <summary>The two depths that came back as a different picture than the file held.</summary>
/// <remarks>
/// Both defects were silent: the reader announced success and handed back wrong samples, which is
/// worse than a refusal because nothing downstream can tell. Every expectation below was measured
/// against ffmpeg n9.0 and ImageMagick 7.1.2 on a file built by this same helper, and the two tools
/// agree with each other everywhere except the one case called out on
/// <see cref="FromSpan_Bpp32_V4AlphaMaskAllZero_KeepsTheDeclaredAlpha"/>.
/// </remarks>
[TestFixture]
public sealed class Bmp16And32BitTests {

  private const int _BI_RGB = 0;
  private const int _BI_BITFIELDS = 3;

  #region 16 bits per pixel

  [Test]
  [Category("Unit")]
  public void FromSpan_Bpp16_BiRgb_IsFiveFiveFive() {
    // BI_RGB at 16bpp is 5-5-5 with the top bit unused. Read as 5-6-5 the same word comes back as a
    // half-intensity red, which is how 387 of 2257 pixels of an ffmpeg-written gradient were wrong.
    var bmp = _Build16(_BI_RGB, null, 0x7C00);
    var image = BmpFile.ToRawImage(BmpReader.FromSpan(bmp));

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Bgr24));
    _AssertFirstPixelRgb(image, 255, 0, 0);
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_Bpp16_BiRgb_MidValueIsNotAPlainShift() {
    // A 5-bit 13 is 107, not the 104 shifting the bits up would give. Both tools agree here.
    var bmp = _Build16(_BI_RGB, null, 13 << 10);
    var image = BmpFile.ToRawImage(BmpReader.FromSpan(bmp));

    _AssertFirstPixelRgb(image, 107, 0, 0);
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_Bpp16_BiRgb_FullScaleChannelReachesWhite() {
    var bmp = _Build16(_BI_RGB, null, (31 << 10) | (31 << 5) | 31);
    var image = BmpFile.ToRawImage(BmpReader.FromSpan(bmp));

    _AssertFirstPixelRgb(image, 255, 255, 255);
  }

  [TestCase(3, 24)]
  [TestCase(7, 57)]
  [TestCase(24, 198)]
  [TestCase(28, 231)]
  [Category("Unit")]
  public void FromSpan_Bpp16_ChannelWideningRepeatsTheBits(int stored, int expected) {
    // The four values of a 5-bit channel that tell the two candidate rules apart. Sweeping all 32
    // through ffmpeg n9.0 put it on bit replication at 32 of 32; rounding the scale gives 25, 58, 197
    // and 230 here instead, which is what left 488 of 2257 pixels of an ffmpeg-written gradient off
    // by one. ImageMagick agrees with ffmpeg on the last two and not the first two, matching neither
    // rule cleanly, so there is no reading that satisfies both tools and we follow ffmpeg.
    var bmp = _Build16(_BI_RGB, null, stored << 10);
    var image = BmpFile.ToRawImage(BmpReader.FromSpan(bmp));

    _AssertFirstPixelRgb(image, expected, 0, 0);
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_Bpp16_Bitfields555_UsesTheMasks() {
    var bmp = _Build16(_BI_BITFIELDS, [0x7C00, 0x03E0, 0x001F], 0x7C00);
    var image = BmpFile.ToRawImage(BmpReader.FromSpan(bmp));

    _AssertFirstPixelRgb(image, 255, 0, 0);
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_Bpp16_Bitfields565_UsesTheMasks() {
    // 5-6-5 is one legal combination among others and only when the masks say so.
    var bmp = _Build16(_BI_BITFIELDS, [0xF800, 0x07E0, 0x001F], 0xF800);
    var image = BmpFile.ToRawImage(BmpReader.FromSpan(bmp));

    _AssertFirstPixelRgb(image, 255, 0, 0);
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_Bpp16_Bitfields565_GreenIsSixBitsWide() {
    // The green channel is the only one that tells 5-6-5 apart from 5-5-5 at a glance: 43 of 63.
    var bmp = _Build16(_BI_BITFIELDS, [0xF800, 0x07E0, 0x001F], 43 << 5);
    var image = BmpFile.ToRawImage(BmpReader.FromSpan(bmp));

    _AssertFirstPixelRgb(image, 0, 174, 0);
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_Bpp16_BitfieldsInAnUnusualOrder_UsesTheMasks() {
    // Neither of the two common layouts, so nothing that hard-codes one can get this right. Blue in
    // the top five bits, red in the bottom five.
    var bmp = _Build16(_BI_BITFIELDS, [0x001F, 0x03E0, 0x7C00], 0x001F);
    var image = BmpFile.ToRawImage(BmpReader.FromSpan(bmp));

    _AssertFirstPixelRgb(image, 255, 0, 0);
  }

  #endregion

  #region 32 bits per pixel

  [Test]
  [Category("Unit")]
  public void FromSpan_Bpp32_BiRgb_IsNotIndexed() {
    // It came back Indexed1 with no palette, and then threw on colour conversion far from the cause.
    var bmp = _Build32(40, _BI_RGB, null, 0xFF);
    var file = BmpReader.FromSpan(bmp);

    Assert.That(file.BitsPerPixel, Is.EqualTo(32));
    Assert.That(BmpFile.ToRawImage(file).Format, Is.Not.EqualTo(PixelFormat.Indexed1));
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_Bpp32_BiRgb_ReadsBlueGreenRedInThatOrder() {
    var bmp = _Build32(40, _BI_RGB, null, 0xFF);
    var image = BmpFile.ToRawImage(BmpReader.FromSpan(bmp));

    _AssertFirstPixelRgb(image, 255, 0, 0);
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_Bpp32_BiRgb_PartialAlphaIsKept() {
    // Whether the fourth byte is alpha or padding is not stated by biCompression. Both tools read it
    // as alpha whenever it carries anything at all, so a half-transparent pixel stays half.
    var bmp = _Build32(40, _BI_RGB, null, 0x80);
    var image = BmpFile.ToRawImage(BmpReader.FromSpan(bmp));

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Bgra32));
    Assert.That(image.PixelData[3], Is.EqualTo(0x80));
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_Bpp32_BiRgb_AllZeroAlphaIsPadding() {
    // The case that decides it. A great many writers leave the fourth byte at zero as padding, and
    // taking that literally turns an opaque picture into an invisible one. ffmpeg substitutes an
    // opaque alpha and ImageMagick drops the channel; both render the file opaque, so we do too.
    var bmp = _Build32(40, _BI_RGB, null, 0x00);
    var image = BmpFile.ToRawImage(BmpReader.FromSpan(bmp));

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Bgr24));
    _AssertFirstPixelRgb(image, 255, 0, 0);
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_Bpp32_V4AlphaMaskAllZero_KeepsTheDeclaredAlpha() {
    // The one place the two tools disagree. ImageMagick honours the declared mask and reports a
    // fully transparent picture; ffmpeg applies the same all-zero rescue it uses for BI_RGB and
    // reports an opaque one. We follow ImageMagick: a header that goes out of its way to declare an
    // alpha mask has stated something, and second-guessing a stated channel is how a legitimately
    // transparent picture becomes a wrong one. The rescue above fills a gap where nothing was said.
    var bmp = _Build32(108, _BI_BITFIELDS, [0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000], 0x00);
    var image = BmpFile.ToRawImage(BmpReader.FromSpan(bmp));

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Bgra32));
    Assert.That(image.PixelData[3], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_Bpp32_V4WithoutAlphaMask_IsOpaque() {
    // An alpha mask of zero is a statement that there is no alpha. ffmpeg says bgr0 and ImageMagick
    // reports three channels, so the fourth byte is padding whatever it holds.
    var bmp = _Build32(108, _BI_BITFIELDS, [0x00FF0000, 0x0000FF00, 0x000000FF, 0x00000000], 0x00);
    var image = BmpFile.ToRawImage(BmpReader.FromSpan(bmp));

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Bgr24));
    _AssertFirstPixelRgb(image, 255, 0, 0);
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_Bpp32_BitfieldsInAnUnusualOrder_UsesTheMasks() {
    // Red and blue exchanged against the usual layout, which only the masks distinguish.
    var bmp = _Build32(40, _BI_BITFIELDS, [0x000000FF, 0x0000FF00, 0x00FF0000], 0x00);
    var image = BmpFile.ToRawImage(BmpReader.FromSpan(bmp));

    // The pixel bytes are B=0,G=0,R=255 in file order; with the masks exchanged the low byte is red.
    _AssertFirstPixelRgb(image, 0, 0, 255);
  }

  #endregion

  #region writer

  [Test]
  [Category("Unit")]
  public void Writer_Bgra32_RoundTripsThroughTheReaderWithAlphaIntact() {
    // Reading 32-bit files correctly is only half of it: with no 32-bit path on the way out, a
    // 32-bit bitmap opened and saved came back 24-bit and silently lost its transparency.
    var pixels = new byte[2 * 2 * 4];
    for (var i = 0; i < 4; ++i) {
      pixels[i * 4] = 10;              // B
      pixels[i * 4 + 1] = 20;          // G
      pixels[i * 4 + 2] = 30;          // R
      pixels[i * 4 + 3] = (byte)(i * 60 + 15);
    }

    var written = BmpWriter.ToBytes(new BmpFile {
      Width = 2,
      Height = 2,
      BitsPerPixel = 32,
      PixelData = pixels,
      RowOrder = BmpRowOrder.TopDown,
      Compression = BmpCompression.None,
      ColorMode = BmpColorMode.Bgra32
    });

    var image = BmpFile.ToRawImage(BmpReader.FromSpan(written));

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Bgra32));
    Assert.That(image.PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_OpaqueBgra32_StillWritesTwentyFourBit() {
    // The other half of the writer change, and the one that could have cost every caller a third
    // more file for nothing: a picture whose alpha is opaque throughout has no transparency to keep,
    // so it takes the 24-bit output it always had. Only a picture that actually has some goes wide.
    var pixels = new byte[2 * 4];
    for (var i = 0; i < 2; ++i)
      pixels[i * 4 + 3] = 0xFF;

    var file = BmpFile.FromRawImage(new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = pixels
    });

    Assert.That(file.ColorMode, Is.EqualTo(BmpColorMode.Rgb24));
    Assert.That(file.BitsPerPixel, Is.EqualTo(24));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Bgra32_DoesNotNarrowToBgr24() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = [1, 2, 3, 4]
    };

    var file = BmpFile.FromRawImage(image);

    Assert.That(file.ColorMode, Is.EqualTo(BmpColorMode.Bgra32));
    Assert.That(file.BitsPerPixel, Is.EqualTo(32));
  }

  #endregion

  #region helpers

  private static void _AssertFirstPixelRgb(RawImage image, int r, int g, int b) {
    var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32).PixelData;
    Assert.Multiple(() => {
      Assert.That(bgra[2], Is.EqualTo(r), "red");
      Assert.That(bgra[1], Is.EqualTo(g), "green");
      Assert.That(bgra[0], Is.EqualTo(b), "blue");
    });
  }

  /// <summary>A 2x2 top-down 16bpp bitmap whose every pixel is <paramref name="pixel"/>.</summary>
  private static byte[] _Build16(int compression, uint[]? masks, int pixel) {
    var rows = new byte[2 * 4]; // 2 px * 2 bytes = 4 bytes a row, already 4-byte aligned
    for (var i = 0; i < 4; ++i) {
      rows[i * 2] = (byte)(pixel & 0xFF);
      rows[i * 2 + 1] = (byte)((pixel >> 8) & 0xFF);
    }

    return _Assemble(40, 2, 2, 16, compression, masks, rows);
  }

  /// <summary>A 2x2 top-down 32bpp bitmap of opaque-red-with-<paramref name="alpha"/> pixels.</summary>
  private static byte[] _Build32(int headerSize, int compression, uint[]? masks, byte alpha) {
    var rows = new byte[2 * 2 * 4];
    for (var i = 0; i < 4; ++i) {
      rows[i * 4] = 0;         // B
      rows[i * 4 + 1] = 0;     // G
      rows[i * 4 + 2] = 255;   // R
      rows[i * 4 + 3] = alpha;
    }

    return _Assemble(headerSize, 2, 2, 32, compression, masks, rows);
  }

  private static byte[] _Assemble(
    int headerSize, int width, int height, int bitsPerPixel, int compression, uint[]? masks, byte[] rows) {
    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);

    // Masks live inside anything from a BITMAPV4HEADER up, and directly after a plain
    // BITMAPINFOHEADER when the compression says BI_BITFIELDS.
    var trailingMaskBytes = headerSize == 40 && compression == _BI_BITFIELDS ? 12 : 0;
    var pixelOffset = 14 + headerSize + trailingMaskBytes;

    bw.Write((byte)'B');
    bw.Write((byte)'M');
    bw.Write(pixelOffset + rows.Length);
    bw.Write((short)0);
    bw.Write((short)0);
    bw.Write(pixelOffset);

    bw.Write(headerSize);
    bw.Write(width);
    bw.Write(-height); // negative = top-down
    bw.Write((short)1);
    bw.Write((short)bitsPerPixel);
    bw.Write(compression);
    bw.Write(rows.Length);
    bw.Write(2835);
    bw.Write(2835);
    bw.Write(0);
    bw.Write(0);

    if (headerSize == 40) {
      if (trailingMaskBytes > 0)
        for (var i = 0; i < 3; ++i)
          bw.Write(masks![i]);
    } else {
      // BITMAPV4HEADER: four masks, a colour space tag, a CIEXYZTRIPLE and three gamma words.
      for (var i = 0; i < 4; ++i)
        bw.Write(i < masks!.Length ? masks[i] : 0u);
      bw.Write(0x73524742); // 'BGRs' — sRGB
      for (var i = 0; i < 9; ++i)
        bw.Write(0);
      for (var i = 0; i < 3; ++i)
        bw.Write(0);
    }

    bw.Write(rows);
    bw.Flush();
    return ms.ToArray();
  }

  #endregion
}
