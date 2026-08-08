using System;
using FileFormat.Core;

namespace FileFormat.ColorStarObject.Tests;

[TestFixture]
public sealed class ColorStarObjectFileFromRawImageTests {

  /// <summary>
  /// Sixteen colours on the three-bit grid the file states them in, at a width that is not a
  /// multiple of eight so a padding mistake in the bitplanes shows.
  /// </summary>
  private static RawImage _Source(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var index = (x / 3 + y) & 15;
      var at = (y * width + x) * 3;
      rgb[at] = ChannelScaling.Expand3(index & 7);
      rgb[at + 1] = ChannelScaling.Expand3((index >> 1) & 7);
      rgb[at + 2] = ChannelScaling.Expand3(index >= 8 ? 7 : 0);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesAPictureTheFormatCanHold() {
    var source = _Source(37, 11);
    var decoded = ColorStarObjectFile.ToRawImage(ColorStarObjectFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(37));
      Assert.That(decoded.Height, Is.EqualTo(11));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureTallerThanTheHeaderCanSayIsScaledRatherThanRefused() {
    // The height is one byte, so 256 rows is all an object can state; a taller picture is brought to
    // that rather than turned away, because a clipping has no size of its own to betray.
    var decoded = ColorStarObjectFile.ToRawImage(ColorStarObjectFile.FromRawImage(_Source(60, 400)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(60));
      Assert.That(decoded.Height, Is.EqualTo(ColorStarObjectFile.MaxHeight));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => ColorStarObjectFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void ThePaletteIsDecimalTextAndTheSizeIsOneLessThanItIs() {
    // Sixteen numbers on lines of their own, then a header whose dimensions are stored one short so
    // that a one-pixel object is not an empty one.
    var bytes = ColorStarObjectWriter.ToBytes(ColorStarObjectFile.FromRawImage(_Source(37, 11)));
    var at = 0;
    for (var line = 0; line < ColorStarObjectFile.ColorCount; ++line) {
      while (bytes[at] is >= (byte)'0' and <= (byte)'9')
        ++at;

      Assert.That(bytes[at], Is.EqualTo((byte)'\r'));
      Assert.That(bytes[at + 1], Is.EqualTo((byte)'\n'));
      at += 2;
    }

    Assert.Multiple(() => {
      Assert.That((bytes[at] << 8) + bytes[at + 1], Is.EqualTo(36));
      Assert.That(bytes[at + 3], Is.EqualTo(10));
      Assert.That(bytes[at + 5], Is.EqualTo(4));
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = ColorStarObjectFile.FromRawImage(_Source(37, 11));
    var restored = ColorStarObjectReader.FromBytes(ColorStarObjectWriter.ToBytes(file));

    Assert.That(
      _Rgb(ColorStarObjectFile.ToRawImage(restored)), Is.EqualTo(_Rgb(ColorStarObjectFile.ToRawImage(file))));
  }
}
