using System;
using System.IO;
using FileFormat.Pcd;

namespace FileFormat.Pcd.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_1x1() {
    var original = new PcdFile {
      Width = 1,
      Height = 1,
      PixelData = [42, 84, 126]
    };

    var bytes = PcdWriter.ToBytes(original);
    var restored = PcdReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_LargerImage() {
    var width = 64;
    var height = 32;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new PcdFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = PcdWriter.ToBytes(original);
    var restored = PcdReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DifferentSizes() {
    var sizes = new[] { (1, 1), (2, 3), (100, 50), (320, 240) };

    foreach (var (w, h) in sizes) {
      var pixelData = new byte[w * h * 3];
      for (var i = 0; i < pixelData.Length; ++i)
        pixelData[i] = (byte)(i * 13 % 256);

      var original = new PcdFile {
        Width = w,
        Height = h,
        PixelData = pixelData
      };

      var bytes = PcdWriter.ToBytes(original);
      var restored = PcdReader.FromBytes(bytes);

      Assert.That(restored.Width, Is.EqualTo(w), $"Width mismatch for {w}x{h}");
      Assert.That(restored.Height, Is.EqualTo(h), $"Height mismatch for {w}x{h}");
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData), $"Pixel data mismatch for {w}x{h}");
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pcd");
    try {
      var original = new PcdFile {
        Width = 3,
        Height = 2,
        PixelData = [
          255, 0, 0, 0, 255, 0, 0, 0, 255,
          128, 128, 128, 64, 64, 64, 32, 32, 32
        ]
      };

      var bytes = PcdWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = PcdReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var original = new PcdFile {
      Width = 2,
      Height = 2,
      PixelData = [
        255, 0, 0, 0, 255, 0,
        0, 0, 255, 128, 128, 128
      ]
    };

    var raw = PcdFile.ToRawImage(original);
    var restored = PcdFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
