using System;
using System.IO;
using FileFormat.Xyz;

namespace FileFormat.Xyz.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_1x1_SinglePixel() {
    var original = new XyzFile {
      Width = 1,
      Height = 1,
      Palette = _CreateGrayscalePalette(),
      PixelData = [42],
    };

    var bytes = XyzWriter.ToBytes(original);
    var restored = XyzReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_320x240_IndexedImage() {
    var palette = _CreateGrayscalePalette();
    var pixelData = new byte[320 * 240];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new XyzFile {
      Width = 320,
      Height = 240,
      Palette = palette,
      PixelData = pixelData,
    };

    var bytes = XyzWriter.ToBytes(original);
    var restored = XyzReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(240));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SpecificPaletteColors() {
    var palette = new byte[768];
    // Entry 0 = red
    palette[0] = 255; palette[1] = 0; palette[2] = 0;
    // Entry 1 = green
    palette[3] = 0; palette[4] = 255; palette[5] = 0;
    // Entry 2 = blue
    palette[6] = 0; palette[7] = 0; palette[8] = 255;
    // Entry 255 = white
    palette[765] = 255; palette[766] = 255; palette[767] = 255;

    var original = new XyzFile {
      Width = 2,
      Height = 2,
      Palette = palette,
      PixelData = [0, 1, 2, 255],
    };

    var bytes = XyzWriter.ToBytes(original);
    var restored = XyzReader.FromBytes(bytes);

    Assert.That(restored.Palette[0], Is.EqualTo(255));
    Assert.That(restored.Palette[1], Is.EqualTo(0));
    Assert.That(restored.Palette[2], Is.EqualTo(0));
    Assert.That(restored.Palette[3], Is.EqualTo(0));
    Assert.That(restored.Palette[4], Is.EqualTo(255));
    Assert.That(restored.Palette[5], Is.EqualTo(0));
    Assert.That(restored.Palette[6], Is.EqualTo(0));
    Assert.That(restored.Palette[7], Is.EqualTo(0));
    Assert.That(restored.Palette[8], Is.EqualTo(255));
    Assert.That(restored.Palette[765], Is.EqualTo(255));
    Assert.That(restored.Palette[766], Is.EqualTo(255));
    Assert.That(restored.Palette[767], Is.EqualTo(255));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var palette = _CreateGrayscalePalette();
    var pixelData = new byte[16 * 16];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new XyzFile {
      Width = 16,
      Height = 16,
      Palette = palette,
      PixelData = pixelData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xyz");
    try {
      var bytes = XyzWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = XyzReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaStream() {
    var original = new XyzFile {
      Width = 4,
      Height = 4,
      Palette = _CreateGrayscalePalette(),
      PixelData = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15],
    };

    var bytes = XyzWriter.ToBytes(original);
    using var stream = new MemoryStream(bytes);
    var restored = XyzReader.FromStream(stream);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllPixelValuesUsed() {
    var palette = _CreateGrayscalePalette();
    // 256 pixels using all palette indices exactly once
    var pixelData = new byte[256];
    for (var i = 0; i < 256; ++i)
      pixelData[i] = (byte)i;

    var original = new XyzFile {
      Width = 16,
      Height = 16,
      Palette = palette,
      PixelData = pixelData,
    };

    var bytes = XyzWriter.ToBytes(original);
    var restored = XyzReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(16));
    Assert.That(restored.Height, Is.EqualTo(16));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  private static byte[] _CreateGrayscalePalette() {
    var palette = new byte[768];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)i;
      palette[i * 3 + 2] = (byte)i;
    }

    return palette;
  }
}
