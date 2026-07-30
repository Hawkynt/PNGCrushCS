using System;
using System.IO;
using FileFormat.Pvr;

namespace FileFormat.Pvr.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_PVRTC() {
    var compressedData = new byte[32];
    for (var i = 0; i < compressedData.Length; ++i)
      compressedData[i] = (byte)(i * 7 % 256);

    var original = new PvrFile {
      Width = 8,
      Height = 8,
      Depth = 1,
      PixelFormat = PvrPixelFormat.PVRTC_4BPP_RGBA,
      ColorSpace = PvrColorSpace.Srgb,
      ChannelType = 0,
      Flags = 0x02,
      Surfaces = 1,
      Faces = 1,
      MipmapCount = 1,
      CompressedData = compressedData
    };

    var bytes = PvrWriter.ToBytes(original);
    var restored = PvrReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.Depth, Is.EqualTo(original.Depth));
    Assert.That(restored.PixelFormat, Is.EqualTo(original.PixelFormat));
    Assert.That(restored.ColorSpace, Is.EqualTo(original.ColorSpace));
    Assert.That(restored.Flags, Is.EqualTo(original.Flags));
    Assert.That(restored.Surfaces, Is.EqualTo(original.Surfaces));
    Assert.That(restored.Faces, Is.EqualTo(original.Faces));
    Assert.That(restored.MipmapCount, Is.EqualTo(original.MipmapCount));
    Assert.That(restored.CompressedData, Is.EqualTo(original.CompressedData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ETC1() {
    var compressedData = new byte[8];
    for (var i = 0; i < compressedData.Length; ++i)
      compressedData[i] = (byte)(i * 31 % 256);

    var original = new PvrFile {
      Width = 4,
      Height = 4,
      Depth = 1,
      PixelFormat = PvrPixelFormat.ETC1,
      ColorSpace = PvrColorSpace.Linear,
      Surfaces = 1,
      Faces = 1,
      MipmapCount = 1,
      CompressedData = compressedData
    };

    var bytes = PvrWriter.ToBytes(original);
    var restored = PvrReader.FromBytes(bytes);

    Assert.That(restored.PixelFormat, Is.EqualTo(PvrPixelFormat.ETC1));
    Assert.That(restored.ColorSpace, Is.EqualTo(PvrColorSpace.Linear));
    Assert.That(restored.CompressedData, Is.EqualTo(original.CompressedData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_WithMetadata() {
    var metadata = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
    var compressedData = new byte[16];
    for (var i = 0; i < compressedData.Length; ++i)
      compressedData[i] = (byte)(i * 13 % 256);

    var original = new PvrFile {
      Width = 4,
      Height = 4,
      Depth = 1,
      PixelFormat = PvrPixelFormat.PVRTC_4BPP_RGB,
      ColorSpace = PvrColorSpace.Srgb,
      Surfaces = 1,
      Faces = 1,
      MipmapCount = 1,
      MetadataSize = metadata.Length,
      Metadata = metadata,
      CompressedData = compressedData
    };

    var bytes = PvrWriter.ToBytes(original);
    var restored = PvrReader.FromBytes(bytes);

    Assert.That(restored.MetadataSize, Is.EqualTo(metadata.Length));
    Assert.That(restored.Metadata, Is.EqualTo(metadata));
    Assert.That(restored.CompressedData, Is.EqualTo(compressedData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var compressedData = new byte[16];
    for (var i = 0; i < compressedData.Length; ++i)
      compressedData[i] = (byte)(i * 11 % 256);

    var original = new PvrFile {
      Width = 4,
      Height = 4,
      Depth = 1,
      PixelFormat = PvrPixelFormat.ETC2_RGB,
      ColorSpace = PvrColorSpace.Linear,
      Surfaces = 1,
      Faces = 1,
      MipmapCount = 1,
      CompressedData = compressedData
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pvr");
    try {
      var bytes = PvrWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);

      var restored = PvrReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.PixelFormat, Is.EqualTo(original.PixelFormat));
      Assert.That(restored.CompressedData, Is.EqualTo(original.CompressedData));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
