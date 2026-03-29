using System;
using FileFormat.Wpg;

namespace FileFormat.Wpg.Tests;

[TestFixture]
public sealed class WpgRleCompressorTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_Empty_ReturnsEmpty() {
    var data = Array.Empty<byte>();

    var compressed = WpgRleCompressor.Compress(data);

    Assert.That(compressed, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllSame_DecompressesCorrectly() {
    var data = new byte[200];
    Array.Fill(data, (byte)42);

    var compressed = WpgRleCompressor.Compress(data);
    var decompressed = WpgRleCompressor.Decompress(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void Compress_AllSame_CompressesSmaller() {
    var data = new byte[200];
    Array.Fill(data, (byte)99);

    var compressed = WpgRleCompressor.Compress(data);

    Assert.That(compressed.Length, Is.LessThan(data.Length));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_MixedData_DecompressesCorrectly() {
    var data = new byte[256];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 17);

    var compressed = WpgRleCompressor.Compress(data);
    var decompressed = WpgRleCompressor.Decompress(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_LargeRun_DecompressesCorrectly() {
    var data = new byte[500];
    Array.Fill(data, (byte)0xAB);

    var compressed = WpgRleCompressor.Compress(data);
    var decompressed = WpgRleCompressor.Decompress(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }
}
