using System;
using FileFormat.Rla;

namespace FileFormat.Rla.Tests;

[TestFixture]
public sealed class RlaRleCompressorTests {

  [Test]
  [Category("Unit")]
  public void Compress_Decompress_RoundTrip() {
    var scanline = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 };

    var compressed = RlaRleCompressor.Compress(scanline);
    var decompressed = RlaRleCompressor.Decompress(compressed, scanline.Length);

    Assert.That(decompressed, Is.EqualTo(scanline));
  }

  [Test]
  [Category("Unit")]
  public void Compress_AllSameData_CompressesWell() {
    var scanline = new byte[128];
    Array.Fill(scanline, (byte)42);

    var compressed = RlaRleCompressor.Compress(scanline);
    var decompressed = RlaRleCompressor.Decompress(compressed, scanline.Length);

    Assert.That(compressed.Length, Is.LessThan(scanline.Length));
    Assert.That(decompressed, Is.EqualTo(scanline));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_Literals_DecodesCorrectly() {
    // Control byte 2 (= 3 literal bytes follow)
    var encoded = new byte[] { 2, 0xAA, 0xBB, 0xCC };
    var decoded = RlaRleCompressor.Decompress(encoded, 3);

    Assert.That(decoded[0], Is.EqualTo(0xAA));
    Assert.That(decoded[1], Is.EqualTo(0xBB));
    Assert.That(decoded[2], Is.EqualTo(0xCC));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_Runs_DecodesCorrectly() {
    // Control byte 253 means run of (257-253) = 4 copies of next byte
    var encoded = new byte[] { 253, 0xFF };
    var decoded = RlaRleCompressor.Decompress(encoded, 4);

    Assert.That(decoded, Is.EqualTo(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }));
  }

  [Test]
  [Category("Unit")]
  public void Compress_Empty_ReturnsEmpty() {
    var scanline = Array.Empty<byte>();

    var compressed = RlaRleCompressor.Compress(scanline);

    Assert.That(compressed, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void Compress_MixedData_RoundTrips() {
    var scanline = new byte[256];
    for (var i = 0; i < scanline.Length; ++i)
      scanline[i] = (byte)(i % 13);

    var compressed = RlaRleCompressor.Compress(scanline);
    var decompressed = RlaRleCompressor.Decompress(compressed, scanline.Length);

    Assert.That(decompressed, Is.EqualTo(scanline));
  }

  [Test]
  [Category("Unit")]
  public void Compress_SingleByte_RoundTrips() {
    var scanline = new byte[] { 99 };

    var compressed = RlaRleCompressor.Compress(scanline);
    var decompressed = RlaRleCompressor.Decompress(compressed, 1);

    Assert.That(decompressed[0], Is.EqualTo(99));
  }

  [Test]
  [Category("Unit")]
  public void Compress_LargeRun_RoundTrips() {
    var scanline = new byte[512];
    Array.Fill(scanline, (byte)0xAB);

    var compressed = RlaRleCompressor.Compress(scanline);
    var decompressed = RlaRleCompressor.Decompress(compressed, scanline.Length);

    Assert.That(compressed.Length, Is.LessThan(scanline.Length));
    Assert.That(decompressed, Is.EqualTo(scanline));
  }
}
