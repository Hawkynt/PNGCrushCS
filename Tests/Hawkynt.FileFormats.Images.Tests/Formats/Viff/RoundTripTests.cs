using System;
using System.IO;
using FileFormat.Viff;

namespace FileFormat.Viff.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_8bit_SingleBand() {
    var width = 4;
    var height = 3;
    var pixelData = new byte[width * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 21);

    var original = new ViffFile {
      Width = width,
      Height = height,
      Bands = 1,
      StorageType = ViffStorageType.Byte,
      PixelData = pixelData
    };

    var bytes = ViffWriter.ToBytes(original);
    var restored = ViffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bands, Is.EqualTo(original.Bands));
    Assert.That(restored.StorageType, Is.EqualTo(original.StorageType));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_16bit() {
    var width = 2;
    var height = 2;
    var bands = 1;
    var pixelData = new byte[width * height * bands * 2];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 37);

    var original = new ViffFile {
      Width = width,
      Height = height,
      Bands = bands,
      StorageType = ViffStorageType.Short,
      PixelData = pixelData
    };

    var bytes = ViffWriter.ToBytes(original);
    var restored = ViffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.StorageType, Is.EqualTo(ViffStorageType.Short));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Float() {
    var width = 3;
    var height = 2;
    var bands = 1;
    var pixelData = new byte[width * height * bands * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7);

    var original = new ViffFile {
      Width = width,
      Height = height,
      Bands = bands,
      StorageType = ViffStorageType.Float,
      PixelData = pixelData
    };

    var bytes = ViffWriter.ToBytes(original);
    var restored = ViffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.StorageType, Is.EqualTo(ViffStorageType.Float));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiBand() {
    var width = 4;
    var height = 4;
    var bands = 3;
    var pixelData = new byte[width * height * bands];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 11);

    var original = new ViffFile {
      Width = width,
      Height = height,
      Bands = bands,
      StorageType = ViffStorageType.Byte,
      ColorSpaceModel = ViffColorSpaceModel.Rgb,
      PixelData = pixelData
    };

    var bytes = ViffWriter.ToBytes(original);
    var restored = ViffReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Bands, Is.EqualTo(bands));
    Assert.That(restored.ColorSpaceModel, Is.EqualTo(ViffColorSpaceModel.Rgb));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CommentPreserved() {
    var original = new ViffFile {
      Width = 2,
      Height = 2,
      Bands = 1,
      StorageType = ViffStorageType.Byte,
      Comment = "VIFF round-trip comment test",
      PixelData = new byte[4]
    };

    var bytes = ViffWriter.ToBytes(original);
    var restored = ViffReader.FromBytes(bytes);

    Assert.That(restored.Comment, Is.EqualTo(original.Comment));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = new ViffFile {
      Width = 8,
      Height = 4,
      Bands = 1,
      StorageType = ViffStorageType.Byte,
      PixelData = new byte[32]
    };

    for (var i = 0; i < original.PixelData.Length; ++i)
      original.PixelData[i] = (byte)(i * 5);

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".viff");
    try {
      var bytes = ViffWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = ViffReader.FromFile(new FileInfo(tempPath));

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
  public void RoundTrip_WithMapData() {
    var mapData = new byte[256 * 3]; // 256-entry RGB map
    for (var i = 0; i < mapData.Length; ++i)
      mapData[i] = (byte)i;

    var original = new ViffFile {
      Width = 4,
      Height = 4,
      Bands = 1,
      StorageType = ViffStorageType.Byte,
      PixelData = new byte[16],
      MapData = mapData,
      MapType = ViffMapType.Byte,
      MapRowSize = 256,
      MapColSize = 3,
      MapStorageType = ViffStorageType.Byte
    };

    var bytes = ViffWriter.ToBytes(original);
    var restored = ViffReader.FromBytes(bytes);

    Assert.That(restored.MapData, Is.Not.Null);
    Assert.That(restored.MapData, Is.EqualTo(original.MapData));
    Assert.That(restored.MapRowSize, Is.EqualTo(256));
    Assert.That(restored.MapColSize, Is.EqualTo(3));
  }
}
