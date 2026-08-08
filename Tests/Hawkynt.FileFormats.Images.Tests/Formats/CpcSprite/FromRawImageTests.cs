using System;
using FileFormat.Core;

namespace FileFormat.CpcSprite.Tests;

[TestFixture]
public sealed class CpcSpriteFileFromRawImageTests {

  /// <summary>The four colours Mode 1 shows, which is the whole of what a sprite can say.</summary>
  private static RawImage _Source(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var slot = (x / 4 + y / 4) % 4;
      var colour = slot switch { 0 => 0x000000, 1 => 0x0000FF, 2 => 0xFF0000, _ => 0xFFFF00 };
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
    var source = _Source(16, 16);
    var decoded = CpcSpriteFile.ToRawImage(CpcSpriteFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(16));
      Assert.That(decoded.Height, Is.EqualTo(16));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    // The screen is one size and callers have whatever they have; refusing them would make encoding
    // useful only to those who already knew the size.
    var decoded = CpcSpriteFile.ToRawImage(CpcSpriteFile.FromRawImage(_Source(96, 72)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(16));
      Assert.That(decoded.Height, Is.EqualTo(16));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => CpcSpriteFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = CpcSpriteFile.FromRawImage(_Source(16, 16));
    var restored = CpcSpriteReader.FromBytes(CpcSpriteWriter.ToBytes(file));

    Assert.That(_Rgb(CpcSpriteFile.ToRawImage(restored)), Is.EqualTo(_Rgb(CpcSpriteFile.ToRawImage(file))));
  }
}
