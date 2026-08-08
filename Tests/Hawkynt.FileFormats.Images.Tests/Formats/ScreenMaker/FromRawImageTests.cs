using System;
using FileFormat.Core;

namespace FileFormat.ScreenMaker.Tests;

[TestFixture]
public sealed class ScreenMakerFileFromRawImageTests {

  /// <summary>A couple of hundred distinct colours, which a palette of 256 holds exactly.</summary>
  private static RawImage _Source(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var level = (x + y * 5) % 200;
      var colour = (level << 16) | ((255 - level) << 8) | (level / 2);
      var at = (y * width + x) * 3;
      rgb[at] = (byte)(colour >> 16);
      rgb[at + 1] = (byte)(colour >> 8);
      rgb[at + 2] = (byte)colour;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesAPictureTheFormatCanHold() {
    var source = _Source(64, 48);
    var decoded = ScreenMakerFile.ToRawImage(ScreenMakerFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(64));
      Assert.That(decoded.Height, Is.EqualTo(48));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    // The screen is one size and callers have whatever they have; refusing them would make encoding
    // useful only to those who already knew the size.
    var decoded = ScreenMakerFile.ToRawImage(ScreenMakerFile.FromRawImage(_Source(96, 72)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(96));
      Assert.That(decoded.Height, Is.EqualTo(72));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => ScreenMakerFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = ScreenMakerFile.FromRawImage(_Source(64, 48));
    var restored = ScreenMakerReader.FromBytes(ScreenMakerWriter.ToBytes(file));

    Assert.That(_Rgb(ScreenMakerFile.ToRawImage(restored)), Is.EqualTo(_Rgb(ScreenMakerFile.ToRawImage(file))));
  }
}
