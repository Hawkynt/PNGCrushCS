using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.FliDesigner2.Tests;

[TestFixture]
public sealed class FliDesigner2FileFromRawImageTests {

  /// <summary>
  /// Alternating multicolour pixels of black and one other machine colour, two colours to a raster
  /// line of a cell, which is what a multicolour FLI screen holds.
  /// </summary>
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
    var source = _Source(296, 200);
    var decoded = FliDesigner2File.ToRawImage(FliDesigner2File.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(296));
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
      Assert.That(decoded.Width, Is.EqualTo(296));
      Assert.That(decoded.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FliDesigner2File.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void ItIsWrittenAtTheLengthThatIdentifiesIt() {
    var bytes = FliDesigner2Writer.ToBytes(FliDesigner2File.FromRawImage(_Source(296, 200)));

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(17409));
      Assert.That(FliDesigner2File.MatricesOffset, Is.EqualTo(0x402));
      Assert.That(FliDesigner2File.BitmapOffset, Is.EqualTo(0x2402));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheOldFabricatedLength_IsNoLongerWhatItWants()
    => Assert.Throws<InvalidDataException>(() => FliDesigner2Reader.FromBytes(new byte[17216]));

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = FliDesigner2File.FromRawImage(_Source(296, 200));
    var restored = FliDesigner2Reader.FromBytes(FliDesigner2Writer.ToBytes(file));

    Assert.That(_Rgb(FliDesigner2File.ToRawImage(restored)), Is.EqualTo(_Rgb(FliDesigner2File.ToRawImage(file))));
  }
}
