using System;
using NUnit.Framework;
using FileFormat.Pcx;

namespace Optimizer.Pcx.Tests;

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
  public void RoundTrip_AllSame_ReconstructsOriginal() {
    var original = new byte[128];
    Array.Fill(original, (byte)42);

    var compressed = PcxRleCompressor.Compress(original);
    var decompressed = PcxRleCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
    Assert.That(compressed.Length, Is.LessThan(original.Length));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllDifferent_ReconstructsOriginal() {
    var original = new byte[63];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i % 192); // Stay below 0xC0 to avoid encoding ambiguity

    var compressed = PcxRleCompressor.Compress(original);
    var decompressed = PcxRleCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_Mixed_ReconstructsOriginal() {
    var original = new byte[] { 5, 5, 5, 5, 10, 20, 30, 40, 40, 40 };

    var compressed = PcxRleCompressor.Compress(original);
    var decompressed = PcxRleCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_HighValueBytes_ReconstructsOriginal() {
    // Values >= 0xC0 must use encoded form even for single bytes
    var original = new byte[] { 0xC0, 0xFF, 0xE0, 0x01, 0xFE };

    var compressed = PcxRleCompressor.Compress(original);
    var decompressed = PcxRleCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void Compress_LongRun_CompressesWell() {
    var original = new byte[256];
    Array.Fill(original, (byte)0xFF);

    var compressed = PcxRleCompressor.Compress(original);
    Assert.That(compressed.Length, Is.LessThan(original.Length / 5));
  }

  [Test]
  [Category("Unit")]
  public void EstimateCompressionRatio_AllSame_ReturnsLowRatio() {
    var data = new byte[4096];
    Array.Fill(data, (byte)0xAA);

    var ratio = PcxRleCompressor.EstimateCompressionRatio(data);
    Assert.That(ratio, Is.LessThan(0.1));
  }

  [Test]
  [Category("Unit")]
  public void EstimateCompressionRatio_Empty_Returns1() {
    var ratio = PcxRleCompressor.EstimateCompressionRatio(ReadOnlySpan<byte>.Empty);
    Assert.That(ratio, Is.EqualTo(1.0));
  }
}
