using System;
using System.IO;
using FileFormat.MobyDick;

namespace FileFormat.MobyDick.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var palette = new byte[768];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (byte)(i * 7 % 256);

    var pixelData = new byte[64000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 3 % 256);

    var original = new MobyDickFile {
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = MobyDickWriter.ToBytes(original);
    var restored = MobyDickReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new MobyDickFile {
      Palette = new byte[768],
      PixelData = new byte[64000]
    };

    var bytes = MobyDickWriter.ToBytes(original);
    var restored = MobyDickReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(200));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllMaxValues() {
    var palette = new byte[768];
    Array.Fill(palette, (byte)0xFF);

    var pixelData = new byte[64000];
    Array.Fill(pixelData, (byte)0xFF);

    var original = new MobyDickFile {
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = MobyDickWriter.ToBytes(original);
    var restored = MobyDickReader.FromBytes(bytes);

    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var palette = new byte[768];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (byte)(i * 13 % 256);

    var pixelData = new byte[64000];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new MobyDickFile {
      Palette = palette,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mby");
    try {
      var bytes = MobyDickWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = MobyDickReader.FromFile(new FileInfo(tempPath));

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
  public void RoundTrip_DimensionsAlwaysFixed() {
    var original = new MobyDickFile {
      Palette = new byte[768],
      PixelData = new byte[64000]
    };

    var bytes = MobyDickWriter.ToBytes(original);
    var restored = MobyDickReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PaletteRGBTriples_Preserved() {
    var palette = new byte[768];
    // Color 0: red
    palette[0] = 255; palette[1] = 0; palette[2] = 0;
    // Color 1: green
    palette[3] = 0; palette[4] = 255; palette[5] = 0;
    // Color 255: blue
    palette[765] = 0; palette[766] = 0; palette[767] = 255;

    var pixelData = new byte[64000];
    pixelData[0] = 0;   // first pixel uses color 0 (red)
    pixelData[1] = 1;   // second pixel uses color 1 (green)
    pixelData[63999] = 255; // last pixel uses color 255 (blue)

    var original = new MobyDickFile {
      Palette = palette,
      PixelData = pixelData
    };

    var bytes = MobyDickWriter.ToBytes(original);
    var restored = MobyDickReader.FromBytes(bytes);

    Assert.That(restored.Palette[0], Is.EqualTo(255));
    Assert.That(restored.Palette[1], Is.EqualTo(0));
    Assert.That(restored.Palette[2], Is.EqualTo(0));
    Assert.That(restored.Palette[765], Is.EqualTo(0));
    Assert.That(restored.Palette[766], Is.EqualTo(0));
    Assert.That(restored.Palette[767], Is.EqualTo(255));
    Assert.That(restored.PixelData[0], Is.EqualTo(0));
    Assert.That(restored.PixelData[1], Is.EqualTo(1));
    Assert.That(restored.PixelData[63999], Is.EqualTo(255));
  }
}
