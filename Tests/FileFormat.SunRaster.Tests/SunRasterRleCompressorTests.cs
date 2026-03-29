using System;
using FileFormat.SunRaster;

namespace FileFormat.SunRaster.Tests;

[TestFixture]
public sealed class SunRasterRleCompressorTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_MixedData_DecompressesCorrectly() {
    var data = new byte[256];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 17);

    var compressed = SunRasterRleCompressor.Compress(data);
    var decompressed = SunRasterRleCompressor.Decompress(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllSame_DecompressesCorrectly() {
    var data = new byte[300];
    Array.Fill(data, (byte)42);

    var compressed = SunRasterRleCompressor.Compress(data);
    var decompressed = SunRasterRleCompressor.Decompress(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void Compress_AllSame_CompressesSmaller() {
    var data = new byte[300];
    Array.Fill(data, (byte)42);

    var compressed = SunRasterRleCompressor.Compress(data);

    Assert.That(compressed.Length, Is.LessThan(data.Length));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_EscapeByte_PreservedCorrectly() {
    var data = new byte[] { 0x80, 0x80, 0x01, 0x80, 0xFF };

    var compressed = SunRasterRleCompressor.Compress(data);
    var decompressed = SunRasterRleCompressor.Decompress(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_Empty_ReturnsEmpty() {
    var data = Array.Empty<byte>();

    var compressed = SunRasterRleCompressor.Compress(data);

    Assert.That(compressed, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_SingleByte_DecompressesCorrectly() {
    var data = new byte[] { 0x42 };

    var compressed = SunRasterRleCompressor.Compress(data);
    var decompressed = SunRasterRleCompressor.Decompress(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_SingleEscapeByte_DecompressesCorrectly() {
    var data = new byte[] { 0x80 };

    var compressed = SunRasterRleCompressor.Compress(data);
    var decompressed = SunRasterRleCompressor.Decompress(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_LargeData_DecompressesCorrectly() {
    var data = new byte[4096];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 5 == 0 ? 0x80 : i % 256);

    var compressed = SunRasterRleCompressor.Compress(data);
    var decompressed = SunRasterRleCompressor.Decompress(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }
}
