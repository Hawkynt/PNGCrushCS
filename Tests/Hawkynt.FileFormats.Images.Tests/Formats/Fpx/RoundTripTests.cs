using System;
using System.IO;
using FileFormat.Fpx;

namespace FileFormat.Fpx.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_1x1() {
    var original = new FpxFile {
      Width = 1,
      Height = 1,
      PixelData = [42, 84, 126]
    };

    var bytes = FpxWriter.ToBytes(original);
    var restored = FpxReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    var width = 64;
    var height = 32;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new FpxFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = FpxWriter.ToBytes(original);
    var restored = FpxReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fpx");
    try {
      var original = new FpxFile {
        Width = 3,
        Height = 2,
        PixelData = [
          255, 0, 0, 0, 255, 0, 0, 0, 255,
          128, 128, 128, 64, 64, 64, 32, 32, 32
        ]
      };

      var bytes = FpxWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = FpxReader.FromFile(new FileInfo(tempPath));

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
    var original = new FpxFile {
      Width = 2,
      Height = 2,
      PixelData = [
        255, 0, 0, 0, 255, 0,
        0, 0, 255, 128, 128, 128
      ]
    };

    var raw = FpxFile.ToRawImage(original);
    var restored = FpxFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_DifferentSizes() {
    int[] widths = [1, 7, 16, 100, 255];
    int[] heights = [1, 3, 16, 50, 200];

    for (var wi = 0; wi < widths.Length; ++wi)
    for (var hi = 0; hi < heights.Length; ++hi) {
      var w = widths[wi];
      var h = heights[hi];
      var pixelData = new byte[w * h * 3];
      for (var i = 0; i < pixelData.Length; ++i)
        pixelData[i] = (byte)((i + w + h) % 256);

      var original = new FpxFile {
        Width = w,
        Height = h,
        PixelData = pixelData
      };

      var bytes = FpxWriter.ToBytes(original);
      var restored = FpxReader.FromBytes(bytes);

      Assert.That(restored.Width, Is.EqualTo(w), $"Width mismatch for {w}x{h}");
      Assert.That(restored.Height, Is.EqualTo(h), $"Height mismatch for {w}x{h}");
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData), $"Pixel data mismatch for {w}x{h}");
    }
  }
}
