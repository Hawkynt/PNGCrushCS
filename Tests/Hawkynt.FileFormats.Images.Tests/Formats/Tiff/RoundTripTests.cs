using System;
using FileFormat.Tiff;

namespace FileFormat.Tiff.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7);

    var original = new TiffFile {
      Width = 4,
      Height = 4,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      PixelData = pixelData,
      ColorMode = TiffColorMode.Rgb
    };

    var bytes = TiffWriter.ToBytes(original);
    var restored = TiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.SamplesPerPixel, Is.EqualTo(3));
    Assert.That(restored.BitsPerSample, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale() {
    var pixelData = new byte[4 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 16);

    var original = new TiffFile {
      Width = 4,
      Height = 4,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      PixelData = pixelData,
      ColorMode = TiffColorMode.Grayscale
    };

    var bytes = TiffWriter.ToBytes(original);
    var restored = TiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.SamplesPerPixel, Is.EqualTo(1));
    Assert.That(restored.BitsPerSample, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  /// <summary>
  /// <see cref="TiffFile.ColorMap"/> holds RGB triplets — that is what the reader puts there and what
  /// the writers expect. A pair of conversions used to sit on either side of it, turning triplets into
  /// the format's own channel-block layout on the way out and back on the way in, so a palette was
  /// laid out twice and read as though it had not been laid out at all. Every colour then came from
  /// somewhere else in the table, which the round trip could not see because both halves agreed.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void RoundTrip_Palette() {
    var colorMap = new byte[256 * 3];
    byte[] colours = [255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 0];
    colours.CopyTo(colorMap, 0);

    var pixelData = new byte[4 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 4);

    var original = new TiffFile {
      Width = 4,
      Height = 4,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      PixelData = pixelData,
      ColorMap = colorMap,
      ColorMode = TiffColorMode.Palette
    };

    var restored = TiffReader.FromBytes(TiffWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(4));
      Assert.That(restored.Height, Is.EqualTo(4));
      Assert.That(restored.SamplesPerPixel, Is.EqualTo(1));
      Assert.That(restored.BitsPerSample, Is.EqualTo(8));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(restored.ColorMap![..12], Is.EqualTo(colours), "the table comes back as it went in");
    });

    var rgb = TiffFile.ToRawImage(restored).ToRgb24();
    Assert.Multiple(() => {
      Assert.That(rgb[..3], Is.EqualTo(new byte[] { 255, 0, 0 }), "entry 0");
      Assert.That(rgb[3..6], Is.EqualTo(new byte[] { 0, 255, 0 }), "entry 1");
      Assert.That(rgb[6..9], Is.EqualTo(new byte[] { 0, 0, 255 }), "entry 2");
      Assert.That(rgb[9..12], Is.EqualTo(new byte[] { 255, 255, 0 }), "entry 3");
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PackBits() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13);

    var original = new TiffFile {
      Width = 4,
      Height = 4,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      PixelData = pixelData,
      ColorMode = TiffColorMode.Rgb
    };

    var bytes = TiffWriter.ToBytes(original, TiffCompression.PackBits);
    var restored = TiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Lzw() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 5);

    var original = new TiffFile {
      Width = 4,
      Height = 4,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      PixelData = pixelData,
      ColorMode = TiffColorMode.Rgb
    };

    var bytes = TiffWriter.ToBytes(original, TiffCompression.Lzw);
    var restored = TiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Tiled() {
    var pixelData = new byte[16 * 16 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 9);

    var original = new TiffFile {
      Width = 16,
      Height = 16,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      PixelData = pixelData,
      ColorMode = TiffColorMode.Rgb
    };

    var bytes = TiffWriter.ToBytes(original, tileWidth: 16, tileHeight: 16);
    var restored = TiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(16));
    Assert.That(restored.Height, Is.EqualTo(16));
    Assert.That(restored.SamplesPerPixel, Is.EqualTo(3));
    Assert.That(restored.BitsPerSample, Is.EqualTo(8));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
