using System;
using FileFormat.Core;

namespace FileFormat.FliDesigner2.Tests;

[TestFixture]
public sealed class FliDesigner2FileFromRawImageTests {

  /// <summary>Alternating columns of black and one other machine colour, two to a raster line, which is what an FLI cell row can hold.</summary>
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
    var decoded = FliDesigner2File.ToRawImage(FliDesigner2File.FromRawImage(source));

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
    var decoded = FliDesigner2File.ToRawImage(FliDesigner2File.FromRawImage(_Source(96, 72)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(160));
      Assert.That(decoded.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FliDesigner2File.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = FliDesigner2File.FromRawImage(_Source(160, 200));
    var restored = FliDesigner2Reader.FromBytes(FliDesigner2Writer.ToBytes(file));

    Assert.That(_Rgb(FliDesigner2File.ToRawImage(restored)), Is.EqualTo(_Rgb(FliDesigner2File.ToRawImage(file))));
  }
}
