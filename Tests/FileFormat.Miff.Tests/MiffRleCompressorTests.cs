using System;
using FileFormat.Miff;

namespace FileFormat.Miff.Tests;

[TestFixture]
public sealed class MiffRleCompressorTests {

  [Test]
  [Category("Unit")]
  public void Compress_Decompress_RoundTrip_MixedData() {
    var data = new byte[90]; // 30 pixels * 3 bytes
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 17);

    var compressed = MiffRleCompressor.Compress(data, 3);
    var decompressed = MiffRleCompressor.Decompress(compressed, 3, 30);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void Compress_AllSame_CompressesSmaller() {
    // 100 identical 3-byte pixels = 300 bytes
    var data = new byte[300];
    for (var i = 0; i < data.Length; i += 3) {
      data[i] = 0xFF;
      data[i + 1] = 0x80;
      data[i + 2] = 0x40;
    }

    var compressed = MiffRleCompressor.Compress(data, 3);

    Assert.That(compressed.Length, Is.LessThan(data.Length));
  }

  [Test]
  [Category("Unit")]
  public void Compress_AllDifferent_RoundTrips() {
    // Each pixel is unique
    var data = new byte[30]; // 10 pixels * 3 bytes
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i * 9 % 256);

    var compressed = MiffRleCompressor.Compress(data, 3);
    var decompressed = MiffRleCompressor.Decompress(compressed, 3, 10);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_Empty_ReturnsEmpty() {
    var compressed = Array.Empty<byte>();
    var decompressed = MiffRleCompressor.Decompress(compressed, 3, 0);

    Assert.That(decompressed, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void Compress_SinglePixel_RoundTrips() {
    var data = new byte[] { 0xAA, 0xBB, 0xCC };

    var compressed = MiffRleCompressor.Compress(data, 3);
    var decompressed = MiffRleCompressor.Decompress(compressed, 3, 1);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void Compress_TwoIdenticalPixels_RoundTrips() {
    var data = new byte[] { 0x11, 0x22, 0x33, 0x11, 0x22, 0x33 };

    var compressed = MiffRleCompressor.Compress(data, 3);
    var decompressed = MiffRleCompressor.Decompress(compressed, 3, 2);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void Compress_SingleBytePixels_RoundTrips() {
    var data = new byte[] { 5, 5, 5, 5, 5, 7, 7, 3 };

    var compressed = MiffRleCompressor.Compress(data, 1);
    var decompressed = MiffRleCompressor.Decompress(compressed, 1, 8);

    Assert.That(decompressed, Is.EqualTo(data));
  }
}
