using System;
using System.IO;
using FileFormat.ColoRix;

namespace FileFormat.ColoRix.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Uncompressed_IndexedImage() {
    var palette = new byte[ColoRixFile.PaletteSize];
    palette[0] = 63; palette[1] = 0; palette[2] = 0;
    palette[3] = 0; palette[4] = 63; palette[5] = 0;
    palette[6] = 0; palette[7] = 0; palette[8] = 63;

    var pixelData = new byte[4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 3);

    var original = new ColoRixFile {
      Width = 4,
      Height = 3,
      Palette = palette,
      PixelData = pixelData,
      StorageType = ColoRixCompression.None,
    };

    var bytes = ColoRixWriter.ToBytes(original);
    var restored = ColoRixReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.StorageType, Is.EqualTo(ColoRixCompression.None));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rle_IndexedImage() {
    var palette = new byte[ColoRixFile.PaletteSize];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)(i * 63 / 255);
      palette[i * 3 + 1] = (byte)(i * 63 / 255);
      palette[i * 3 + 2] = (byte)(i * 63 / 255);
    }

    var pixelData = new byte[8 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i / 4);

    var original = new ColoRixFile {
      Width = 8,
      Height = 4,
      Palette = palette,
      PixelData = pixelData,
      StorageType = ColoRixCompression.Rle,
    };

    var bytes = ColoRixWriter.ToBytes(original);
    var restored = ColoRixReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.StorageType, Is.EqualTo(ColoRixCompression.Rle));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SpecificPalette_PreservedExactly() {
    var palette = new byte[ColoRixFile.PaletteSize];
    palette[0] = 63; palette[1] = 0; palette[2] = 0;
    palette[765] = 0; palette[766] = 0; palette[767] = 63;

    var pixelData = new byte[] { 0, 255 };

    var original = new ColoRixFile {
      Width = 2,
      Height = 1,
      Palette = palette,
      PixelData = pixelData,
      StorageType = ColoRixCompression.None,
    };

    var bytes = ColoRixWriter.ToBytes(original);
    var restored = ColoRixReader.FromBytes(bytes);

    Assert.That(restored.Palette[0], Is.EqualTo(63));
    Assert.That(restored.Palette[1], Is.EqualTo(0));
    Assert.That(restored.Palette[2], Is.EqualTo(0));
    Assert.That(restored.Palette[765], Is.EqualTo(0));
    Assert.That(restored.Palette[766], Is.EqualTo(0));
    Assert.That(restored.Palette[767], Is.EqualTo(63));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var palette = new byte[ColoRixFile.PaletteSize];
    for (var i = 0; i < ColoRixFile.PaletteSize; ++i)
      palette[i] = (byte)(i % 64);

    var pixelData = new byte[10 * 10];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new ColoRixFile {
      Width = 10,
      Height = 10,
      Palette = palette,
      PixelData = pixelData,
      StorageType = ColoRixCompression.None,
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rix");
    try {
      var bytes = ColoRixWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = ColoRixReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Palette, Is.EqualTo(original.Palette));
      Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_320x200_StandardVgaResolution() {
    var palette = new byte[ColoRixFile.PaletteSize];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)(i % 64);
      palette[i * 3 + 1] = (byte)((i * 2) % 64);
      palette[i * 3 + 2] = (byte)((i * 3) % 64);
    }

    var pixelData = new byte[320 * 200];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i % 256);

    var original = new ColoRixFile {
      Width = 320,
      Height = 200,
      Palette = palette,
      PixelData = pixelData,
      StorageType = ColoRixCompression.None,
    };

    var bytes = ColoRixWriter.ToBytes(original);
    var restored = ColoRixReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(320));
    Assert.That(restored.Height, Is.EqualTo(200));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var original = new ColoRixFile {
      Width = 2,
      Height = 2,
      Palette = new byte[ColoRixFile.PaletteSize],
      PixelData = new byte[4],
      StorageType = ColoRixCompression.None,
    };

    var bytes = ColoRixWriter.ToBytes(original);
    var restored = ColoRixReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(2));
    Assert.That(restored.Height, Is.EqualTo(2));
    Assert.That(restored.Palette, Is.EqualTo(original.Palette));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }
}
