using System;
using FileFormat.Core;

namespace FileFormat.LogoPainter.Tests;

[TestFixture]
public sealed class LogoPainterFileFromRawImageTests {

  /// <summary>Four machine colours in a handful of repeating cell patterns, well inside the 256 characters a set holds.</summary>
  private static RawImage _Source(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      // Stored four pixels to a character and each shown twice, so a colour spans two columns.
      var slot = (x / 2 % 4 + (x / 8 + y / 8) % 4) % 4;
      var colour = Commodore64Graphics.HexColors[slot switch { 0 => 0, 1 => 1, 2 => 2, _ => 5 }];
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
    var source = _Source(320, 400);
    var decoded = LogoPainterFile.ToRawImage(LogoPainterFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(400));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    // The screen is one size and callers have whatever they have; refusing them would make encoding
    // useful only to those who already knew the size.
    var decoded = LogoPainterFile.ToRawImage(LogoPainterFile.FromRawImage(_Source(96, 72)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(400));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => LogoPainterFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = LogoPainterFile.FromRawImage(_Source(320, 400));
    var restored = LogoPainterReader.FromBytes(LogoPainterWriter.ToBytes(file));

    Assert.That(_Rgb(LogoPainterFile.ToRawImage(restored)), Is.EqualTo(_Rgb(LogoPainterFile.ToRawImage(file))));
  }
}
