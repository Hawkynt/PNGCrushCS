using System;
using System.IO;
using FileFormat.PhotoPaint;

namespace FileFormat.PhotoPaint.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_1x1() {
    var original = new PhotoPaintFile {
      Width = 1,
      Height = 1,
      PixelData = [255, 128, 64],
    };

    var bytes = PhotoPaintWriter.ToBytes(original);
    var restored = PhotoPaintReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_SmallImage() {
    const int width = 4;
    const int height = 3;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new PhotoPaintFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var bytes = PhotoPaintWriter.ToBytes(original);
    var restored = PhotoPaintReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb24_LargeImage() {
    const int width = 64;
    const int height = 48;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new PhotoPaintFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var bytes = PhotoPaintWriter.ToBytes(original);
    var restored = PhotoPaintReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".cpt");
    try {
      var original = new PhotoPaintFile {
        Width = 3,
        Height = 2,
        PixelData = [
          255, 0, 0, 0, 255, 0, 0, 0, 255,
          128, 128, 128, 64, 64, 64, 32, 32, 32
        ],
      };

      var bytes = PhotoPaintWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = PhotoPaintReader.FromFile(new FileInfo(tempPath));

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
  public void RoundTrip_AllZeros() {
    var original = new PhotoPaintFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3],
    };

    var bytes = PhotoPaintWriter.ToBytes(original);
    var restored = PhotoPaintReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllMaxValues() {
    var pixelData = new byte[2 * 2 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = 0xFF;

    var original = new PhotoPaintFile {
      Width = 2,
      Height = 2,
      PixelData = pixelData,
    };

    var bytes = PhotoPaintWriter.ToBytes(original);
    var restored = PhotoPaintReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var original = new PhotoPaintFile {
      Width = 2,
      Height = 1,
      PixelData = [10, 20, 30, 40, 50, 60],
    };

    var raw = PhotoPaintFile.ToRawImage(original);
    var restored = PhotoPaintFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
