using System;
using FileFormat.Tga;

namespace FileFormat.Tga.Tests;

[TestFixture]
public sealed class TgaRleCompressorTests {

  [Test]
  [Category("Unit")]
  public void Compress_Empty_ReturnsEmpty() {
    var result = TgaRleCompressor.Compress(ReadOnlySpan<byte>.Empty, 1);
    Assert.That(result.Length, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Compress_AllSame_3Bpp_CompressesWell() {
    var data = new byte[128 * 3];
    for (var i = 0; i < 128; ++i) {
      data[i * 3] = 0xAA;
      data[i * 3 + 1] = 0xBB;
      data[i * 3 + 2] = 0xCC;
    }

    var compressed = TgaRleCompressor.Compress(data, 3);

    Assert.That(compressed.Length, Is.LessThan(data.Length));
  }

  [Test]
  [Category("Unit")]
  public void Compress_AllSame_4Bpp_CompressesWell() {
    var data = new byte[128 * 4];
    for (var i = 0; i < 128; ++i) {
      data[i * 4] = 0x11;
      data[i * 4 + 1] = 0x22;
      data[i * 4 + 2] = 0x33;
      data[i * 4 + 3] = 0xFF;
    }

    var compressed = TgaRleCompressor.Compress(data, 4);

    Assert.That(compressed.Length, Is.LessThan(data.Length));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_RoundTrip_MixedData() {
    var data = new byte[32 * 3];
    for (var i = 0; i < 8; ++i) {
      data[i * 3] = 0xFF;
      data[i * 3 + 1] = 0x00;
      data[i * 3 + 2] = 0x00;
    }
    for (var i = 8; i < 32; ++i) {
      data[i * 3] = (byte)(i * 7);
      data[i * 3 + 1] = (byte)(i * 5);
      data[i * 3 + 2] = (byte)(i * 3);
    }

    var compressed = TgaRleCompressor.Compress(data, 3);
    var decompressed = TgaRleCompressor.Decompress(compressed, 32, 3);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void EstimateCompressionRatio_AllSame_ReturnsLowRatio() {
    var data = new byte[512 * 3];
    for (var i = 0; i < 512; ++i) {
      data[i * 3] = 0xDD;
      data[i * 3 + 1] = 0xDD;
      data[i * 3 + 2] = 0xDD;
    }

    var ratio = TgaRleCompressor.EstimateCompressionRatio(data, 3);

    Assert.That(ratio, Is.LessThan(0.2));
  }
}
