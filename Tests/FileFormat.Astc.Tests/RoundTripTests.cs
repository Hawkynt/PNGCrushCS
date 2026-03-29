using System;
using System.IO;
using FileFormat.Astc;

namespace FileFormat.Astc.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_4x4() {
    var blockData = new byte[16];
    for (var i = 0; i < blockData.Length; ++i)
      blockData[i] = (byte)(i * 13 % 256);

    var original = new AstcFile {
      Width = 4,
      Height = 4,
      Depth = 1,
      BlockDimX = 4,
      BlockDimY = 4,
      BlockDimZ = 1,
      CompressedData = blockData
    };

    var bytes = AstcWriter.ToBytes(original);
    var restored = AstcReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Depth, Is.EqualTo(original.Depth));
    Assert.That(restored.BlockDimX, Is.EqualTo(original.BlockDimX));
    Assert.That(restored.BlockDimY, Is.EqualTo(original.BlockDimY));
    Assert.That(restored.BlockDimZ, Is.EqualTo(original.BlockDimZ));
    Assert.That(restored.CompressedData, Is.EqualTo(original.CompressedData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_8x8() {
    // 16x16 image with 8x8 blocks = 4 blocks = 64 bytes
    var blockData = new byte[64];
    for (var i = 0; i < blockData.Length; ++i)
      blockData[i] = (byte)(i * 7 % 256);

    var original = new AstcFile {
      Width = 16,
      Height = 16,
      Depth = 1,
      BlockDimX = 8,
      BlockDimY = 8,
      BlockDimZ = 1,
      CompressedData = blockData
    };

    var bytes = AstcWriter.ToBytes(original);
    var restored = AstcReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(16));
    Assert.That(restored.Height, Is.EqualTo(16));
    Assert.That(restored.BlockDimX, Is.EqualTo(8));
    Assert.That(restored.BlockDimY, Is.EqualTo(8));
    Assert.That(restored.CompressedData, Is.EqualTo(original.CompressedData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_12x12() {
    // 24x24 image with 12x12 blocks = 4 blocks = 64 bytes
    var blockData = new byte[64];
    for (var i = 0; i < blockData.Length; ++i)
      blockData[i] = (byte)(i * 11 % 256);

    var original = new AstcFile {
      Width = 24,
      Height = 24,
      Depth = 1,
      BlockDimX = 12,
      BlockDimY = 12,
      BlockDimZ = 1,
      CompressedData = blockData
    };

    var bytes = AstcWriter.ToBytes(original);
    var restored = AstcReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(24));
    Assert.That(restored.Height, Is.EqualTo(24));
    Assert.That(restored.BlockDimX, Is.EqualTo(12));
    Assert.That(restored.BlockDimY, Is.EqualTo(12));
    Assert.That(restored.CompressedData, Is.EqualTo(original.CompressedData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_3D() {
    // 3D texture: 4x4x4 blocks, 8x8x8 image = 8 blocks = 128 bytes
    var blockData = new byte[128];
    for (var i = 0; i < blockData.Length; ++i)
      blockData[i] = (byte)(i * 3 % 256);

    var original = new AstcFile {
      Width = 8,
      Height = 8,
      Depth = 8,
      BlockDimX = 4,
      BlockDimY = 4,
      BlockDimZ = 4,
      CompressedData = blockData
    };

    var bytes = AstcWriter.ToBytes(original);
    var restored = AstcReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(8));
    Assert.That(restored.Height, Is.EqualTo(8));
    Assert.That(restored.Depth, Is.EqualTo(8));
    Assert.That(restored.BlockDimX, Is.EqualTo(4));
    Assert.That(restored.BlockDimY, Is.EqualTo(4));
    Assert.That(restored.BlockDimZ, Is.EqualTo(4));
    Assert.That(restored.CompressedData, Is.EqualTo(original.CompressedData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var blockData = new byte[16];
    blockData[0] = 0xAA;
    blockData[15] = 0x55;

    var original = new AstcFile {
      Width = 4,
      Height = 4,
      Depth = 1,
      BlockDimX = 4,
      BlockDimY = 4,
      BlockDimZ = 1,
      CompressedData = blockData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".astc");
    try {
      var bytes = AstcWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = AstcReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(4));
      Assert.That(restored.Height, Is.EqualTo(4));
      Assert.That(restored.CompressedData, Is.EqualTo(original.CompressedData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
