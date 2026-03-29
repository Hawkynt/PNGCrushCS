using System;
using FileFormat.Tiny;

namespace FileFormat.Tiny.Tests;

[TestFixture]
public sealed class TinyCompressorTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllZeros() {
    var original = new byte[32000];
    var compressed = TinyCompressor.Compress(original, 4, 4000);
    var decompressed = TinyCompressor.Decompress(compressed, 4, 4000);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllSame() {
    var original = new byte[32000];
    for (var i = 0; i < original.Length; i += 2) {
      original[i] = 0x12;
      original[i + 1] = 0x34;
    }

    var compressed = TinyCompressor.Compress(original, 4, 4000);
    var decompressed = TinyCompressor.Decompress(compressed, 4, 4000);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_MixedData() {
    var original = new byte[32000];
    for (var i = 0; i < original.Length; i += 2) {
      original[i] = (byte)((i / 2) >> 8 & 0xFF);
      original[i + 1] = (byte)((i / 2) & 0xFF);
    }

    var compressed = TinyCompressor.Compress(original, 4, 4000);
    var decompressed = TinyCompressor.Decompress(compressed, 4, 4000);

    Assert.That(decompressed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_LargeData_SinglePlane() {
    var original = new byte[32000];
    for (var i = 0; i < original.Length; i += 2) {
      original[i] = (byte)(i % 7);
      original[i + 1] = (byte)(i % 13);
    }

    var compressed = TinyCompressor.Compress(original, 1, 16000);
    var decompressed = TinyCompressor.Decompress(compressed, 1, 16000);

    Assert.That(decompressed, Is.EqualTo(original));
  }
}
