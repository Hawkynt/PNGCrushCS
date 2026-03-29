using System;
using System.IO;
using FileFormat.Fbm;

namespace FileFormat.Fbm.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grayscale() {
    var pixels = new byte[4 * 4];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 16);

    var original = new FbmFile {
      Width = 4,
      Height = 4,
      Bands = 1,
      PixelData = pixels,
      Title = "Gray test"
    };

    var bytes = FbmWriter.ToBytes(original);
    var restored = FbmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bands, Is.EqualTo(original.Bands));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Title, Is.EqualTo(original.Title));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb() {
    var pixels = new byte[3 * 2 * 3]; // 3x2 image, 3 bands
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 13 % 256);

    var original = new FbmFile {
      Width = 3,
      Height = 2,
      Bands = 3,
      PixelData = pixels,
      Title = "RGB test"
    };

    var bytes = FbmWriter.ToBytes(original);
    var restored = FbmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bands, Is.EqualTo(original.Bands));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Title, Is.EqualTo(original.Title));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fbm");
    try {
      var pixels = new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255 };
      var original = new FbmFile {
        Width = 3,
        Height = 1,
        Bands = 3,
        PixelData = pixels,
        Title = "File test"
      };

      var bytes = FbmWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = FbmReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Bands, Is.EqualTo(original.Bands));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
      Assert.That(restored.Title, Is.EqualTo(original.Title));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LargeImage() {
    var width = 100;
    var height = 80;
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 7 % 256);

    var original = new FbmFile {
      Width = width,
      Height = height,
      Bands = 3,
      PixelData = pixels
    };

    var bytes = FbmWriter.ToBytes(original);
    var restored = FbmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.Bands, Is.EqualTo(3));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NonAlignedWidth() {
    // 5 cols * 1 band = 5 bytes, rowlen = 16 (padded)
    var pixels = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
    var original = new FbmFile {
      Width = 5,
      Height = 2,
      Bands = 1,
      PixelData = pixels
    };

    var bytes = FbmWriter.ToBytes(original);
    var restored = FbmReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(5));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EmptyTitle() {
    var original = new FbmFile {
      Width = 1,
      Height = 1,
      Bands = 1,
      PixelData = [128],
      Title = ""
    };

    var bytes = FbmWriter.ToBytes(original);
    var restored = FbmReader.FromBytes(bytes);

    Assert.That(restored.Title, Is.EqualTo(string.Empty));
  }
}
