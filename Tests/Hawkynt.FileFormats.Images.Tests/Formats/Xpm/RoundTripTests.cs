using System;
using FileFormat.Xpm;

namespace FileFormat.Xpm.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Simple2Color() {
    var original = new XpmFile {
      Width = 4,
      Height = 4,
      CharsPerPixel = 1,
      Name = "simple",
      Palette = [0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00],
      PaletteColorCount = 2,
      PixelData = [0, 1, 0, 1, 1, 0, 1, 0, 0, 1, 0, 1, 1, 0, 1, 0]
    };

    var bytes = XpmWriter.ToBytes(original);
    var restored = XpmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PaletteColorCount, Is.EqualTo(original.PaletteColorCount));
    Assert.That(restored.CharsPerPixel, Is.EqualTo(original.CharsPerPixel));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithTransparency() {
    var original = new XpmFile {
      Width = 2,
      Height = 2,
      CharsPerPixel = 1,
      Name = "transparent",
      Palette = [0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF],
      PaletteColorCount = 2,
      TransparentIndex = 0,
      PixelData = [0, 1, 1, 0]
    };

    var bytes = XpmWriter.ToBytes(original);
    var restored = XpmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.TransparentIndex, Is.EqualTo(0));
    Assert.That(restored.Palette[3], Is.EqualTo(0xFF), "R of opaque color");
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleColors() {
    var palette = new byte[4 * 3];
    palette[0] = 0xFF; palette[1] = 0x00; palette[2] = 0x00; // Red
    palette[3] = 0x00; palette[4] = 0xFF; palette[5] = 0x00; // Green
    palette[6] = 0x00; palette[7] = 0x00; palette[8] = 0xFF; // Blue
    palette[9] = 0xFF; palette[10] = 0xFF; palette[11] = 0x00; // Yellow

    var original = new XpmFile {
      Width = 2,
      Height = 2,
      CharsPerPixel = 1,
      Name = "multi",
      Palette = palette,
      PaletteColorCount = 4,
      PixelData = [0, 1, 2, 3]
    };

    var bytes = XpmWriter.ToBytes(original);
    var restored = XpmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.PaletteColorCount, Is.EqualTo(4));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CharsPerPixel2() {
    var original = new XpmFile {
      Width = 3,
      Height = 2,
      CharsPerPixel = 2,
      Name = "cpp2",
      Palette = [0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF],
      PaletteColorCount = 3,
      PixelData = [0, 1, 2, 2, 1, 0]
    };

    var bytes = XpmWriter.ToBytes(original);
    var restored = XpmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(3));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.CharsPerPixel, Is.EqualTo(2));
    Assert.That(restored.PaletteColorCount, Is.EqualTo(3));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    var width = 16;
    var height = 16;
    var numColors = 8;
    var palette = new byte[numColors * 3];
    for (var i = 0; i < numColors; ++i) {
      palette[i * 3] = (byte)(i * 32);
      palette[i * 3 + 1] = (byte)(255 - i * 32);
      palette[i * 3 + 2] = (byte)(i * 16);
    }

    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % numColors);

    var original = new XpmFile {
      Width = width,
      Height = height,
      CharsPerPixel = 1,
      Name = "large",
      Palette = palette,
      PaletteColorCount = numColors,
      PixelData = pixelData
    };

    var bytes = XpmWriter.ToBytes(original);
    var restored = XpmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PaletteColorCount, Is.EqualTo(numColors));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PreservesName() {
    var original = new XpmFile {
      Width = 1,
      Height = 1,
      CharsPerPixel = 1,
      Name = "my_custom_icon",
      Palette = [0x00, 0x00, 0x00],
      PaletteColorCount = 1,
      PixelData = [0]
    };

    var bytes = XpmWriter.ToBytes(original);
    var restored = XpmReader.FromBytes(bytes);

    Assert.That(restored.Name, Is.EqualTo("my_custom_icon"));
  }
}
