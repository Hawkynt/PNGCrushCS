using System;
using FileFormat.Tiff;

namespace FileFormat.Tiff.Tests;

[TestFixture]
public sealed class TiffWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Rgb_StartsWithTiffHeader() {
    var file = new TiffFile {
      Width = 2,
      Height = 2,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      PixelData = new byte[2 * 2 * 3],
      ColorMode = TiffColorMode.Rgb
    };

    var bytes = TiffWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThan(8));
    Assert.That(bytes[0], Is.EqualTo((byte)'I'));
    Assert.That(bytes[1], Is.EqualTo((byte)'I'));
    Assert.That(BitConverter.ToUInt16(bytes, 2), Is.EqualTo(42));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Palette_IncludesColorMap() {
    var colorMap = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      colorMap[i * 3] = (byte)i;
      colorMap[i * 3 + 1] = (byte)(255 - i);
      colorMap[i * 3 + 2] = (byte)(i / 2);
    }

    var file = new TiffFile {
      Width = 4,
      Height = 4,
      SamplesPerPixel = 1,
      BitsPerSample = 8,
      PixelData = new byte[4 * 4],
      ColorMap = colorMap,
      ColorMode = TiffColorMode.Palette
    };

    var bytes = TiffWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.GreaterThan(8));

    var restored = TiffReader.FromBytes(bytes);
    Assert.That(restored.ColorMap, Is.Not.Null);
    Assert.That(restored.ColorMap!.Length, Is.GreaterThan(0));
    Assert.That(restored.ColorMode, Is.EqualTo(TiffColorMode.Palette));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize_NonZero() {
    var file = new TiffFile {
      Width = 1,
      Height = 1,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      PixelData = new byte[3],
      ColorMode = TiffColorMode.Rgb
    };

    var bytes = TiffWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_Lzw_ProducesValidTiff() {
    var pixelData = new byte[8 * 8 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11);

    var file = new TiffFile {
      Width = 8,
      Height = 8,
      SamplesPerPixel = 3,
      BitsPerSample = 8,
      PixelData = pixelData,
      ColorMode = TiffColorMode.Rgb
    };

    var bytes = TiffWriter.ToBytes(file, TiffCompression.Lzw);
    var restored = TiffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(8));
    Assert.That(restored.SamplesPerPixel, Is.EqualTo(3));
    Assert.That(restored.BitsPerSample, Is.EqualTo(8));
  }
}
