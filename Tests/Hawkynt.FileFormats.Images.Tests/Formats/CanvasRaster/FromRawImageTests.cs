using System;
using FileFormat.Core;

namespace FileFormat.CanvasRaster.Tests;

[TestFixture]
public sealed class CanvasRasterFileFromRawImageTests {

  /// <summary>
  /// Sixteen colours a band, on the three-bit grid the ST stores them in, and different colours in
  /// every band so that a palette written in the wrong order shows as the wrong band's colours.
  /// </summary>
  private static RawImage _Source(int width, int height) {
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var band = y / CanvasRasterFile.BandHeight;
      var index = (x / 20) & 15;
      var at = (y * width + x) * 3;
      rgb[at] = ChannelScaling.Expand3(index & 7);
      rgb[at + 1] = ChannelScaling.Expand3(band & 7);
      rgb[at + 2] = ChannelScaling.Expand3(index >= 8 ? 7 : 0);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
  }

  private static byte[] _Rgb(RawImage image) => PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;

  [Test]
  [Category("Unit")]
  public void EncodeThenDecode_ReproducesAPictureTheFormatCanHold() {
    var source = _Source(CanvasRasterFile.Width, CanvasRasterFile.Height);
    var decoded = CanvasRasterFile.ToRawImage(CanvasRasterFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(CanvasRasterFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(CanvasRasterFile.Height));
      Assert.That(_Rgb(decoded), Is.EqualTo(_Rgb(source)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADifferentlySizedPictureIsScaledRatherThanRefused() {
    var decoded = CanvasRasterFile.ToRawImage(CanvasRasterFile.FromRawImage(_Source(101, 77)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(CanvasRasterFile.Width));
      Assert.That(decoded.Height, Is.EqualTo(CanvasRasterFile.Height));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => CanvasRasterFile.FromRawImage(null!));

  [Test]
  [Category("Unit")]
  public void EveryBandCarriesAPaletteAndTheFirstBandsIsWrittenLast() {
    // The palettes run backwards from a fixed end, so the first band's is the one nearest the
    // picture; writing them forwards would give every band the colours of its opposite.
    var bytes = CanvasRasterWriter.ToBytes(
      CanvasRasterFile.FromRawImage(_Source(CanvasRasterFile.Width, CanvasRasterFile.Height)));

    var cursor = CanvasRasterFile.PaletteEnd + CanvasRasterFile.PaletteSize * (CanvasRasterFile.BandCount - 1);

    Assert.Multiple(() => {
      for (var band = 0; band < CanvasRasterFile.BandCount; ++band)
        Assert.That(bytes[band * 2] != 255 || bytes[band * 2 + 1] != 255, Is.True, $"band {band} has no palette");

      // Every colour of band b has green level b, so which band a palette belongs to is readable
      // from the palette alone.
      for (var band = 0; band < 8; ++band) {
        var at = cursor - (band + 1) * CanvasRasterFile.PaletteSize;
        for (var slot = 0; slot < CanvasRasterFile.ColorCount; ++slot)
          Assert.That(bytes[at + slot * 3 + 1], Is.EqualTo(band), $"band {band} slot {slot}");
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatIsEncodedSurvivesTheWriterAndTheReader() {
    var file = CanvasRasterFile.FromRawImage(_Source(CanvasRasterFile.Width, CanvasRasterFile.Height));
    var restored = CanvasRasterReader.FromBytes(CanvasRasterWriter.ToBytes(file));

    Assert.That(_Rgb(CanvasRasterFile.ToRawImage(restored)), Is.EqualTo(_Rgb(CanvasRasterFile.ToRawImage(file))));
  }
}
