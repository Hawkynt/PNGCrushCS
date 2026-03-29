using System;
using FileFormat.Msp;

namespace FileFormat.Msp.Tests;

[TestFixture]
public sealed class MspRleCompressorTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_Empty_ReturnsEmpty() {
    var data = Array.Empty<byte>();

    var compressed = MspRleCompressor.Compress(data);

    Assert.That(compressed, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllSame_DecompressesCorrectly() {
    var data = new byte[64];
    Array.Fill(data, (byte)0xFF);

    var compressed = MspRleCompressor.Compress(data);
    var decompressed = MspRleCompressor.Decompress(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void Compress_AllSame_CompressesSmaller() {
    var data = new byte[64];
    Array.Fill(data, (byte)0xFF);

    var compressed = MspRleCompressor.Compress(data);

    Assert.That(compressed.Length, Is.LessThan(data.Length));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_MixedData_DecompressesCorrectly() {
    var data = new byte[128];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 17);

    var compressed = MspRleCompressor.Compress(data);
    var decompressed = MspRleCompressor.Decompress(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_LargeRun_DecompressesCorrectly() {
    var data = new byte[512];
    Array.Fill(data, (byte)0xAA);

    var compressed = MspRleCompressor.Compress(data);
    var decompressed = MspRleCompressor.Decompress(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }
}
