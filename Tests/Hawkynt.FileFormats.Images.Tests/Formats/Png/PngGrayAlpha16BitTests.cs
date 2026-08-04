using System;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.Png.Tests;

/// <summary>
/// PNG's fourth colour type at a depth of sixteen.
/// </summary>
/// <remarks>
/// Grey with alpha existed here only at eight bits a channel, so a sixteen-bit one could not be
/// opened at all — not narrowed to what could be held, refused outright with "unsupported PNG color
/// type". Every other combination the specification allows had a pixel format and a route through
/// the converter; this one had neither.
/// <para/>
/// ImageMagick reads what this writes back to the same pixels, and reads its own such file to the
/// same pixels we do.
/// </remarks>
[TestFixture]
public class PngGrayAlpha16BitTests {

  /// <summary>A picture whose grey and whose alpha both need more than eight bits to tell apart.</summary>
  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      // Values one apart in the low byte, which narrowing to eight bits would merge.
      pixels[i * 4] = 0x40;
      pixels[i * 4 + 1] = (byte)(i & 0xFF);
      pixels[i * 4 + 2] = 0x80;
      pixels[i * 4 + 3] = (byte)(255 - (i & 0xFF));
    }

    return new() { Width = width, Height = height, Format = PixelFormat.GrayAlpha32, PixelData = pixels };
  }

  [Test]
  public void BitsPerPixel_IsThirtyTwo()
    => Assert.That(RawImage.BitsPerPixel(PixelFormat.GrayAlpha32), Is.EqualTo(32));

  [Test]
  public void HasAlpha_IsTrue() {
    var image = _Picture(4, 4);

    Assert.That(image.HasAlpha, Is.True);
  }

  [Test]
  public void RoundTrip_ThroughPngKeepsEveryBitOfGreyAndAlpha() {
    var source = _Picture(16, 16);

    var restored = PngFile.ToRawImage(PngReader.FromBytes(PngWriter.ToBytes(PngFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Format, Is.EqualTo(PixelFormat.GrayAlpha32));
      Assert.That(restored.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  public void ToBytes_StatesTheFourthColourTypeAtSixteenBits() {
    var bytes = PngWriter.ToBytes(PngFile.FromRawImage(_Picture(8, 8)));

    // IHDR's payload starts at byte 16: width, height, then depth and colour type.
    Assert.Multiple(() => {
      Assert.That(bytes[24], Is.EqualTo(16), "the bit depth");
      Assert.That(bytes[25], Is.EqualTo(4), "grey with alpha");
    });
  }

  [Test]
  public void Convert_ToRgbaKeepsBothChannelsWhole() {
    var source = _Picture(2, 1);

    var wide = PixelConverter.Convert(source, PixelFormat.Rgba64).PixelData;

    // The first pixel is grey 0x4000 and alpha 0x80FF; nothing of either may be dropped.
    Assert.Multiple(() => {
      Assert.That(wide[..2], Is.EqualTo(new byte[] { 0x40, 0x00 }), "red takes the grey");
      Assert.That(wide[2..4], Is.EqualTo(new byte[] { 0x40, 0x00 }), "and so does green");
      Assert.That(wide[6..8], Is.EqualTo(new byte[] { 0x80, 0xFF }), "alpha whole");
    });
  }

  [Test]
  public void Convert_BackFromRgbaReturnsWhatWentIn() {
    var source = _Picture(8, 8);

    var wide = PixelConverter.Convert(source, PixelFormat.Rgba64);
    var narrow = PixelConverter.Convert(wide, PixelFormat.GrayAlpha32);

    Assert.That(narrow.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  public void Convert_ToBgraTakesTheHighByteOfEach() {
    var source = _Picture(1, 1);

    var bgra = PixelConverter.Convert(source, PixelFormat.Bgra32).PixelData;

    Assert.Multiple(() => {
      Assert.That(bgra[0], Is.EqualTo(0x40));
      Assert.That(bgra[1], Is.EqualTo(0x40));
      Assert.That(bgra[2], Is.EqualTo(0x40));
      Assert.That(bgra[3], Is.EqualTo(0x80));
    });
  }

  [Test]
  public void Convert_FromTheEightBitFormWidensByRepeating() {
    // 255 must become 65535 rather than 65280, or full opacity stops being full.
    var eight = new RawImage {
      Width = 1, Height = 1, Format = PixelFormat.GrayAlpha16, PixelData = [0x7F, 0xFF],
    };

    var wide = PixelConverter.Convert(eight, PixelFormat.GrayAlpha32).PixelData;

    Assert.That(wide, Is.EqualTo(new byte[] { 0x7F, 0x7F, 0xFF, 0xFF }));
  }
}
