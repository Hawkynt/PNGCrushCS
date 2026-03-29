using System;
using System.IO;
using FileFormat.Art;

namespace FileFormat.Art.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleTile() {
    var pixels = new byte[8 * 8];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 256);

    var original = new ArtFile {
      TileStart = 0,
      Tiles = [new ArtTile { Width = 8, Height = 8, PixelData = pixels }]
    };

    var bytes = ArtWriter.ToBytes(original);
    var restored = ArtReader.FromBytes(bytes);

    Assert.That(restored.TileStart, Is.EqualTo(0));
    Assert.That(restored.Tiles, Has.Count.EqualTo(1));
    Assert.That(restored.Tiles[0].Width, Is.EqualTo(8));
    Assert.That(restored.Tiles[0].Height, Is.EqualTo(8));
    Assert.That(restored.Tiles[0].PixelData, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiTile() {
    var pixels1 = new byte[4 * 4];
    var pixels2 = new byte[8 * 16];
    for (var i = 0; i < pixels1.Length; ++i)
      pixels1[i] = (byte)(i * 3 % 256);
    for (var i = 0; i < pixels2.Length; ++i)
      pixels2[i] = (byte)(i * 7 % 256);

    var original = new ArtFile {
      TileStart = 100,
      Tiles = [
        new ArtTile { Width = 4, Height = 4, PixelData = pixels1 },
        new ArtTile { Width = 8, Height = 16, PixelData = pixels2 }
      ]
    };

    var bytes = ArtWriter.ToBytes(original);
    var restored = ArtReader.FromBytes(bytes);

    Assert.That(restored.TileStart, Is.EqualTo(100));
    Assert.That(restored.Tiles, Has.Count.EqualTo(2));
    Assert.That(restored.Tiles[0].Width, Is.EqualTo(4));
    Assert.That(restored.Tiles[0].Height, Is.EqualTo(4));
    Assert.That(restored.Tiles[0].PixelData, Is.EqualTo(pixels1));
    Assert.That(restored.Tiles[1].Width, Is.EqualTo(8));
    Assert.That(restored.Tiles[1].Height, Is.EqualTo(16));
    Assert.That(restored.Tiles[1].PixelData, Is.EqualTo(pixels2));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EmptyTile() {
    var original = new ArtFile {
      TileStart = 0,
      Tiles = [new ArtTile { Width = 0, Height = 0, PixelData = [] }]
    };

    var bytes = ArtWriter.ToBytes(original);
    var restored = ArtReader.FromBytes(bytes);

    Assert.That(restored.Tiles, Has.Count.EqualTo(1));
    Assert.That(restored.Tiles[0].Width, Is.EqualTo(0));
    Assert.That(restored.Tiles[0].Height, Is.EqualTo(0));
    Assert.That(restored.Tiles[0].PixelData, Is.Empty);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AnimData() {
    var original = new ArtFile {
      TileStart = 5,
      Tiles = [new ArtTile {
        Width = 2,
        Height = 2,
        AnimType = ArtAnimType.Forward,
        NumFrames = 7,
        XOffset = -3,
        YOffset = -10,
        AnimSpeed = 15,
        PixelData = [10, 20, 30, 40]
      }]
    };

    var bytes = ArtWriter.ToBytes(original);
    var restored = ArtReader.FromBytes(bytes);

    var tile = restored.Tiles[0];
    Assert.That(tile.AnimType, Is.EqualTo(ArtAnimType.Forward));
    Assert.That(tile.NumFrames, Is.EqualTo(7));
    Assert.That(tile.XOffset, Is.EqualTo(-3));
    Assert.That(tile.YOffset, Is.EqualTo(-10));
    Assert.That(tile.AnimSpeed, Is.EqualTo(15));
    Assert.That(tile.PixelData, Is.EqualTo(new byte[] { 10, 20, 30, 40 }));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".art");
    try {
      var pixels = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
      var original = new ArtFile {
        TileStart = 0,
        Tiles = [new ArtTile { Width = 2, Height = 2, PixelData = pixels }]
      };

      var bytes = ArtWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = ArtReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Tiles, Has.Count.EqualTo(1));
      Assert.That(restored.Tiles[0].PixelData, Is.EqualTo(pixels));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
