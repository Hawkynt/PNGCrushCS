using System;
using NUnit.Framework;
using FileFormat.Tga;

namespace Optimizer.Tga.Tests;

[TestFixture]
public sealed class TgaRleCompressorTests {
  [Test]
  [Category("Unit")]
  public void Compress_Empty_ReturnsEmpty() {
    var result = TgaRleCompressor.Compress(ReadOnlySpan<byte>.Empty, 3);
    Assert.That(result.Length, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllSame_Rgb24_ReconstructsOriginal() {
    var original = new byte[64 * 3];
    for (var i = 0; i < 64; ++i) {
      original[i * 3] = 0xAA;
      original[i * 3 + 1] = 0xBB;
      original[i * 3 + 2] = 0xCC;
    }

    var compressed = TgaRleCompressor.Compress(original, 3);
    var decompressed = TgaRleCompressor.Decompress(compressed, 64, 3);

    Assert.That(decompressed, Is.EqualTo(original));
    Assert.That(compressed.Length, Is.LessThan(original.Length));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllDifferent_Rgb24_ReconstructsOriginal() {
    var original = new byte[32 * 3];
    for (var i = 0; i < 32; ++i) {
      original[i * 3] = (byte)(i * 8);
      original[i * 3 + 1] = (byte)(i * 4);
      original[i * 3 + 2] = (byte)(i * 2);
    }

    var compressed = TgaRleCompressor.Compress(original, 3);
    var decompressed = TgaRleCompressor.Decompress(compressed, 32, 3);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_SingleByte_Grayscale_ReconstructsOriginal() {
    var original = new byte[] { 42, 42, 42, 42, 100, 200, 50 };

    var compressed = TgaRleCompressor.Compress(original, 1);
    var decompressed = TgaRleCompressor.Decompress(compressed, 7, 1);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_Rgba32_ReconstructsOriginal() {
    var original = new byte[16 * 4];
    for (var i = 0; i < 16; ++i) {
      original[i * 4] = (byte)(i * 16);
      original[i * 4 + 1] = (byte)(i * 8);
      original[i * 4 + 2] = (byte)(i * 4);
      original[i * 4 + 3] = 255;
    }

    var compressed = TgaRleCompressor.Compress(original, 4);
    var decompressed = TgaRleCompressor.Decompress(compressed, 16, 4);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void EstimateCompressionRatio_AllSame_ReturnsLowRatio() {
    var data = new byte[3072]; // 1024 pixels * 3 bytes
    for (var i = 0; i < 1024; ++i) {
      data[i * 3] = 0xFF;
      data[i * 3 + 1] = 0xFF;
      data[i * 3 + 2] = 0xFF;
    }

    var ratio = TgaRleCompressor.EstimateCompressionRatio(data, 3);
    Assert.That(ratio, Is.LessThan(0.2));
  }

  [Test]
  [Category("Unit")]
  public void EstimateCompressionRatio_Empty_Returns1() {
    var ratio = TgaRleCompressor.EstimateCompressionRatio(ReadOnlySpan<byte>.Empty, 3);
    Assert.That(ratio, Is.EqualTo(1.0));
  }
}
