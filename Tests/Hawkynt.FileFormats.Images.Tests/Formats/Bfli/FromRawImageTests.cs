using System;
using FileFormat.Core;

namespace FileFormat.Bfli.Tests;

[TestFixture]
public sealed class BfliFromRawImageTests {

  /// <summary>
  /// Alternating pairs of columns, black and one other machine colour, the second changing every
  /// character cell.
  /// </summary>
  /// <remarks>
  /// The mode is multicolour, so a stored pixel is two wide and a picture that changes colour every
  /// column cannot be held whatever else is true of it. Two colours to a raster line of a cell is
  /// inside what the format holds — black from the background register and the other from a nibble
  /// of the matrix entry — so a round trip through it has to come back exactly, and it does not
  /// depend on colour memory, which the two halves of the picture share and neither owns.
  /// </remarks>
  private static RawImage _Stripes(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cell = y / 8 * 40 + x / 8;
      var colour = x / 2 % 2 == 0
        ? 0
        : Commodore64Graphics.HexColors[1 + cell % (Commodore64Graphics.ColorCount - 1)];

      var at = (y * width + x) * 3;
      rgb[at] = (byte)(colour >> 16);
      rgb[at + 1] = (byte)(colour >> 8);
      rgb[at + 2] = (byte)colour;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  /// <summary>
  /// The picture from column 24 rightwards, which is all of it the format can carry: the raster
  /// switch has not happened when the first three cells of a row are drawn.
  /// </summary>
  private static byte[] _Visible(RawImage image) {
    var rgb = _Rgb(image);
    var hidden = BfliFile.HiddenPairs * 2;
    var width = image.Width - hidden;
    var output = new byte[width * image.Height * 3];
    for (var y = 0; y < image.Height; ++y)
      Array.Copy(rgb, (y * image.Width + hidden) * 3, output, y * width * 3, width * 3);

    return output;
  }

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesAPictureTheFormatCanHold() {
    var source = _Stripes(320, 400);
    var decoded = BfliFile.ToRawImage(BfliFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(400));
      Assert.That(_Visible(decoded), Is.EqualTo(_Visible(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    // The screen is one size and callers have whatever they have; refusing them would make encoding
    // useful only to those who already knew the size.
    var decoded = BfliFile.ToRawImage(BfliFile.FromRawImage(_Stripes(96, 72)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(400));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => BfliFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = BfliFile.FromRawImage(_Stripes(320, 400));
    var restored = BfliReader.FromBytes(BfliWriter.ToBytes(file));

    Assert.Multiple(() => {
      Assert.That(restored.RawData, Is.EqualTo(file.RawData));
      Assert.That(_Rgb(BfliFile.ToRawImage(restored)), Is.EqualTo(_Rgb(BfliFile.ToRawImage(file))));
    });
  }
}
