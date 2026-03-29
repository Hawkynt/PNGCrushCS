using System;
using FileFormat.DrHalo;

namespace FileFormat.DrHalo.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_IndexedData() {
    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new DrHaloFile {
      Width = 4,
      Height = 3,
      PixelData = pixelData
    };

    var bytes = DrHaloWriter.ToBytes(original);
    var restored = DrHaloReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var pixelData = new byte[8 * 4];

    var original = new DrHaloFile {
      Width = 8,
      Height = 4,
      PixelData = pixelData
    };

    var bytes = DrHaloWriter.ToBytes(original);
    var restored = DrHaloReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllSameValue() {
    var pixelData = new byte[10 * 5];
    Array.Fill(pixelData, (byte)128);

    var original = new DrHaloFile {
      Width = 10,
      Height = 5,
      PixelData = pixelData
    };

    var bytes = DrHaloWriter.ToBytes(original);
    var restored = DrHaloReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SinglePixel() {
    var original = new DrHaloFile {
      Width = 1,
      Height = 1,
      PixelData = [42]
    };

    var bytes = DrHaloWriter.ToBytes(original);
    var restored = DrHaloReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(1));
    Assert.That(restored.Height, Is.EqualTo(1));
    Assert.That(restored.PixelData[0], Is.EqualTo(42));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    var pixelData = new byte[320 * 200];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new DrHaloFile {
      Width = 320,
      Height = 200,
      PixelData = pixelData
    };

    var bytes = DrHaloWriter.ToBytes(original);
    var restored = DrHaloReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(200));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithPalette_PalettePreservedOnFile() {
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(255 - i);
      palette[i * 3 + 2] = (byte)(i / 2);
    }

    var original = new DrHaloFile {
      Width = 4,
      Height = 2,
      PixelData = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 },
      Palette = palette
    };

    Assert.That(original.Palette, Is.Not.Null);
    Assert.That(original.Palette!.Length, Is.EqualTo(256 * 3));
  }
}
