using System;
using FileFormat.Core;
using FileFormat.DoodleAtari;

namespace FileFormat.DoodleAtari.Tests;

/// <summary>
/// Reducing a picture to the Atari ST's monochrome screen.
/// </summary>
/// <remarks>
/// 640 by 400, one bit a pixel, and a set bit meaning black. RECOIL reads what this writes and draws
/// what we draw.
/// </remarks>
[TestFixture]
public class DoodleAtariAuthoringTests {

  private static RawImage _Flat(byte value) {
    var pixels = new byte[640 * 400 * 3];
    Array.Fill(pixels, value);

    return new() { Width = 640, Height = 400, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => DoodleAtariFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_IsTheSizeTheScreenIs()
    => Assert.That(DoodleAtariFile.FromRawImage(_Flat(255)).PixelData, Has.Length.EqualTo(32000));

  [Test]
  public void FromRawImage_WhiteLeavesEveryBitClear()
    => Assert.That(DoodleAtariFile.FromRawImage(_Flat(255)).PixelData, Is.All.EqualTo(0x00));

  [Test]
  public void FromRawImage_BlackSetsEveryBit()
    => Assert.That(DoodleAtariFile.FromRawImage(_Flat(0)).PixelData, Is.All.EqualTo(0xFF));

  [Test]
  public void FromRawImage_ThresholdsOnBrightnessRatherThanOnOneChannel() {
    // Saturated blue is dark and saturated yellow is light, though each has a full channel. Taking
    // any single channel would call one of them the wrong side of the middle.
    var pixels = new byte[640 * 400 * 3];
    for (var i = 0; i < 640 * 400; ++i) {
      var blue = (i % 640) < 320;
      pixels[i * 3] = blue ? (byte)0 : (byte)255;
      pixels[i * 3 + 1] = blue ? (byte)0 : (byte)255;
      pixels[i * 3 + 2] = blue ? (byte)255 : (byte)0;
    }

    var file = DoodleAtariFile.FromRawImage(
      new() { Width = 640, Height = 400, Format = PixelFormat.Rgb24, PixelData = pixels });

    Assert.Multiple(() => {
      Assert.That(file.PixelData[0], Is.EqualTo(0xFF), "blue is dark");
      Assert.That(file.PixelData[79], Is.EqualTo(0x00), "yellow is light");
    });
  }

  [Test]
  public void RoundTrip_AHalfAndHalfPictureComesBackAsItself() {
    var pixels = new byte[640 * 400 * 3];
    for (var y = 0; y < 400; ++y)
      for (var x = 0; x < 640; ++x)
        if (x >= 320)
          Array.Fill(pixels, (byte)255, (y * 640 + x) * 3, 3);

    var source = new RawImage { Width = 640, Height = 400, Format = PixelFormat.Rgb24, PixelData = pixels };
    var bytes = DoodleAtariWriter.ToBytes(DoodleAtariFile.FromRawImage(source));

    var drawn = DoodleAtariFile.ToRawImage(DoodleAtariReader.FromBytes(bytes)).ToRgb24();

    Assert.Multiple(() => {
      Assert.That(drawn[0], Is.EqualTo(0), "the left half is black");
      Assert.That(drawn[320 * 3], Is.EqualTo(255), "the right half is white");
    });
  }

  [Test]
  public void FromRawImage_AcceptsAPictureOfAnotherSize() {
    var small = new RawImage { Width = 16, Height = 16, Format = PixelFormat.Rgb24, PixelData = new byte[16 * 16 * 3] };

    Assert.That(DoodleAtariFile.FromRawImage(small).PixelData, Has.Length.EqualTo(32000));
  }
}
