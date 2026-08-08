using System;
using FileFormat.Core;

namespace FileFormat.InterlaceStudio.Tests;

[TestFixture]
public sealed class InterlaceStudioFileFromRawImageTests {

  /// <summary>The seven levels two four-level frames average to, spaced half a frame's own step apart.</summary>
  private static RawImage _Source(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var level = (x / 4 + y / 8) % 7 * 34;
      var colour = (level << 16) | (level << 8) | level;
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
    var decoded = InterlaceStudioFile.ToRawImage(InterlaceStudioFile.FromRawImage(source));

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
    var decoded = InterlaceStudioFile.ToRawImage(InterlaceStudioFile.FromRawImage(_Source(96, 72)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(160));
      Assert.That(decoded.Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => InterlaceStudioFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = InterlaceStudioFile.FromRawImage(_Source(160, 200));
    var restored = InterlaceStudioReader.FromBytes(InterlaceStudioWriter.ToBytes(file));

    Assert.That(_Rgb(InterlaceStudioFile.ToRawImage(restored)), Is.EqualTo(_Rgb(InterlaceStudioFile.ToRawImage(file))));
  }
}
