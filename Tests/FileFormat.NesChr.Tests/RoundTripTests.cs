using System;
using System.IO;
using FileFormat.NesChr;

namespace FileFormat.NesChr.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleTile() {
    var pixels = new byte[128 * 8];
    // Set one pixel of each value in tile 0
    pixels[0] = 0;
    pixels[1] = 1;
    pixels[2] = 2;
    pixels[3] = 3;

    var original = new NesChrFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var bytes = NesChrWriter.ToBytes(original);
    // Only read back the first tile (16 bytes)
    var singleTileData = new byte[16];
    Array.Copy(bytes, 0, singleTileData, 0, 16);

    // Reconstruct from full output
    var restored = NesChrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData[0], Is.EqualTo(0));
    Assert.That(restored.PixelData[1], Is.EqualTo(1));
    Assert.That(restored.PixelData[2], Is.EqualTo(2));
    Assert.That(restored.PixelData[3], Is.EqualTo(3));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleTiles() {
    var pixels = new byte[128 * 16];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 4);

    var original = new NesChrFile {
      Width = 128,
      Height = 16,
      PixelData = pixels
    };

    var bytes = NesChrWriter.ToBytes(original);
    var restored = NesChrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new NesChrFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8]
    };

    var bytes = NesChrWriter.ToBytes(original);
    var restored = NesChrReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllMaxValues() {
    var pixels = new byte[128 * 8];
    Array.Fill(pixels, (byte)3);

    var original = new NesChrFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var bytes = NesChrWriter.ToBytes(original);
    var restored = NesChrReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixels = new byte[128 * 8];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 4);

    var original = new NesChrFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".chr");
    try {
      var bytes = NesChrWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = NesChrReader.FromFile(new FileInfo(tempPath));

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
    var pixels = new byte[128 * 8];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 4);

    var original = new NesChrFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var rawImage = NesChrFile.ToRawImage(original);
    var restored = NesChrFile.FromRawImage(rawImage);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
