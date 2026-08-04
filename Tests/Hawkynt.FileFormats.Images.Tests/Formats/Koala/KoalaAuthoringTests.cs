using System;
using FileFormat.Core;
using FileFormat.Koala;

namespace FileFormat.Koala.Tests;

/// <summary>
/// Reducing an arbitrary picture to the screen Koala Painter saved.
/// </summary>
/// <remarks>
/// A file this produces was decoded by RECOIL and by XnView, which agreed with each other and with
/// this project's own reader to the pixel.
/// </remarks>
[TestFixture]
public class KoalaAuthoringTests {

  /// <summary>A picture of one flat colour, which the machine can hold exactly if it is one of its own.</summary>
  private static RawImage _Flat(byte red, byte green, byte blue) {
    var pixels = new byte[KoalaFile.FixedWidth * KoalaFile.FixedHeight * 3];
    for (var i = 0; i < pixels.Length; i += 3) {
      pixels[i] = red;
      pixels[i + 1] = green;
      pixels[i + 2] = blue;
    }

    return new() {
      Width = KoalaFile.FixedWidth,
      Height = KoalaFile.FixedHeight,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  [Test]
  public void FromRawImage_NullImage_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => KoalaFile.FromRawImage(null!));

  [Test]
  public void FromRawImage_FillsEverySectionToItsStatedSize() {
    var file = KoalaFile.FromRawImage(_Flat(0, 0, 0));

    Assert.Multiple(() => {
      Assert.That(file.BitmapData, Has.Length.EqualTo(8000));
      Assert.That(file.VideoMatrix, Has.Length.EqualTo(1000));
      Assert.That(file.ColorRam, Has.Length.EqualTo(1000));
      Assert.That(file.Width, Is.EqualTo(160));
      Assert.That(file.Height, Is.EqualTo(200));
    });
  }

  [Test]
  public void FromRawImage_CarriesTheAddressKoalaPainterWrote() {
    // Nothing reads it back, but every tool recognising these by their first two bytes looks for it.
    Assert.That(KoalaFile.FromRawImage(_Flat(0, 0, 0)).LoadAddress, Is.EqualTo(0x6000));
  }

  [Test]
  public void ToBytes_IsTheSizeAKoalaFileIs() {
    var bytes = KoalaWriter.ToBytes(KoalaFile.FromRawImage(_Flat(0, 0, 0)));

    Assert.That(bytes, Has.Length.EqualTo(KoalaFile.ExpectedFileSize));
  }

  [Test]
  public void RoundTrip_AFlatPictureOfOneMachineColourComesBackExactly() {
    // Black is one of the sixteen, so a screen of it costs the reduction nothing and any difference
    // afterwards is the encoder's own doing rather than the palette's.
    var source = _Flat(0, 0, 0);

    var bytes = KoalaWriter.ToBytes(KoalaFile.FromRawImage(source));
    var restored = KoalaFile.ToRawImage(KoalaReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(160));
      Assert.That(restored.Height, Is.EqualTo(200));
      Assert.That(restored.ToRgb24(), Is.All.EqualTo(0));
    });
  }

  [Test]
  public void RoundTrip_APictureOfMoreColoursThanACellHoldsStillComesBackTheRightShape() {
    var pixels = new byte[160 * 200 * 3];
    for (var i = 0; i < 160 * 200; ++i) {
      pixels[i * 3] = (byte)(i % 256);
      pixels[i * 3 + 1] = (byte)(i / 160 % 256);
      pixels[i * 3 + 2] = 128;
    }

    var source = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = pixels };
    var restored = KoalaFile.ToRawImage(KoalaReader.FromBytes(KoalaWriter.ToBytes(KoalaFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(160));
      Assert.That(restored.Height, Is.EqualTo(200));
      Assert.That(restored.ToRgb24(), Has.Length.EqualTo(160 * 200 * 3));
    });
  }

  [Test]
  public void FromRawImage_AcceptsAPictureOfAnyOtherSize() {
    // The screen is one fixed shape, so anything else is resampled onto it rather than refused.
    var small = new RawImage { Width = 8, Height = 8, Format = PixelFormat.Rgb24, PixelData = new byte[8 * 8 * 3] };

    Assert.That(KoalaWriter.ToBytes(KoalaFile.FromRawImage(small)), Has.Length.EqualTo(KoalaFile.ExpectedFileSize));
  }
}
