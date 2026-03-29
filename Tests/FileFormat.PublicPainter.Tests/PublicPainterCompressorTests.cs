using System;
using FileFormat.PublicPainter;

namespace FileFormat.PublicPainter.Tests;

[TestFixture]
public sealed class PublicPainterCompressorTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllZeros() {
    var original = new byte[32000];
    var compressed = PublicPainterCompressor.Compress(original);
    var decompressed = PublicPainterCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllSameNonZero() {
    var original = new byte[32000];
    for (var i = 0; i < original.Length; ++i)
      original[i] = 0xAA;

    var compressed = PublicPainterCompressor.Compress(original);
    var decompressed = PublicPainterCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_MixedData() {
    var original = new byte[32000];
    var rng = new Random(42);
    rng.NextBytes(original);

    var compressed = PublicPainterCompressor.Compress(original);
    var decompressed = PublicPainterCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AlternatingBytes() {
    var original = new byte[32000];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i % 2 == 0 ? 0xAA : 0x55);

    var compressed = PublicPainterCompressor.Compress(original);
    var decompressed = PublicPainterCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void Compress_AllSame_SmallerThanOriginal() {
    var original = new byte[32000];
    for (var i = 0; i < original.Length; ++i)
      original[i] = 0xFF;

    var compressed = PublicPainterCompressor.Compress(original);

    Assert.That(compressed.Length, Is.LessThan(original.Length));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_SmallData() {
    var original = new byte[] { 0xAA, 0xAA, 0xAA, 0xBB, 0xCC, 0xDD };

    var compressed = PublicPainterCompressor.Compress(original);
    var decompressed = PublicPainterCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_SingleByte() {
    var original = new byte[] { 0x42 };

    var compressed = PublicPainterCompressor.Compress(original);
    var decompressed = PublicPainterCompressor.Decompress(compressed, original.Length);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_EmptyInput_ReturnsZeros() {
    var result = PublicPainterCompressor.Decompress(ReadOnlySpan<byte>.Empty, 10);

    Assert.That(result, Is.EqualTo(new byte[10]));
  }
}
