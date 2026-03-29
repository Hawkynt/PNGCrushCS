using System;
using System.Collections.Generic;
using System.Text;
using FileFormat.WebP;

namespace FileFormat.WebP.Tests;

[TestFixture]
public sealed class WebPWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => WebPWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_StartsWithRiffSignature() {
    var file = _CreateSimpleLosslessFile();
    var bytes = WebPWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.GreaterThanOrEqualTo(12));
    Assert.That(Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("RIFF"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_HasWebpFormType() {
    var file = _CreateSimpleLosslessFile();
    var bytes = WebPWriter.ToBytes(file);

    Assert.That(Encoding.ASCII.GetString(bytes, 8, 4), Is.EqualTo("WEBP"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_SimpleFormat_ContainsVp8LChunk() {
    var file = _CreateSimpleLosslessFile();
    var bytes = WebPWriter.ToBytes(file);

    var found = _FindChunk(bytes, "VP8L");
    Assert.That(found, Is.True, "VP8L chunk not found in output");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LossyFormat_ContainsVp8Chunk() {
    var vp8Data = new byte[] { 0x00, 0x00, 0x00, 0x9D, 0x01, 0x2A, 0x04, 0x00, 0x04, 0x00 };
    var file = new WebPFile {
      Features = new WebPFeatures(4, 4, false, false, false),
      ImageData = vp8Data,
      IsLossless = false
    };

    var bytes = WebPWriter.ToBytes(file);
    var found = _FindChunk(bytes, "VP8 ");
    Assert.That(found, Is.True, "VP8 chunk not found in output");
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WithMetadata_ContainsVp8XChunk() {
    var vp8LData = new byte[] { 0x2F, 0x03, 0xC0, 0x00, 0x00 };
    var file = new WebPFile {
      Features = new WebPFeatures(4, 4, false, true, false),
      ImageData = vp8LData,
      IsLossless = true,
      MetadataChunks = [("EXIF", new byte[] { 0x01, 0x02, 0x03 })]
    };

    var bytes = WebPWriter.ToBytes(file);
    var foundVp8X = _FindChunk(bytes, "VP8X");
    var foundExif = _FindChunk(bytes, "EXIF");
    Assert.That(foundVp8X, Is.True, "VP8X chunk not found in extended output");
    Assert.That(foundExif, Is.True, "EXIF chunk not found in extended output");
  }

  private static WebPFile _CreateSimpleLosslessFile() {
    var vp8LData = new byte[] { 0x2F, 0x03, 0xC0, 0x00, 0x00 };
    return new WebPFile {
      Features = new WebPFeatures(4, 4, false, true, false),
      ImageData = vp8LData,
      IsLossless = true
    };
  }

  private static bool _FindChunk(byte[] data, string chunkId) {
    var target = Encoding.ASCII.GetBytes(chunkId);
    for (var i = 12; i + 4 <= data.Length; ++i)
      if (data[i] == target[0] && data[i + 1] == target[1] && data[i + 2] == target[2] && data[i + 3] == target[3])
        return true;

    return false;
  }
}
