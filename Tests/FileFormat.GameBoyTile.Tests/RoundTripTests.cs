using System;
using System.IO;
using FileFormat.GameBoyTile;

namespace FileFormat.GameBoyTile.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleTile() {
    var pixelData = new byte[128 * 8];
    // Set up a known pattern in the first tile
    for (var row = 0; row < 8; ++row)
      for (var col = 0; col < 8; ++col)
        pixelData[row * 128 + col] = (byte)((row + col) % 4);

    var original = new GameBoyTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixelData
    };

    var bytes = GameBoyTileWriter.ToBytes(original);
    var restored = GameBoyTileReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleTiles() {
    var pixelData = new byte[128 * 16];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 4);

    var original = new GameBoyTileFile {
      Width = 128,
      Height = 16,
      PixelData = pixelData
    };

    var bytes = GameBoyTileWriter.ToBytes(original);
    var restored = GameBoyTileReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var pixelData = new byte[128 * 8];

    var original = new GameBoyTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixelData
    };

    var bytes = GameBoyTileWriter.ToBytes(original);
    var restored = GameBoyTileReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllMaxColor() {
    var pixelData = new byte[128 * 8];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = 3;

    var original = new GameBoyTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixelData
    };

    var bytes = GameBoyTileWriter.ToBytes(original);
    var restored = GameBoyTileReader.FromBytes(bytes);

    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[128 * 8];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 4);

    var original = new GameBoyTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixelData
    };

    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".2bpp");
    try {
      var bytes = GameBoyTileWriter.ToBytes(original);
      File.WriteAllBytes(path, bytes);

      var restored = GameBoyTileReader.FromFile(new FileInfo(path));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var pixelData = new byte[128 * 8];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 4);

    var original = new GameBoyTileFile {
      Width = 128,
      Height = 8,
      PixelData = pixelData
    };

    var raw = GameBoyTileFile.ToRawImage(original);
    var restored = GameBoyTileFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
