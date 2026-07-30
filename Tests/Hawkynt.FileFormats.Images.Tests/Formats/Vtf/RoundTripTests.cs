using System;
using System.Collections.Generic;
using FileFormat.Vtf;

namespace FileFormat.Vtf.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgba8888_SingleMip() {
    var pixelData = new byte[4 * 4 * 4];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var original = new VtfFile {
      Width = 4,
      Height = 4,
      MipmapCount = 1,
      Format = VtfFormat.Rgba8888,
      Flags = VtfFlags.None,
      Frames = 1,
      VersionMajor = 7,
      VersionMinor = 2,
      Surfaces = [
        new VtfSurface {
          Width = 4,
          Height = 4,
          MipLevel = 0,
          Frame = 0,
          Data = pixelData
        }
      ]
    };

    var bytes = VtfWriter.ToBytes(original);
    var restored = VtfReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.MipmapCount, Is.EqualTo(original.MipmapCount));
    Assert.That(restored.Format, Is.EqualTo(original.Format));
    Assert.That(restored.Frames, Is.EqualTo(original.Frames));
    Assert.That(restored.Surfaces, Has.Count.EqualTo(1));
    Assert.That(restored.Surfaces[0].Data, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Dxt1_WithThumbnail() {
    // DXT1: 8 bytes per 4x4 block
    var mipData = new byte[8]; // 4x4 DXT1 = 1 block = 8 bytes
    for (var i = 0; i < mipData.Length; ++i)
      mipData[i] = (byte)(i + 1);

    var thumbnailData = new byte[8]; // 4x4 DXT1 thumbnail
    for (var i = 0; i < thumbnailData.Length; ++i)
      thumbnailData[i] = (byte)(i + 100);

    var original = new VtfFile {
      Width = 4,
      Height = 4,
      MipmapCount = 1,
      Format = VtfFormat.Dxt1,
      Flags = VtfFlags.None,
      Frames = 1,
      VersionMajor = 7,
      VersionMinor = 2,
      ThumbnailData = thumbnailData,
      Surfaces = [
        new VtfSurface {
          Width = 4,
          Height = 4,
          MipLevel = 0,
          Frame = 0,
          Data = mipData
        }
      ]
    };

    var bytes = VtfWriter.ToBytes(original);
    var restored = VtfReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(4));
    Assert.That(restored.Height, Is.EqualTo(4));
    Assert.That(restored.Format, Is.EqualTo(VtfFormat.Dxt1));
    Assert.That(restored.ThumbnailData, Is.Not.Null);
    Assert.That(restored.ThumbnailData, Is.EqualTo(thumbnailData));
    Assert.That(restored.Surfaces, Has.Count.EqualTo(1));
    Assert.That(restored.Surfaces[0].Data, Is.EqualTo(mipData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb888_PreservesVersion() {
    var original = new VtfFile {
      Width = 8,
      Height = 8,
      MipmapCount = 1,
      Format = VtfFormat.Rgb888,
      Flags = VtfFlags.Trilinear | VtfFlags.Anisotropic,
      Frames = 1,
      VersionMajor = 7,
      VersionMinor = 2,
      Surfaces = [
        new VtfSurface {
          Width = 8,
          Height = 8,
          MipLevel = 0,
          Frame = 0,
          Data = new byte[8 * 8 * 3]
        }
      ]
    };

    var bytes = VtfWriter.ToBytes(original);
    var restored = VtfReader.FromBytes(bytes);

    Assert.That(restored.VersionMajor, Is.EqualTo(7));
    Assert.That(restored.VersionMinor, Is.EqualTo(2));
    Assert.That(restored.Flags, Is.EqualTo(VtfFlags.Trilinear | VtfFlags.Anisotropic));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_I8_GrayscaleFormat() {
    var pixelData = new byte[8 * 8]; // I8 = 1 byte per pixel
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)i;

    var original = new VtfFile {
      Width = 8,
      Height = 8,
      MipmapCount = 1,
      Format = VtfFormat.I8,
      Flags = VtfFlags.None,
      Frames = 1,
      VersionMajor = 7,
      VersionMinor = 2,
      Surfaces = [
        new VtfSurface {
          Width = 8,
          Height = 8,
          MipLevel = 0,
          Frame = 0,
          Data = pixelData
        }
      ]
    };

    var bytes = VtfWriter.ToBytes(original);
    var restored = VtfReader.FromBytes(bytes);

    Assert.That(restored.Format, Is.EqualTo(VtfFormat.I8));
    Assert.That(restored.Surfaces, Has.Count.EqualTo(1));
    Assert.That(restored.Surfaces[0].Data, Is.EqualTo(pixelData));
  }
}
