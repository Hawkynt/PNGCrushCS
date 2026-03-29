using System;
using System.IO;
using FileFormat.Xcf;

namespace FileFormat.Xcf.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb() {
    var pixelData = new byte[2 * 2 * 4]; // 2x2 RGBA
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new XcfFile {
      Width = 2,
      Height = 2,
      ColorMode = XcfColorMode.Rgb,
      Version = 1,
      PixelData = pixelData
    };

    var bytes = XcfWriter.ToBytes(original);
    var restored = XcfReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ColorMode, Is.EqualTo(XcfColorMode.Rgb));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale() {
    var pixelData = new byte[4 * 3 * 2]; // 4x3 GrayA
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 31 % 256);

    var original = new XcfFile {
      Width = 4,
      Height = 3,
      ColorMode = XcfColorMode.Grayscale,
      Version = 1,
      PixelData = pixelData
    };

    var bytes = XcfWriter.ToBytes(original);
    var restored = XcfReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ColorMode, Is.EqualTo(XcfColorMode.Grayscale));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SmallImage() {
    var pixelData = new byte[1 * 1 * 4]; // 1x1 RGBA
    pixelData[0] = 255; // R
    pixelData[1] = 128; // G
    pixelData[2] = 64;  // B
    pixelData[3] = 255; // A

    var original = new XcfFile {
      Width = 1,
      Height = 1,
      ColorMode = XcfColorMode.Rgb,
      Version = 1,
      PixelData = pixelData
    };

    var bytes = XcfWriter.ToBytes(original);
    var restored = XcfReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[2 * 2 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new XcfFile {
      Width = 2,
      Height = 2,
      ColorMode = XcfColorMode.Rgb,
      Version = 1,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xcf");
    try {
      var bytes = XcfWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = XcfReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
