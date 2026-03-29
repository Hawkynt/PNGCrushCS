using System.Collections.Generic;
using FileFormat.WebP;

namespace FileFormat.WebP.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_Lossless_PreservesFeatures() {
    // VP8L: signature 0x2F, then width-1=3, height-1=3, no alpha
    // bits = 3 | (3 << 14) = 0x0000C003
    var vp8LData = new byte[] { 0x2F, 0x03, 0xC0, 0x00, 0x00 };

    var original = new WebPFile {
      Features = new WebPFeatures(4, 4, false, true, false),
      ImageData = vp8LData,
      IsLossless = true
    };

    var bytes = WebPWriter.ToBytes(original);
    var restored = WebPReader.FromBytes(bytes);

    Assert.That(restored.Features.Width, Is.EqualTo(4));
    Assert.That(restored.Features.Height, Is.EqualTo(4));
    Assert.That(restored.Features.IsLossless, Is.True);
    Assert.That(restored.Features.HasAlpha, Is.False);
    Assert.That(restored.ImageData, Is.EqualTo(vp8LData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Lossy_PreservesFeatures() {
    // VP8 keyframe: frame tag byte[0]=0 (keyframe), signature 0x9D 0x01 0x2A, 4x4
    var vp8Data = new byte[] { 0x00, 0x00, 0x00, 0x9D, 0x01, 0x2A, 0x04, 0x00, 0x04, 0x00 };

    var original = new WebPFile {
      Features = new WebPFeatures(4, 4, false, false, false),
      ImageData = vp8Data,
      IsLossless = false
    };

    var bytes = WebPWriter.ToBytes(original);
    var restored = WebPReader.FromBytes(bytes);

    Assert.That(restored.Features.Width, Is.EqualTo(4));
    Assert.That(restored.Features.Height, Is.EqualTo(4));
    Assert.That(restored.Features.IsLossless, Is.False);
    Assert.That(restored.ImageData, Is.EqualTo(vp8Data));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MetadataPreserved() {
    var vp8LData = new byte[] { 0x2F, 0x03, 0xC0, 0x00, 0x00 };
    var exifData = new byte[] { 0x45, 0x78, 0x69, 0x66, 0x00, 0x00 };

    var original = new WebPFile {
      Features = new WebPFeatures(4, 4, false, true, false),
      ImageData = vp8LData,
      IsLossless = true,
      MetadataChunks = [("EXIF", exifData)]
    };

    var bytes = WebPWriter.ToBytes(original);
    var restored = WebPReader.FromBytes(bytes);

    Assert.That(restored.MetadataChunks, Has.Count.EqualTo(1));
    Assert.That(restored.MetadataChunks[0].ChunkId, Is.EqualTo("EXIF"));
    Assert.That(restored.MetadataChunks[0].Data, Is.EqualTo(exifData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MetadataStripped() {
    var vp8LData = new byte[] { 0x2F, 0x03, 0xC0, 0x00, 0x00 };

    var original = new WebPFile {
      Features = new WebPFeatures(4, 4, false, true, false),
      ImageData = vp8LData,
      IsLossless = true,
      MetadataChunks = []
    };

    var bytes = WebPWriter.ToBytes(original);
    var restored = WebPReader.FromBytes(bytes);

    Assert.That(restored.MetadataChunks, Is.Empty);
  }
}
