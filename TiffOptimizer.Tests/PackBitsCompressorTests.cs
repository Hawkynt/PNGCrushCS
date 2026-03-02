using System;
using NUnit.Framework;

namespace TiffOptimizer.Tests;

[TestFixture]
public sealed class PackBitsCompressorTests {
  [Test]
  [Category("Unit")]
  public void Compress_Empty_ReturnsEmpty() {
    var result = PackBitsCompressor.Compress(ReadOnlySpan<byte>.Empty);
    Assert.That(result.Length, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllSame_ReconstructsOriginal() {
    var original = new byte[128];
    Array.Fill(original, (byte)42);

    var compressed = PackBitsCompressor.Compress(original);
    var decompressed = PackBitsCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
    Assert.That(compressed.Length, Is.LessThan(original.Length));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllDifferent_ReconstructsOriginal() {
    var original = new byte[64];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)i;

    var compressed = PackBitsCompressor.Compress(original);
    var decompressed = PackBitsCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_Mixed_ReconstructsOriginal() {
    var original = new byte[] { 1, 1, 1, 1, 2, 3, 4, 5, 5, 5 };

    var compressed = PackBitsCompressor.Compress(original);
    var decompressed = PackBitsCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_SingleByte_ReconstructsOriginal() {
    var original = new byte[] { 99 };

    var compressed = PackBitsCompressor.Compress(original);
    var decompressed = PackBitsCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void Compress_LongRun_CompressesWell() {
    var original = new byte[256];
    Array.Fill(original, (byte)0xFF);

    var compressed = PackBitsCompressor.Compress(original);
    Assert.That(compressed.Length, Is.LessThan(original.Length / 10));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_LargeData_ReconstructsOriginal() {
    var rng = new Random(42);
    var original = new byte[4096];
    rng.NextBytes(original);

    var compressed = PackBitsCompressor.Compress(original);
    var decompressed = PackBitsCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  // --- EstimateCompressionRatio Tests ---

  [Test]
  [Category("Unit")]
  public void EstimateCompressionRatio_AllSame_ReturnsLowRatio() {
    var data = new byte[4096];
    Array.Fill(data, (byte)0xAA);

    var ratio = PackBitsCompressor.EstimateCompressionRatio(data);
    Assert.That(ratio, Is.LessThan(0.1), $"All-same data should compress very well, got ratio={ratio:F3}");
  }

  [Test]
  [Category("Unit")]
  public void EstimateCompressionRatio_RandomData_ReturnsHighRatio() {
    var rng = new Random(123);
    var data = new byte[4096];
    rng.NextBytes(data);

    var ratio = PackBitsCompressor.EstimateCompressionRatio(data);
    Assert.That(ratio, Is.GreaterThan(0.95), $"Random data should not compress well, got ratio={ratio:F3}");
  }

  [Test]
  [Category("Unit")]
  public void EstimateCompressionRatio_Empty_Returns1() {
    var ratio = PackBitsCompressor.EstimateCompressionRatio(ReadOnlySpan<byte>.Empty);
    Assert.That(ratio, Is.EqualTo(1.0));
  }
}
