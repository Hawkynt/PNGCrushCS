using System;
using FileFormat.Core;

namespace FileFormat.GoDot4Bit.Tests;

[TestFixture]
public sealed class GoDot4BitFileFromRawImageTests {

  /// <summary>Every machine colour in turn, which four bits a pixel holds with nothing else to obey.</summary>
  private static RawImage _Source(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var colour = Commodore64Graphics.HexColors[(x / 8 + y / 8 * 5) % Commodore64Graphics.ColorCount];
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
    var source = _Source(320, 200);
    var decoded = GoDot4BitFile.ToRawImage(GoDot4BitFile.FromRawImage(source));

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
    var decoded = GoDot4BitFile.ToRawImage(GoDot4BitFile.FromRawImage(_Source(96, 72)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(320));
      Assert.That(decoded.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => GoDot4BitFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = GoDot4BitFile.FromRawImage(_Source(320, 200));
    var restored = GoDot4BitReader.FromBytes(GoDot4BitWriter.ToBytes(file));

    Assert.That(_Rgb(GoDot4BitFile.ToRawImage(restored)), Is.EqualTo(_Rgb(GoDot4BitFile.ToRawImage(file))));
  }
}
