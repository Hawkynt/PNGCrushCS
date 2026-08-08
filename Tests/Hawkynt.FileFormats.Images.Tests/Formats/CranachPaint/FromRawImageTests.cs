using System;
using FileFormat.Core;

namespace FileFormat.CranachPaint.Tests;

[TestFixture]
public sealed class CranachPaintFileFromRawImageTests {

  /// <summary>A picture whose width is not a multiple of eight, so a stride mistake shows.</summary>
  private static RawImage _Source(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      rgb[at] = (byte)(x * 7 + y);
      rgb[at + 1] = (byte)(x + y * 5);
      rgb[at + 2] = (byte)(x * y + 3);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesEveryPixel() {
    var source = _Source(37, 11);
    var decoded = CranachPaintFile.ToRawImage(CranachPaintFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(37));
      Assert.That(decoded.Height, Is.EqualTo(11));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureOfAnySizeIsStoredAtThatSize() {
    // The format states its own dimensions, so unlike a screen format there is nothing to scale to.
    var decoded = CranachPaintFile.ToRawImage(CranachPaintFile.FromRawImage(_Source(96, 72)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(96));
      Assert.That(decoded.Height, Is.EqualTo(72));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => CranachPaintFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void ThePaletteIsWrittenEvenThoughATrueColourPictureNeverReadsIt() {
    // Three planes of 256 bytes sit ahead of every picture whatever its depth, and leaving them zero
    // would make three quarters of a kilobyte read as a picture of black.
    var bytes = CranachPaintWriter.ToBytes(CranachPaintFile.FromRawImage(_Source(37, 11)));

    Assert.Multiple(() => {
      Assert.That(bytes.Length, Is.EqualTo(CranachPaintFile.PixelsOffset + 37 * 11 * 3));
      Assert.That(bytes[11], Is.EqualTo(24));
      Assert.That(bytes[CranachPaintFile.PaletteOffset + 200], Is.EqualTo(200));
      Assert.That(bytes[CranachPaintFile.PaletteOffset + CranachPaintFile.ColorCount + 200], Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheSizeIsStatedBigEndian() {
    // The two sixteen-bit fields are the one place a byte order mistake would still produce a file
    // that reads, so it is worth pinning rather than inferring from the round trip.
    var bytes = CranachPaintWriter.ToBytes(CranachPaintFile.FromRawImage(_Source(300, 260)));

    Assert.Multiple(() => {
      Assert.That(bytes[6], Is.EqualTo(1));
      Assert.That(bytes[7], Is.EqualTo(44));
      Assert.That(bytes[8], Is.EqualTo(1));
      Assert.That(bytes[9], Is.EqualTo(4));
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = CranachPaintFile.FromRawImage(_Source(37, 11));
    var restored = CranachPaintReader.FromBytes(CranachPaintWriter.ToBytes(file));

    Assert.That(_Rgb(CranachPaintFile.ToRawImage(restored)), Is.EqualTo(_Rgb(CranachPaintFile.ToRawImage(file))));
  }
}
