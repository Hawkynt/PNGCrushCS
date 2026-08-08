using System;
using FileFormat.Core;

namespace FileFormat.AnimPainter.Tests;

[TestFixture]
public sealed class AnimPainterFileFromRawImageTests {

  /// <summary>Alternating columns of black and one other machine colour, changing every cell, which one multicolour frame holds exactly.</summary>
  private static RawImage _Source(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var colour = Commodore64Graphics.HexColors[x % 2 == 0 ? 0 : (x / 4 + y / 8 * 3) % Commodore64Graphics.ColorCount];
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
    var source = _Source(160, 200);
    var decoded = AnimPainterFile.ToRawImage(AnimPainterFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(160));
      Assert.That(decoded.Height, Is.EqualTo(200));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    // The screen is one size and callers have whatever they have; refusing them would make encoding
    // useful only to those who already knew the size.
    var decoded = AnimPainterFile.ToRawImage(AnimPainterFile.FromRawImage(_Source(96, 72)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(160));
      Assert.That(decoded.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => AnimPainterFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = AnimPainterFile.FromRawImage(_Source(160, 200));
    var restored = AnimPainterReader.FromBytes(AnimPainterWriter.ToBytes(file));

    Assert.That(_Rgb(AnimPainterFile.ToRawImage(restored)), Is.EqualTo(_Rgb(AnimPainterFile.ToRawImage(file))));
  }
}
