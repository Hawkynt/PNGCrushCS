using System;
using System.IO;
using FileFormat.AmigaIcon;

namespace FileFormat.AmigaIcon.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_2ColorIcon() {
    var width = 32;
    var height = 16;
    var depth = 1;
    var planarSize = AmigaIconFile.PlanarDataSize(width, height, depth);
    var planarData = new byte[planarSize];
    for (var i = 0; i < planarData.Length; ++i)
      planarData[i] = (byte)(i * 7 % 256);

    var original = new AmigaIconFile {
      Width = width,
      Height = height,
      Depth = depth,
      IconType = (int)AmigaIconType.Tool,
      PlanarData = planarData,
    };

    var bytes = AmigaIconWriter.ToBytes(original);
    var restored = AmigaIconReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Depth, Is.EqualTo(original.Depth));
    Assert.That(restored.IconType, Is.EqualTo(original.IconType));
    Assert.That(restored.PlanarData, Is.EqualTo(original.PlanarData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_4ColorIcon() {
    var width = 48;
    var height = 32;
    var depth = 2;
    var planarSize = AmigaIconFile.PlanarDataSize(width, height, depth);
    var planarData = new byte[planarSize];
    for (var i = 0; i < planarData.Length; ++i)
      planarData[i] = (byte)(i * 13 % 256);

    var original = new AmigaIconFile {
      Width = width,
      Height = height,
      Depth = depth,
      IconType = (int)AmigaIconType.Drawer,
      PlanarData = planarData,
    };

    var bytes = AmigaIconWriter.ToBytes(original);
    var restored = AmigaIconReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Depth, Is.EqualTo(original.Depth));
    Assert.That(restored.IconType, Is.EqualTo(original.IconType));
    Assert.That(restored.PlanarData, Is.EqualTo(original.PlanarData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_8ColorIcon() {
    var width = 64;
    var height = 32;
    var depth = 3;
    var planarSize = AmigaIconFile.PlanarDataSize(width, height, depth);
    var planarData = new byte[planarSize];
    for (var i = 0; i < planarData.Length; ++i)
      planarData[i] = (byte)(i * 31 % 256);

    var original = new AmigaIconFile {
      Width = width,
      Height = height,
      Depth = depth,
      IconType = (int)AmigaIconType.Project,
      PlanarData = planarData,
    };

    var bytes = AmigaIconWriter.ToBytes(original);
    var restored = AmigaIconReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Depth, Is.EqualTo(original.Depth));
    Assert.That(restored.PlanarData, Is.EqualTo(original.PlanarData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var width = 32;
    var height = 16;
    var depth = 2;
    var planarSize = AmigaIconFile.PlanarDataSize(width, height, depth);
    var planarData = new byte[planarSize];
    for (var i = 0; i < planarData.Length; ++i)
      planarData[i] = (byte)(i % 256);

    var original = new AmigaIconFile {
      Width = width,
      Height = height,
      Depth = depth,
      IconType = (int)AmigaIconType.Disk,
      PlanarData = planarData,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".info");
    try {
      var bytes = AmigaIconWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = AmigaIconReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Depth, Is.EqualTo(original.Depth));
      Assert.That(restored.IconType, Is.EqualTo(original.IconType));
      Assert.That(restored.PlanarData, Is.EqualTo(original.PlanarData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_NonWordAlignedWidth() {
    // 17 pixels wide: bytesPerPlaneRow = ((17+15)/16)*2 = 4
    var width = 17;
    var height = 4;
    var depth = 2;
    var planarSize = AmigaIconFile.PlanarDataSize(width, height, depth);
    var planarData = new byte[planarSize];
    planarData[0] = 0xFF;
    planarData[1] = 0x80;

    var original = new AmigaIconFile {
      Width = width,
      Height = height,
      Depth = depth,
      PlanarData = planarData,
    };

    var bytes = AmigaIconWriter.ToBytes(original);
    var restored = AmigaIconReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.PlanarData, Is.EqualTo(original.PlanarData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_PlanarToChunkyToplanar_Preserves() {
    var width = 16;
    var height = 4;
    var depth = 2;

    // Build known chunky data (indices 0-3)
    var chunky = new byte[width * height];
    for (var i = 0; i < chunky.Length; ++i)
      chunky[i] = (byte)(i % 4);

    var planar = AmigaIconFile._ChunkyToPlanar(chunky, width, height, depth);
    var backToChunky = AmigaIconFile._PlanarToChunky(planar, width, height, depth);

    Assert.That(backToChunky, Is.EqualTo(chunky));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaRawImage() {
    var width = 16;
    var height = 4;
    var depth = 2;

    // Build chunky data with all 4 color indices so depth=2 is preserved
    var chunky = new byte[width * height];
    for (var i = 0; i < chunky.Length; ++i)
      chunky[i] = (byte)(i % 4);

    var planarData = AmigaIconFile._ChunkyToPlanar(chunky, width, height, depth);

    var original = new AmigaIconFile {
      Width = width,
      Height = height,
      Depth = depth,
      PlanarData = planarData,
    };

    var rawImage = original.ToRawImage();
    var restored = AmigaIconFile.FromRawImage(rawImage);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Depth, Is.EqualTo(original.Depth));

    var originalChunky = AmigaIconFile._PlanarToChunky(original.PlanarData, width, height, depth);
    var restoredChunky = AmigaIconFile._PlanarToChunky(restored.PlanarData, restored.Width, restored.Height, restored.Depth);
    Assert.That(restoredChunky, Is.EqualTo(originalChunky));
  }
}
