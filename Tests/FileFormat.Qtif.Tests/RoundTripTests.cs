using System;
using System.IO;
using FileFormat.Qtif;

namespace FileFormat.Qtif.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_1x1_Rgb24() {
    var original = new QtifFile {
      Width = 1,
      Height = 1,
      PixelData = [255, 128, 64],
    };

    var bytes = QtifWriter.ToBytes(original);
    var restored = QtifReader.FromBytes(bytes);

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

    var original = new QtifFile {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };

    var bytes = QtifWriter.ToBytes(original);
    var restored = QtifReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".qtif");
    try {
      var original = new QtifFile {
        Width = 3,
        Height = 2,
        PixelData = [
          255, 0, 0, 0, 255, 0, 0, 0, 255,
          128, 128, 128, 64, 64, 64, 32, 32, 32
        ],
      };

      var bytes = QtifWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = QtifReader.FromFile(new FileInfo(tempPath));

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
    var original = new QtifFile {
      Width = 2,
      Height = 2,
      PixelData = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120],
    };

    var raw = QtifFile.ToRawImage(original);
    var restored = QtifFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new QtifFile {
      Width = 4,
      Height = 4,
      PixelData = new byte[4 * 4 * 3],
    };

    var bytes = QtifWriter.ToBytes(original);
    var restored = QtifReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
