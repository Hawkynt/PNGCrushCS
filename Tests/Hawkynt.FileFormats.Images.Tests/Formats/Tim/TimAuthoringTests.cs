using System;
using FileFormat.Core;
using FileFormat.Tim;

namespace FileFormat.Tim.Tests;

/// <summary>
/// Building a PlayStation TIM texture.
/// </summary>
/// <remarks>
/// Sixteen bits a pixel, five to each channel, no colour table. RECOIL reads what this writes and
/// draws what we draw.
/// </remarks>
[TestFixture]
public class TimAuthoringTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 3] = (byte)(i * 8 % 256);
      pixels[i * 3 + 1] = 0x40;
      pixels[i * 3 + 2] = 0xF8;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => TimFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_IsSixteenBitWithNoColourTable() {
    var file = TimFile.FromRawImage(_Picture(8, 8));

    Assert.Multiple(() => {
      Assert.That(file.Bpp, Is.EqualTo(TimBpp.Bpp16));
      Assert.That(file.HasClut, Is.False);
      Assert.That(file.PixelData, Has.Length.EqualTo(8 * 8 * 2));
    });
  }

  [Test]
  public void FromRawImage_BlackKeepsTheSemiTransparencyBit() {
    // A pixel of all zeros with that bit clear is not black but a hole, so black must set it.
    var black = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Rgb24, PixelData = [0, 0, 0] };

    var file = TimFile.FromRawImage(black);

    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 0x00, 0x80 }));
  }

  [Test]
  public void FromRawImage_PacksFiveBitsAChannelInTheRightOrder() {
    // Full red only: five bits at the bottom of the word.
    var red = new RawImage { Width = 1, Height = 1, Format = PixelFormat.Rgb24, PixelData = [255, 0, 0] };

    var file = TimFile.FromRawImage(red);

    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 0x1F, 0x00 }));
  }

  [Test]
  public void ToBytes_OpensWithTheTimMagic() {
    var bytes = TimWriter.ToBytes(TimFile.FromRawImage(_Picture(4, 4)));

    Assert.That(bytes[..4], Is.EqualTo(new byte[] { 0x10, 0x00, 0x00, 0x00 }));
  }

  [Test]
  public void RoundTrip_AChannelAtEitherEndComesBackExactly() {
    // Five bits are expanded on the way out by repeating the top of them, so the range still runs
    // the whole way: 31 comes back as 255 rather than 248. Nought and full survive; what lies
    // between is only as close as five bits allow.
    var source = new RawImage {
      Width = 2, Height = 1, Format = PixelFormat.Rgb24, PixelData = [255, 0, 255, 0, 255, 0],
    };

    var restored = TimFile.ToRawImage(TimReader.FromBytes(TimWriter.ToBytes(TimFile.FromRawImage(source))));

    Assert.That(restored.ToRgb24(), Is.EqualTo(source.PixelData));
  }

  [Test]
  public void RoundTrip_WhatComesBackIsWithinWhatFiveBitsCost() {
    var source = _Picture(16, 16);

    var restored = TimFile.ToRawImage(TimReader.FromBytes(TimWriter.ToBytes(TimFile.FromRawImage(source)))).ToRgb24();

    Assert.Multiple(() => {
      for (var i = 0; i < restored.Length; ++i)
        Assert.That(Math.Abs(restored[i] - source.PixelData[i]), Is.LessThanOrEqualTo(8), $"channel {i}");
    });
  }

  [Test]
  public void RoundTrip_KeepsTheSize() {
    var restored = TimFile.ToRawImage(TimReader.FromBytes(TimWriter.ToBytes(TimFile.FromRawImage(_Picture(17, 5)))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(17));
      Assert.That(restored.Height, Is.EqualTo(5));
    });
  }

  [Test]
  public void FromRawImage_AcceptsAPictureThatIsNotAlreadyThreeBytesAPixel() {
    var indexed = new RawImage {
      Width = 2, Height = 1,
      Format = PixelFormat.Indexed8,
      PixelData = [0, 1],
      Palette = [248, 0, 0, 0, 0, 248],
      PaletteCount = 2,
    };

    var file = TimFile.FromRawImage(indexed);

    Assert.That(file.PixelData, Is.EqualTo(new byte[] { 0x1F, 0x00, 0x00, 0x7C }));
  }
}
