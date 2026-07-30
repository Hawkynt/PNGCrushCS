using System;
using System.IO;
using FileFormat.Pict;

namespace FileFormat.Pict.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SmallRgb() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new PictFile {
      Width = width,
      Height = height,
      BitsPerPixel = 24,
      PixelData = pixelData
    };

    var bytes = PictWriter.ToBytes(original);
    var restored = PictReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.BitsPerPixel, Is.EqualTo(24));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargerImage() {
    var width = 32;
    var height = 16;
    var pixelData = new byte[width * height * 3];
    var rng = new Random(42);
    rng.NextBytes(pixelData);

    var original = new PictFile {
      Width = width,
      Height = height,
      BitsPerPixel = 24,
      PixelData = pixelData
    };

    var bytes = PictWriter.ToBytes(original);
    var restored = PictReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(width));
      Assert.That(restored.Height, Is.EqualTo(height));
      Assert.That(restored.BitsPerPixel, Is.EqualTo(24));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var width = 8;
    var height = 4;
    var pixelData = new byte[width * height * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new PictFile {
      Width = width,
      Height = height,
      BitsPerPixel = 24,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pict");
    try {
      var bytes = PictWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = PictReader.FromFile(new FileInfo(tempPath));

      Assert.Multiple(() => {
        Assert.That(restored.Width, Is.EqualTo(width));
        Assert.That(restored.Height, Is.EqualTo(height));
        Assert.That(restored.BitsPerPixel, Is.EqualTo(24));
        Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      });
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
