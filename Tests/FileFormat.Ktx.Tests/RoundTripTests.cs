using System;
using System.Collections.Generic;
using FileFormat.Ktx;

namespace FileFormat.Ktx.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Ktx1_SingleMip() {
    var pixelData = new byte[8 * 8 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new KtxFile {
      Width = 8,
      Height = 8,
      Depth = 0,
      Version = KtxVersion.Ktx1,
      MipmapCount = 1,
      Faces = 1,
      ArrayElements = 0,
      GlType = 0x1401,
      GlTypeSize = 1,
      GlFormat = 0x1908,
      GlInternalFormat = 0x8058,
      GlBaseInternalFormat = 0x1908,
      MipLevels = [new KtxMipLevel { Width = 8, Height = 8, Data = pixelData }]
    };

    var bytes = KtxWriter.ToBytes(original);
    var restored = KtxReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Depth, Is.EqualTo(original.Depth));
    Assert.That(restored.Version, Is.EqualTo(KtxVersion.Ktx1));
    Assert.That(restored.MipmapCount, Is.EqualTo(1));
    Assert.That(restored.Faces, Is.EqualTo(1));
    Assert.That(restored.MipLevels, Has.Count.EqualTo(1));
    Assert.That(restored.MipLevels[0].Data, Is.EqualTo(pixelData));
    Assert.That(restored.GlType, Is.EqualTo(original.GlType));
    Assert.That(restored.GlInternalFormat, Is.EqualTo(original.GlInternalFormat));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Ktx2_SingleMip() {
    var pixelData = new byte[4 * 4 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var original = new KtxFile {
      Width = 4,
      Height = 4,
      Depth = 0,
      Version = KtxVersion.Ktx2,
      MipmapCount = 1,
      Faces = 1,
      ArrayElements = 0,
      VkFormat = 37,
      TypeSize = 1,
      MipLevels = [new KtxMipLevel { Width = 4, Height = 4, Data = pixelData }]
    };

    var bytes = KtxWriter.ToBytes(original);
    var restored = KtxReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Depth, Is.EqualTo(original.Depth));
    Assert.That(restored.Version, Is.EqualTo(KtxVersion.Ktx2));
    Assert.That(restored.MipmapCount, Is.EqualTo(1));
    Assert.That(restored.Faces, Is.EqualTo(1));
    Assert.That(restored.MipLevels, Has.Count.EqualTo(1));
    Assert.That(restored.MipLevels[0].Data, Is.EqualTo(pixelData));
    Assert.That(restored.VkFormat, Is.EqualTo(original.VkFormat));
    Assert.That(restored.TypeSize, Is.EqualTo(original.TypeSize));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Ktx1_WithKeyValues() {
    var pixelData = new byte[2 * 2 * 4];

    var original = new KtxFile {
      Width = 2,
      Height = 2,
      Version = KtxVersion.Ktx1,
      MipmapCount = 1,
      Faces = 1,
      GlType = 0x1401,
      GlTypeSize = 1,
      GlFormat = 0x1908,
      GlInternalFormat = 0x8058,
      GlBaseInternalFormat = 0x1908,
      MipLevels = [new KtxMipLevel { Width = 2, Height = 2, Data = pixelData }],
      KeyValues = new List<KtxKeyValue> {
        new() { Key = "KTXorientation", Value = System.Text.Encoding.UTF8.GetBytes("S=r,T=d") }
      }
    };

    var bytes = KtxWriter.ToBytes(original);
    var restored = KtxReader.FromBytes(bytes);

    Assert.That(restored.KeyValues, Is.Not.Null);
    Assert.That(restored.KeyValues!, Has.Count.EqualTo(1));
    Assert.That(restored.KeyValues![0].Key, Is.EqualTo("KTXorientation"));
    Assert.That(restored.KeyValues[0].Value, Is.EqualTo(System.Text.Encoding.UTF8.GetBytes("S=r,T=d")));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Ktx2_WithKeyValues() {
    var pixelData = new byte[2 * 2 * 4];

    var original = new KtxFile {
      Width = 2,
      Height = 2,
      Version = KtxVersion.Ktx2,
      MipmapCount = 1,
      Faces = 1,
      VkFormat = 37,
      TypeSize = 1,
      MipLevels = [new KtxMipLevel { Width = 2, Height = 2, Data = pixelData }],
      KeyValues = new List<KtxKeyValue> {
        new() { Key = "KTXorientation", Value = System.Text.Encoding.UTF8.GetBytes("rd") }
      }
    };

    var bytes = KtxWriter.ToBytes(original);
    var restored = KtxReader.FromBytes(bytes);

    Assert.That(restored.KeyValues, Is.Not.Null);
    Assert.That(restored.KeyValues!, Has.Count.EqualTo(1));
    Assert.That(restored.KeyValues![0].Key, Is.EqualTo("KTXorientation"));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Ktx1_MultipleMipLevels() {
    var mip0 = new byte[8 * 8 * 4];
    var mip1 = new byte[4 * 4 * 4];
    var mip2 = new byte[2 * 2 * 4];
    for (var i = 0; i < mip0.Length; ++i) mip0[i] = (byte)(i % 256);
    for (var i = 0; i < mip1.Length; ++i) mip1[i] = (byte)(i * 3 % 256);
    for (var i = 0; i < mip2.Length; ++i) mip2[i] = (byte)(i * 5 % 256);

    var original = new KtxFile {
      Width = 8,
      Height = 8,
      Version = KtxVersion.Ktx1,
      MipmapCount = 3,
      Faces = 1,
      GlType = 0x1401,
      GlTypeSize = 1,
      GlFormat = 0x1908,
      GlInternalFormat = 0x8058,
      GlBaseInternalFormat = 0x1908,
      MipLevels = [
        new KtxMipLevel { Width = 8, Height = 8, Data = mip0 },
        new KtxMipLevel { Width = 4, Height = 4, Data = mip1 },
        new KtxMipLevel { Width = 2, Height = 2, Data = mip2 }
      ]
    };

    var bytes = KtxWriter.ToBytes(original);
    var restored = KtxReader.FromBytes(bytes);

    Assert.That(restored.MipmapCount, Is.EqualTo(3));
    Assert.That(restored.MipLevels, Has.Count.EqualTo(3));
    Assert.That(restored.MipLevels[0].Data, Is.EqualTo(mip0));
    Assert.That(restored.MipLevels[1].Data, Is.EqualTo(mip1));
    Assert.That(restored.MipLevels[2].Data, Is.EqualTo(mip2));
    Assert.That(restored.MipLevels[0].Width, Is.EqualTo(8));
    Assert.That(restored.MipLevels[1].Width, Is.EqualTo(4));
    Assert.That(restored.MipLevels[2].Width, Is.EqualTo(2));
  }
}
