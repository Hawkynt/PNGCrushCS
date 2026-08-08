using System;
using FileFormat.Core;

namespace FileFormat.DelmPaint.Tests;

[TestFixture]
public sealed class DelmPaintFileFromRawImageTests {

  /// <summary>A picture of sixteen colours, which a 256-entry palette holds without choosing.</summary>
  private static RawImage _Source(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var index = (x / 7 + y / 5) & 15;
      var at = (y * width + x) * 3;
      rgb[at] = (byte)(index * 17);
      rgb[at + 1] = (byte)(255 - index * 17);
      rgb[at + 2] = (byte)(index * 5 + 40);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesAPictureTheFormatCanHold() {
    var source = _Source(DelmPaintFile.QuadrantWidth, DelmPaintFile.QuadrantHeight);
    var decoded = DelmPaintFile.ToRawImage(DelmPaintFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(DelmPaintFile.QuadrantWidth));
      Assert.That(decoded.Height, Is.EqualTo(DelmPaintFile.QuadrantHeight));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    // A quadrant is one size, and a caller has whatever it has.
    var decoded = DelmPaintFile.ToRawImage(DelmPaintFile.FromRawImage(_Source(101, 77)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(DelmPaintFile.QuadrantWidth));
      Assert.That(decoded.Height, Is.EqualTo(DelmPaintFile.QuadrantHeight));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => DelmPaintFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void ThirdBlockIsTheRemainderAndHasNoLengthOfItsOwn() {
    // Two lengths are stored and three blocks are written; the third runs from where the second ends
    // to the end of the file, so anything appended past it would be read as picture.
    var bytes = DelmPaintWriter.ToBytes(
      DelmPaintFile.FromRawImage(_Source(DelmPaintFile.QuadrantWidth, DelmPaintFile.QuadrantHeight)));

    var first = (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
    var second = (bytes[4] << 24) | (bytes[5] << 16) | (bytes[6] << 8) | bytes[7];

    Assert.That(8 + first + second, Is.LessThan(bytes.Length));
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = DelmPaintFile.FromRawImage(_Source(DelmPaintFile.QuadrantWidth, DelmPaintFile.QuadrantHeight));
    var restored = DelmPaintReader.FromBytes(DelmPaintWriter.ToBytes(file));

    Assert.That(_Rgb(DelmPaintFile.ToRawImage(restored)), Is.EqualTo(_Rgb(DelmPaintFile.ToRawImage(file))));
  }
}
