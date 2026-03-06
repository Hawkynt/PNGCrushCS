using System;
using FileFormat.Bmp;
using NUnit.Framework;

namespace Optimizer.Bmp.Tests;

[TestFixture]
public sealed class RleCompressorTests {
  [Test]
  [Category("Unit")]
  public void CompressRle8_AllSame_CompressesWell() {
    var original = new byte[128];
    Array.Fill(original, (byte)42);

    var compressed = RleCompressor.CompressRle8(original);

    // Encoded run + end-of-line marker
    Assert.That(compressed.Length, Is.LessThan(original.Length));
  }

  [Test]
  [Category("Unit")]
  public void CompressRle8_Empty_ReturnsEmpty() {
    var result = RleCompressor.CompressRle8(ReadOnlySpan<byte>.Empty);
    Assert.That(result.Length, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void DecompressRle8_RoundTrip_AllSame() {
    var original = new byte[64];
    Array.Fill(original, (byte)0xAA);

    var compressed = RleCompressor.CompressRle8(original);
    var decompressed = RleCompressor.DecompressRle8(compressed, 64, 1);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void DecompressRle8_RoundTrip_Sequential() {
    var original = new byte[64];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i % 256);

    var compressed = RleCompressor.CompressRle8(original);
    var decompressed = RleCompressor.DecompressRle8(compressed, 64, 1);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void DecompressRle8_RoundTrip_Mixed() {
    var original = new byte[] { 1, 1, 1, 1, 2, 3, 4, 5, 5, 5, 5, 5, 6, 7, 8, 9 };

    var compressed = RleCompressor.CompressRle8(original);
    var decompressed = RleCompressor.DecompressRle8(compressed, 16, 1);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void EstimateCompressionRatio_AllSame_ReturnsLowRatio() {
    var data = new byte[4096];
    Array.Fill(data, (byte)0xBB);

    var ratio = RleCompressor.EstimateCompressionRatio(data);
    Assert.That(ratio, Is.LessThan(0.1));
  }

  [Test]
  [Category("Unit")]
  public void EstimateCompressionRatio_RandomData_ReturnsHighRatio() {
    var rng = new Random(42);
    var data = new byte[4096];
    rng.NextBytes(data);

    var ratio = RleCompressor.EstimateCompressionRatio(data);
    Assert.That(ratio, Is.GreaterThan(0.9));
  }

  [Test]
  [Category("Unit")]
  public void EstimateCompressionRatio_Empty_Returns1() {
    var ratio = RleCompressor.EstimateCompressionRatio(ReadOnlySpan<byte>.Empty);
    Assert.That(ratio, Is.EqualTo(1.0));
  }

  [Test]
  [Category("Unit")]
  public void CompressRle4_SmallData_ProducesOutput() {
    var indices = new byte[] { 0, 0, 0, 0, 1, 1, 1, 1 };
    var compressed = RleCompressor.CompressRle4(indices, 8);

    Assert.That(compressed.Length, Is.GreaterThan(0));
  }
}
