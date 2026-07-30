using System;
using System.IO;
using FileFormat.QuakeSpr;

namespace FileFormat.QuakeSpr.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleFrame() {
    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 17 % 256);

    var original = new QuakeSprFile {
      Width = 4,
      Height = 3,
      SpriteType = 2,
      NumFrames = 1,
      BoundingRadius = 42.0f,
      BeamLength = 1.5f,
      SyncType = 1,
      PixelData = pixelData
    };

    var bytes = QuakeSprWriter.ToBytes(original);
    var restored = QuakeSprReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.SpriteType, Is.EqualTo(original.SpriteType));
    Assert.That(restored.NumFrames, Is.EqualTo(original.NumFrames));
    Assert.That(restored.BoundingRadius, Is.EqualTo(original.BoundingRadius));
    Assert.That(restored.BeamLength, Is.EqualTo(original.BeamLength));
    Assert.That(restored.SyncType, Is.EqualTo(original.SyncType));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var pixelData = new byte[2 * 2];
    pixelData[0] = 10;
    pixelData[1] = 20;
    pixelData[2] = 30;
    pixelData[3] = 40;

    var original = new QuakeSprFile {
      Width = 2,
      Height = 2,
      PixelData = pixelData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".spr");
    try {
      var bytes = QuakeSprWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = QuakeSprReader.FromFile(new FileInfo(tempPath));

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
  public void RoundTrip_LargerSprite() {
    var width = 64;
    var height = 48;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new QuakeSprFile {
      Width = width,
      Height = height,
      PixelData = pixelData
    };

    var bytes = QuakeSprWriter.ToBytes(original);
    var restored = QuakeSprReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var pixelData = new byte[] { 0, 1, 2, 3 };
    var original = new QuakeSprFile {
      Width = 2,
      Height = 2,
      PixelData = pixelData
    };

    var raw = QuakeSprFile.ToRawImage(original);
    var restored = QuakeSprFile.FromRawImage(raw);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
