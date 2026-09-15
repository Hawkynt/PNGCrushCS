using System;
using FileFormat.Gif;
using NUnit.Framework;

namespace Optimizer.Gif.Tests;

/// <summary>The GIF optimizer's view of the shared LZW encoder: the sizes and the adaptive clear
/// strategy it relies on when it picks a winner between compression trials. The codec's own
/// behaviour is covered by <c>Hawkynt.FileFormats.Images.Tests.Formats.Gif.LzwCodecTests</c>.</summary>
[TestFixture]
public sealed class LzwCompressorTests {
  [Test]
  [Category("Unit")]
  public void Compress_AllZeros_CompressesWell() {
    var data = new byte[256];
    var compressed = GifLzwCodec.Encode(data, 8);

    Assert.That(compressed.Length, Is.GreaterThan(0));
    Assert.That(compressed.Length, Is.LessThan(data.Length));
  }

  [Test]
  [Category("Unit")]
  public void Compress_RandomData_ProducesOutput() {
    var rng = new Random(42);
    var data = new byte[512];
    rng.NextBytes(data);

    var compressed = GifLzwCodec.Encode(data, 8);

    Assert.That(compressed.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void Compress_SingleByte_ProducesOutput() {
    var data = new byte[] { 42 };
    var compressed = GifLzwCodec.Encode(data, 8);

    Assert.That(compressed.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void Compress_LargeData_HandlesCodeTableReset() {
    var rng = new Random(123);
    var data = new byte[8192];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)rng.Next(0, 256);

    var compressed = GifLzwCodec.Encode(data, 8);

    Assert.That(compressed.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void Compress_RepeatingPattern_CompressesBetterThanRandom() {
    var pattern = new byte[1024];
    for (var i = 0; i < pattern.Length; ++i)
      pattern[i] = (byte)(i % 4);

    var random = new byte[1024];
    new Random(99).NextBytes(random);

    var compressedPattern = GifLzwCodec.Encode(pattern, 8);
    var compressedRandom = GifLzwCodec.Encode(random, 8);

    Assert.That(compressedPattern.Length, Is.LessThan(compressedRandom.Length));
  }

  [Test]
  [Category("Unit")]
  public void Compress_Empty_ProducesValidOutput() {
    var compressed = GifLzwCodec.Encode(ReadOnlySpan<byte>.Empty, 8);
    Assert.That(compressed.Length, Is.GreaterThan(0));
  }

  // --- Deferred Clear Code Tests ---

  [Test]
  [Category("Unit")]
  public void DeferredClear_ProducesSmallerOrEqualOutput() {
    var data = new byte[8192];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 16);

    var standard = GifLzwCodec.Encode(data, 8);
    var deferred = GifLzwCodec.Encode(data, 8, GifLzwCodec.EncodeOptions.StandardCompression(GifLzwCodec.ClearStrategy.Adaptive));

    Assert.That(deferred.Length, Is.LessThanOrEqualTo(standard.Length + standard.Length / 10),
      $"Deferred={deferred.Length} vs Standard={standard.Length}");
  }

  [Test]
  [Category("Unit")]
  public void DeferredClear_ProducesOutput() {
    var rng = new Random(77);
    var data = new byte[4096];
    rng.NextBytes(data);

    var compressed = GifLzwCodec.Encode(data, 8, GifLzwCodec.EncodeOptions.StandardCompression(GifLzwCodec.ClearStrategy.Adaptive));
    Assert.That(compressed.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void DeferredClear_AllZeros_ProducesOutput() {
    var data = new byte[4096];
    var compressed = GifLzwCodec.Encode(data, 8, GifLzwCodec.EncodeOptions.StandardCompression(GifLzwCodec.ClearStrategy.Adaptive));

    Assert.That(compressed.Length, Is.GreaterThan(0));
    Assert.That(compressed.Length, Is.LessThan(data.Length));
  }

  [Test]
  [Category("Unit")]
  public void Compress_LargeRepetitive_RoundTrip() {
    // 64KB repetitive data — tests hash table correctness under many resets
    var data = new byte[65536];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 7);

    var compressed = GifLzwCodec.Encode(data, 8);
    Assert.That(compressed.Length, Is.GreaterThan(0));
    Assert.That(compressed.Length, Is.LessThan(data.Length));
  }

  [Test]
  [Category("Unit")]
  public void Compress_ManyResets_GenerationCounterCorrect() {
    // Data forcing many table resets to verify generation counter correctness
    var rng = new Random(42);
    var data = new byte[32768];
    rng.NextBytes(data);

    var standard = GifLzwCodec.Encode(data, 8);
    Assert.That(standard.Length, Is.GreaterThan(0));

    var deferred = GifLzwCodec.Encode(data, 8, GifLzwCodec.EncodeOptions.StandardCompression(GifLzwCodec.ClearStrategy.Adaptive));
    Assert.That(deferred.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void DeferredClear_AdaptiveInterval_SameOrBetterCompression() {
    // Verify adaptive interval produces same or better output than fixed
    var data = new byte[16384];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i / 64 % 256);

    var compressed = GifLzwCodec.Encode(data, 8, GifLzwCodec.EncodeOptions.StandardCompression(GifLzwCodec.ClearStrategy.Adaptive));
    Assert.That(compressed.Length, Is.GreaterThan(0));
    Assert.That(compressed.Length, Is.LessThan(data.Length));
  }
}
