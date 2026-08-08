using System;
using FileFormat.Core;

namespace FileFormat.Crack.Tests;

[TestFixture]
public sealed class CrackFileFromRawImageTests {

  /// <summary>Eight colours built from the ends of each channel, which a nine-bit Atari palette keeps exactly.</summary>
  private static RawImage _Source(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var slot = (x / 8 + y / 8 * 3) % 8;
      var colour = ((slot & 1) == 0 ? 0 : 0xFF0000) | ((slot & 2) == 0 ? 0 : 0x00FF00) | ((slot & 4) == 0 ? 0 : 0x0000FF);
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
    var source = _Source(320, 200);
    var decoded = CrackFile.ToRawImage(CrackFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(200));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    // The screen is one size and callers have whatever they have; refusing them would make encoding
    // useful only to those who already knew the size.
    var decoded = CrackFile.ToRawImage(CrackFile.FromRawImage(_Source(96, 72)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => CrackFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = CrackFile.FromRawImage(_Source(320, 200));
    var restored = CrackReader.FromBytes(CrackWriter.ToBytes(file));

    Assert.That(_Rgb(CrackFile.ToRawImage(restored)), Is.EqualTo(_Rgb(CrackFile.ToRawImage(file))));
  }
}
