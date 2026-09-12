using System;
using FileFormat.Core;

namespace FileFormat.Ffli.Tests;

[TestFixture]
public sealed class FfliFromRawImageTests {

  /// <summary>
  /// Alternating multicolour pixels of black and one other machine colour, the second changing every
  /// character cell.
  /// </summary>
  /// <remarks>
  /// A multicolour pixel is drawn two wide, so the pairs are the unit here. Two colours to every
  /// raster line of a cell is inside what a multicolour FLI screen holds, so a round trip through it
  /// has to come back byte for byte, and half the picture being black settles the background on
  /// black — which is the only thing this format can show for pattern 00.
  /// </remarks>
  internal static RawImage Stripes(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var colour = x / 2 % 2 == 0
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
    var source = Stripes(296, 200);
    var decoded = FfliFile.ToRawImage(FfliFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(296));
      Assert.That(decoded.Height, Is.EqualTo(200));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    var decoded = FfliFile.ToRawImage(FfliFile.FromRawImage(Stripes(96, 72)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(296));
      Assert.That(decoded.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FfliFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = FfliFile.FromRawImage(Stripes(296, 200));
    var restored = FfliReader.FromBytes(FfliWriter.ToBytes(file));

    Assert.That(_Rgb(FfliFile.ToRawImage(restored)), Is.EqualTo(_Rgb(FfliFile.ToRawImage(file))));
  }
}
