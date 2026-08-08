using System;
using FileFormat.Core;

namespace FileFormat.HiresFliCrest.Tests;

[TestFixture]
public sealed class HiresFliCrestFromRawImageTests {

  /// <summary>
  /// Alternating columns of black and one other machine colour, the second changing every character
  /// cell.
  /// </summary>
  /// <remarks>
  /// Two colours to a cell and to every raster line of one, which is inside what a hires FLI screen can hold,
  /// so a round trip through it has to come back byte for byte. Half the picture being black also
  /// settles the shared background register on black wherever the format has one to choose.
  /// </remarks>
  private static RawImage _Stripes(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var colour = x % 2 == 0
        ? 0
        : Commodore64Graphics.HexColors[(x / 8 + y / 8 * 3) % Commodore64Graphics.ColorCount];

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
    var source = _Stripes(320, 200);
    var decoded = HiresFliCrestFile.ToRawImage(HiresFliCrestFile.FromRawImage(source));

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
    var decoded = HiresFliCrestFile.ToRawImage(HiresFliCrestFile.FromRawImage(_Stripes(96, 72)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => HiresFliCrestFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = HiresFliCrestFile.FromRawImage(_Stripes(320, 200));
    var restored = HiresFliCrestReader.FromBytes(HiresFliCrestWriter.ToBytes(file));

    Assert.That(_Rgb(HiresFliCrestFile.ToRawImage(restored)), Is.EqualTo(_Rgb(HiresFliCrestFile.ToRawImage(file))));
  }
}
