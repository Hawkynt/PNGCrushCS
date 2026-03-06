using System;
using FileFormat.Tiff;

namespace FileFormat.Tiff.Tests;

[TestFixture]
public sealed class PackBitsCompressorTests {

  [Test]
  [Category("Unit")]
  public void Compress_Empty_ReturnsEmpty() {
    var result = PackBitsCompressor.Compress(ReadOnlySpan<byte>.Empty);

    Assert.That(result, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void Compress_AllSame_CompressesWell() {
    var data = new byte[128];
    Array.Fill(data, (byte)0xAB);

    var compressed = PackBitsCompressor.Compress(data);

    Assert.That(compressed.Length, Is.LessThan(data.Length));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_RoundTrip_MixedData() {
    var data = new byte[64];
    for (var i = 0; i < 16; ++i)
      data[i] = 0xFF;
    for (var i = 16; i < 64; ++i)
      data[i] = (byte)(i * 3);

    var compressed = PackBitsCompressor.Compress(data);
    var decompressed = PackBitsCompressor.Decompress(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void Compress_AllDifferent_LiteralRuns() {
    var data = new byte[128];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)i;

    var compressed = PackBitsCompressor.Compress(data);

    Assert.That(compressed.Length, Is.GreaterThanOrEqualTo(data.Length));
  }

  [Test]
  [Category("Unit")]
  public void EstimateCompressionRatio_AllSame_ReturnsLowRatio() {
    var data = new byte[256];
    Array.Fill(data, (byte)0x42);

    var ratio = PackBitsCompressor.EstimateCompressionRatio(data);

    Assert.That(ratio, Is.LessThan(0.1));
  }
}
