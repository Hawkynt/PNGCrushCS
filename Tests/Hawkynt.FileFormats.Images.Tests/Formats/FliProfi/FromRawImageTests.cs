using System;
using FileFormat.Core;

namespace FileFormat.FliProfi.Tests;

[TestFixture]
public sealed class FliProfiFromRawImageTests {

  /// <summary>
  /// Alternating multicolour pixels of black and one other machine colour, with the leftmost three
  /// cells black.
  /// </summary>
  /// <remarks>
  /// Those three cells are drawn by sprites and the encoder leaves the sprites blank, so a picture
  /// that expects to survive a round trip has to be black there — which is what the format shows for
  /// an empty border.
  /// </remarks>
  private static RawImage _Stripes(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var colour = x < Commodore64Fli.HiddenColumns || x / 2 % 2 == 0
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
    var decoded = FliProfiFile.ToRawImage(FliProfiFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(200));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    var decoded = FliProfiFile.ToRawImage(FliProfiFile.FromRawImage(_Stripes(96, 72)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FliProfiFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = FliProfiFile.FromRawImage(_Stripes(320, 200));
    var restored = FliProfiReader.FromBytes(FliProfiWriter.ToBytes(file));

    Assert.That(_Rgb(FliProfiFile.ToRawImage(restored)), Is.EqualTo(_Rgb(FliProfiFile.ToRawImage(file))));
  }
}
