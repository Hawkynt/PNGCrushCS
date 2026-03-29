using System;
using System.IO;
using FileFormat.ScitexCt;

namespace FileFormat.ScitexCt.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Cmyk() {
    var width = 4;
    var height = 3;
    var channels = 4;
    var pixelData = new byte[width * height * channels];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11 % 256);

    var original = new ScitexCtFile {
      Width = width,
      Height = height,
      BitsPerComponent = 8,
      ColorMode = ScitexCtColorMode.Cmyk,
      HResolution = 300,
      VResolution = 300,
      Description = "Test CMYK",
      PixelData = pixelData
    };

    var bytes = ScitexCtWriter.ToBytes(original);
    var restored = ScitexCtReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ColorMode, Is.EqualTo(ScitexCtColorMode.Cmyk));
    Assert.That(restored.BitsPerComponent, Is.EqualTo(8));
    Assert.That(restored.HResolution, Is.EqualTo(300));
    Assert.That(restored.VResolution, Is.EqualTo(300));
    Assert.That(restored.Description, Is.EqualTo("Test CMYK"));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb() {
    var width = 8;
    var height = 4;
    var channels = 3;
    var pixelData = new byte[width * height * channels];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new ScitexCtFile {
      Width = width,
      Height = height,
      BitsPerComponent = 8,
      ColorMode = ScitexCtColorMode.Rgb,
      HResolution = 150,
      VResolution = 150,
      Description = "RGB image",
      PixelData = pixelData
    };

    var bytes = ScitexCtWriter.ToBytes(original);
    var restored = ScitexCtReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ColorMode, Is.EqualTo(ScitexCtColorMode.Rgb));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale() {
    var width = 16;
    var height = 8;
    var channels = 1;
    var pixelData = new byte[width * height * channels];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new ScitexCtFile {
      Width = width,
      Height = height,
      BitsPerComponent = 8,
      ColorMode = ScitexCtColorMode.Grayscale,
      HResolution = 72,
      VResolution = 72,
      Description = "Gray",
      PixelData = pixelData
    };

    var bytes = ScitexCtWriter.ToBytes(original);
    var restored = ScitexCtReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.ColorMode, Is.EqualTo(ScitexCtColorMode.Grayscale));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var width = 4;
    var height = 2;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new ScitexCtFile {
      Width = width,
      Height = height,
      BitsPerComponent = 8,
      ColorMode = ScitexCtColorMode.Rgb,
      HResolution = 96,
      VResolution = 96,
      Description = "File test",
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sct");
    try {
      var bytes = ScitexCtWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = ScitexCtReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.ColorMode, Is.EqualTo(original.ColorMode));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
