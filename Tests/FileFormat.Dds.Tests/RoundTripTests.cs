using System;
using FileFormat.Dds;

namespace FileFormat.Dds.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_UncompressedRgba() {
    var pixelData = new byte[4 * 4 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new DdsFile {
      Width = 4,
      Height = 4,
      MipMapCount = 1,
      Format = DdsFormat.Rgba,
      Surfaces = [new DdsSurface { Width = 4, Height = 4, MipLevel = 0, Data = pixelData }]
    };

    var bytes = DdsWriter.ToBytes(original);
    var restored = DdsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Format, Is.EqualTo(DdsFormat.Rgba));
    Assert.That(restored.MipMapCount, Is.EqualTo(1));
    Assert.That(restored.Surfaces, Has.Count.EqualTo(1));
    Assert.That(restored.Surfaces[0].Data, Is.EqualTo(original.Surfaces[0].Data));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_UncompressedRgb() {
    var pixelData = new byte[4 * 4 * 3];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new DdsFile {
      Width = 4,
      Height = 4,
      MipMapCount = 1,
      Format = DdsFormat.Rgb,
      Surfaces = [new DdsSurface { Width = 4, Height = 4, MipLevel = 0, Data = pixelData }]
    };

    var bytes = DdsWriter.ToBytes(original);
    var restored = DdsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Format, Is.EqualTo(DdsFormat.Rgb));
    Assert.That(restored.Surfaces[0].Data, Is.EqualTo(original.Surfaces[0].Data));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Dxt1_RawBlocks() {
    // DXT1: 4x4 block = 8 bytes
    var blockData = new byte[8];
    for (var i = 0; i < blockData.Length; ++i)
      blockData[i] = (byte)(i * 31 % 256);

    var original = new DdsFile {
      Width = 4,
      Height = 4,
      MipMapCount = 1,
      Format = DdsFormat.Dxt1,
      Surfaces = [new DdsSurface { Width = 4, Height = 4, MipLevel = 0, Data = blockData }]
    };

    var bytes = DdsWriter.ToBytes(original);
    var restored = DdsReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.Format, Is.EqualTo(DdsFormat.Dxt1));
    Assert.That(restored.Surfaces[0].Data, Is.EqualTo(original.Surfaces[0].Data));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Dxt5_RawBlocks() {
    // DXT5: 4x4 block = 16 bytes
    var blockData = new byte[16];
    for (var i = 0; i < blockData.Length; ++i)
      blockData[i] = (byte)(i * 17 % 256);

    var original = new DdsFile {
      Width = 4,
      Height = 4,
      MipMapCount = 1,
      Format = DdsFormat.Dxt5,
      Surfaces = [new DdsSurface { Width = 4, Height = 4, MipLevel = 0, Data = blockData }]
    };

    var bytes = DdsWriter.ToBytes(original);
    var restored = DdsReader.FromBytes(bytes);

    Assert.That(restored.Format, Is.EqualTo(DdsFormat.Dxt5));
    Assert.That(restored.Surfaces[0].Data, Is.EqualTo(original.Surfaces[0].Data));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultipleMipLevels() {
    // 8x8 RGBA: mip0 = 8*8*4=256, mip1 = 4*4*4=64, mip2 = 2*2*4=16, mip3 = 1*1*4=4
    var mip0 = new byte[256];
    var mip1 = new byte[64];
    var mip2 = new byte[16];
    var mip3 = new byte[4];

    for (var i = 0; i < mip0.Length; ++i)
      mip0[i] = (byte)(i % 256);
    for (var i = 0; i < mip1.Length; ++i)
      mip1[i] = (byte)(i * 3 % 256);
    for (var i = 0; i < mip2.Length; ++i)
      mip2[i] = (byte)(i * 7 % 256);
    for (var i = 0; i < mip3.Length; ++i)
      mip3[i] = (byte)(i * 11 % 256);

    var original = new DdsFile {
      Width = 8,
      Height = 8,
      MipMapCount = 4,
      Format = DdsFormat.Rgba,
      Surfaces = [
        new DdsSurface { Width = 8, Height = 8, MipLevel = 0, Data = mip0 },
        new DdsSurface { Width = 4, Height = 4, MipLevel = 1, Data = mip1 },
        new DdsSurface { Width = 2, Height = 2, MipLevel = 2, Data = mip2 },
        new DdsSurface { Width = 1, Height = 1, MipLevel = 3, Data = mip3 }
      ]
    };

    var bytes = DdsWriter.ToBytes(original);
    var restored = DdsReader.FromBytes(bytes);

    Assert.That(restored.MipMapCount, Is.EqualTo(4));
    Assert.That(restored.Surfaces, Has.Count.EqualTo(4));
    Assert.That(restored.Surfaces[0].Data, Is.EqualTo(mip0));
    Assert.That(restored.Surfaces[1].Data, Is.EqualTo(mip1));
    Assert.That(restored.Surfaces[2].Data, Is.EqualTo(mip2));
    Assert.That(restored.Surfaces[3].Data, Is.EqualTo(mip3));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Dx10Header_Preserved() {
    var pixelData = new byte[4 * 4 * 4];

    var original = new DdsFile {
      Width = 4,
      Height = 4,
      MipMapCount = 1,
      Format = DdsFormat.Dx10,
      HasDx10Header = true,
      Surfaces = [new DdsSurface { Width = 4, Height = 4, MipLevel = 0, Data = pixelData }]
    };

    var bytes = DdsWriter.ToBytes(original);
    var restored = DdsReader.FromBytes(bytes);

    Assert.That(restored.HasDx10Header, Is.True);
    Assert.That(restored.Format, Is.EqualTo(DdsFormat.Dx10));
  }
}
