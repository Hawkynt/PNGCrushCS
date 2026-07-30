using System;
using FileFormat.Pcx;

namespace FileFormat.Pcx.Tests;

[TestFixture]
public sealed class PcxRleCompressorTests {

  [Test]
  [Category("Unit")]
  public void Compress_Empty_ReturnsEmpty() {
    var result = PcxRleCompressor.Compress(ReadOnlySpan<byte>.Empty);
    Assert.That(result.Length, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Compress_AllSame_CompressesWell() {
    var original = new byte[256];
    Array.Fill(original, (byte)42);

    var compressed = PcxRleCompressor.Compress(original);

    Assert.That(compressed.Length, Is.LessThan(original.Length / 5));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_RoundTrip_MixedData() {
    var original = new byte[] { 5, 5, 5, 5, 10, 20, 30, 40, 40, 40, 0, 0, 0, 0, 0, 99 };

    var compressed = PcxRleCompressor.Compress(original);
    var decompressed = PcxRleCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void Compress_HighValueBytes_Encoded() {
    // Values >= 0xC0 must be RLE-encoded even as singletons
    var original = new byte[] { 0xC0, 0xFF, 0xE0 };

    var compressed = PcxRleCompressor.Compress(original);
    var decompressed = PcxRleCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
    // Each high-value byte requires 2 bytes (0xC1 + value), so compressed must be >= 6
    Assert.That(compressed.Length, Is.GreaterThanOrEqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void EstimateCompressionRatio_AllSame_ReturnsLowRatio() {
    var data = new byte[4096];
    Array.Fill(data, (byte)0xAA);

    var ratio = PcxRleCompressor.EstimateCompressionRatio(data);

    Assert.That(ratio, Is.LessThan(0.1));
  }
}
