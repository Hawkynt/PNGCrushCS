using System;
using FileFormat.Core;

namespace FileFormat.FliGraph.Tests;

[TestFixture]
public sealed class FliGraphFromRawImageTests {

  /// <summary>
  /// Alternating pairs of columns, black against one other machine colour, the second changing every
  /// character cell.
  /// </summary>
  /// <remarks>
  /// Pairs rather than single columns because this is multicolour: a stored pixel is drawn twice, so
  /// a picture with neighbouring columns that differ is one the format cannot hold whatever the
  /// colours. Two colours to a raster line of a cell is inside what one video matrix entry says, and
  /// black is pattern 00, which the format has no register to change.
  /// </remarks>
  private static RawImage _Source(int width, int height) {
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
    var source = _Source(FliGraphFile.VisibleWidth, FliGraphFile.FixedHeight);
    var decoded = FliGraphFile.ToRawImage(FliGraphFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(FliGraphFile.VisibleWidth));
      Assert.That(decoded.Height, Is.EqualTo(FliGraphFile.FixedHeight));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ThePictureIsNarrowerThanTheScreenItLivesIn() {
    // The raster switch happens in the border and is not ready before the first three character
    // cells are drawn, so those 24 pixels are not part of the picture at either end of the trip.
    Assert.Multiple(() => {
      Assert.That(FliGraphFile.VisibleWidth, Is.EqualTo(296));
      Assert.That(FliGraphFile.HiddenStoredPixels, Is.EqualTo(12));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    var decoded = FliGraphFile.ToRawImage(FliGraphFile.FromRawImage(_Source(96, 72)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(FliGraphFile.VisibleWidth));
      Assert.That(decoded.Height, Is.EqualTo(FliGraphFile.FixedHeight));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FliGraphFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = FliGraphFile.FromRawImage(_Source(FliGraphFile.VisibleWidth, FliGraphFile.FixedHeight));
    var restored = FliGraphReader.FromBytes(FliGraphWriter.ToBytes(file));

    Assert.That(_Rgb(FliGraphFile.ToRawImage(restored)), Is.EqualTo(_Rgb(FliGraphFile.ToRawImage(file))));
  }
}
