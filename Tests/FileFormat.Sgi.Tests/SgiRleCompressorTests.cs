using System;
using FileFormat.Sgi;

namespace FileFormat.Sgi.Tests;

[TestFixture]
public sealed class SgiRleCompressorTests {

  [Test]
  [Category("Unit")]
  public void Compress_Decompress_RoundTrip() {
    var scanline = new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 };

    var compressed = SgiRleCompressor.Compress(scanline);
    var decompressed = SgiRleCompressor.Decompress(compressed, 0, compressed.Length, scanline.Length);

    Assert.That(decompressed, Is.EqualTo(scanline));
  }

  [Test]
  [Category("Unit")]
  public void Compress_AllSameData_CompressesWell() {
    var scanline = new byte[128];
    Array.Fill(scanline, (byte)42);

    var compressed = SgiRleCompressor.Compress(scanline);
    var decompressed = SgiRleCompressor.Decompress(compressed, 0, compressed.Length, scanline.Length);

    Assert.That(compressed.Length, Is.LessThan(scanline.Length));
    Assert.That(decompressed, Is.EqualTo(scanline));
  }

  [Test]
  [Category("Unit")]
  public void Compress_MixedData_RoundTrips() {
    var scanline = new byte[256];
    for (var i = 0; i < scanline.Length; ++i)
      scanline[i] = (byte)(i % 13);

    var compressed = SgiRleCompressor.Compress(scanline);
    var decompressed = SgiRleCompressor.Decompress(compressed, 0, compressed.Length, scanline.Length);

    Assert.That(decompressed, Is.EqualTo(scanline));
  }

  [Test]
  [Category("Unit")]
  public void Compress_EmptyScanline_ProducesTerminator() {
    var scanline = Array.Empty<byte>();

    var compressed = SgiRleCompressor.Compress(scanline);

    // Should contain just the zero terminator
    Assert.That(compressed, Is.Not.Empty);
    Assert.That(compressed[0], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Compress_SingleByte_RoundTrips() {
    var scanline = new byte[] { 99 };

    var compressed = SgiRleCompressor.Compress(scanline);
    var decompressed = SgiRleCompressor.Decompress(compressed, 0, compressed.Length, 1);

    Assert.That(decompressed[0], Is.EqualTo(99));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_WithOffset_ReadsFromCorrectPosition() {
    var scanline = new byte[] { 1, 2, 3, 4, 5 };
    var compressed = SgiRleCompressor.Compress(scanline);

    // Embed compressed data at an offset
    var wrapper = new byte[10 + compressed.Length];
    Array.Copy(compressed, 0, wrapper, 10, compressed.Length);

    var decompressed = SgiRleCompressor.Decompress(wrapper, 10, compressed.Length, scanline.Length);

    Assert.That(decompressed, Is.EqualTo(scanline));
  }
}
