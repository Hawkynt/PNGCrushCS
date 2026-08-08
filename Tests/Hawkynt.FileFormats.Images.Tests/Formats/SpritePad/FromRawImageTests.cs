using System;
using FileFormat.Core;

namespace FileFormat.SpritePad.Tests;

[TestFixture]
public sealed class SpritePadFileFromRawImageTests {

  /// <summary>Set and clear pixels, which is the whole of what a hi-res sprite can say.</summary>
  private static RawImage _Source(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var level = (x + y) % 2 * 255;
      var colour = (level << 16) | (level << 8) | level;
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
    var source = _Source(24, 21);
    var decoded = SpritePadFile.ToRawImage(SpritePadFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(24));
      Assert.That(decoded.Height, Is.EqualTo(21));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    // The screen is one size and callers have whatever they have; refusing them would make encoding
    // useful only to those who already knew the size.
    var decoded = SpritePadFile.ToRawImage(SpritePadFile.FromRawImage(_Source(96, 72)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(24));
      Assert.That(decoded.Height, Is.EqualTo(21));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => SpritePadFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = SpritePadFile.FromRawImage(_Source(24, 21));
    var restored = SpritePadReader.FromBytes(SpritePadWriter.ToBytes(file));

    Assert.That(_Rgb(SpritePadFile.ToRawImage(restored)), Is.EqualTo(_Rgb(SpritePadFile.ToRawImage(file))));
  }
}
