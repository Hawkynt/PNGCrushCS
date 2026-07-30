using System;
using System.IO;
using FileFormat.PcEngineTile;

namespace FileFormat.PcEngineTile.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleTile() {
    var pixels = new byte[128 * 8];
    pixels[0] = 0;
    pixels[1] = 1;
    pixels[2] = 2;
    pixels[3] = 3;
    pixels[4] = 4;
    pixels[5] = 5;
    pixels[6] = 10;
    pixels[7] = 15;

    var original = new PcEngineTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var bytes = PcEngineTileWriter.ToBytes(original);
    var restored = PcEngineTileReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData[0], Is.EqualTo(0));
    Assert.That(restored.PixelData[1], Is.EqualTo(1));
    Assert.That(restored.PixelData[2], Is.EqualTo(2));
    Assert.That(restored.PixelData[3], Is.EqualTo(3));
    Assert.That(restored.PixelData[4], Is.EqualTo(4));
    Assert.That(restored.PixelData[5], Is.EqualTo(5));
    Assert.That(restored.PixelData[6], Is.EqualTo(10));
    Assert.That(restored.PixelData[7], Is.EqualTo(15));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleTiles() {
    var pixels = new byte[128 * 16];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 16);

    var original = new PcEngineTileFile {
      Width = 128,
      Height = 16,
      PixelData = pixels
    };

    var bytes = PcEngineTileWriter.ToBytes(original);
    var restored = PcEngineTileReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new PcEngineTileFile {
      Width = 128,
      Height = 8,
      PixelData = new byte[128 * 8]
    };

    var bytes = PcEngineTileWriter.ToBytes(original);
    var restored = PcEngineTileReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllMaxValues() {
    var pixels = new byte[128 * 8];
    Array.Fill(pixels, (byte)15);

    var original = new PcEngineTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var bytes = PcEngineTileWriter.ToBytes(original);
    var restored = PcEngineTileReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixels = new byte[128 * 8];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 16);

    var original = new PcEngineTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pce");
    try {
      var bytes = PcEngineTileWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = PcEngineTileReader.FromFile(new FileInfo(tempPath));

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
      pixels[i] = (byte)(i % 16);

    var original = new PcEngineTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var rawImage = PcEngineTileFile.ToRawImage(original);
    var restored = PcEngineTileFile.FromRawImage(rawImage);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EachValueFrom0To15() {
    var pixels = new byte[128 * 8];
    for (var v = 0; v < 16; ++v)
      pixels[v] = (byte)v;

    var original = new PcEngineTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var bytes = PcEngineTileWriter.ToBytes(original);
    var restored = PcEngineTileReader.FromBytes(bytes);

    for (var v = 0; v < 16; ++v)
      Assert.That(restored.PixelData[v], Is.EqualTo(v), $"Pixel value {v} round-trip failed");
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_HighValuesAbove15AreMasked() {
    var pixels = new byte[128 * 8];
    pixels[0] = 0xFF; // should be masked to 0x0F = 15

    var original = new PcEngineTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixels
    };

    var bytes = PcEngineTileWriter.ToBytes(original);
    var restored = PcEngineTileReader.FromBytes(bytes);

    Assert.That(restored.PixelData[0], Is.EqualTo(15));
  }
}
