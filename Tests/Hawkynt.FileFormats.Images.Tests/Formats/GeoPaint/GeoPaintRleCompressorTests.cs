using System;
using FileFormat.GeoPaint;

namespace FileFormat.GeoPaint.Tests;

[TestFixture]
public sealed class GeoPaintRleCompressorTests {

  [Test]
  [Category("Unit")]
  public void CompressScanline_Empty_ReturnsEndMarker() {
    var result = GeoPaintRleCompressor.CompressScanline([]);

    Assert.That(result.Length, Is.EqualTo(1));
    Assert.That(result[0], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllZeros() {
    var scanline = new byte[80];
    var compressed = GeoPaintRleCompressor.CompressScanline(scanline);
    var offset = 0;
    var decompressed = GeoPaintRleCompressor.DecompressScanline(compressed, ref offset, 80, out _);

    Assert.That(decompressed, Is.EqualTo(scanline));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllSameNonZero() {
    var scanline = new byte[80];
    Array.Fill(scanline, (byte)0xAB);
    var compressed = GeoPaintRleCompressor.CompressScanline(scanline);
    var offset = 0;
    var decompressed = GeoPaintRleCompressor.DecompressScanline(compressed, ref offset, 80, out _);

    Assert.That(decompressed, Is.EqualTo(scanline));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_MixedData() {
    var scanline = new byte[80];
    for (var i = 0; i < 80; ++i)
      scanline[i] = (byte)(i * 3 % 256);

    var compressed = GeoPaintRleCompressor.CompressScanline(scanline);
    var offset = 0;
    var decompressed = GeoPaintRleCompressor.DecompressScanline(compressed, ref offset, 80, out _);

    Assert.That(decompressed, Is.EqualTo(scanline));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AlternatingBytes() {
    var scanline = new byte[80];
    for (var i = 0; i < 80; ++i)
      scanline[i] = (byte)(i % 2 == 0 ? 0xAA : 0x55);

    var compressed = GeoPaintRleCompressor.CompressScanline(scanline);
    var offset = 0;
    var decompressed = GeoPaintRleCompressor.DecompressScanline(compressed, ref offset, 80, out _);

    Assert.That(decompressed, Is.EqualTo(scanline));
  }

  [Test]
  [Category("Unit")]
  public void CompressScanline_AllZeros_UsesZeroRunEncoding() {
    var scanline = new byte[80];
    var compressed = GeoPaintRleCompressor.CompressScanline(scanline);

    // Should be much smaller than 80 bytes
    Assert.That(compressed.Length, Is.LessThan(10));
    // Last byte should be end marker
    Assert.That(compressed[^1], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void CompressScanline_AllSame_UsesRepeatRunEncoding() {
    var scanline = new byte[80];
    Array.Fill(scanline, (byte)0xCC);
    var compressed = GeoPaintRleCompressor.CompressScanline(scanline);

    // Should be much smaller than 80 bytes
    Assert.That(compressed.Length, Is.LessThan(10));
    Assert.That(compressed[^1], Is.EqualTo(0xFF));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_SingleByte() {
    var scanline = new byte[] { 0x42 };
    var compressed = GeoPaintRleCompressor.CompressScanline(scanline);
    var offset = 0;
    var decompressed = GeoPaintRleCompressor.DecompressScanline(compressed, ref offset, 1, out _);

    Assert.That(decompressed[0], Is.EqualTo(0x42));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_ZeroThenNonZero() {
    var scanline = new byte[80];
    // First 40 bytes zero, then 40 bytes 0xBB
    Array.Fill(scanline, (byte)0xBB, 40, 40);
    var compressed = GeoPaintRleCompressor.CompressScanline(scanline);
    var offset = 0;
    var decompressed = GeoPaintRleCompressor.DecompressScanline(compressed, ref offset, 80, out _);

    Assert.That(decompressed, Is.EqualTo(scanline));
  }

  [Test]
  [Category("Unit")]
  public void Compress_Decompress_MultiRow() {
    var height = 5;
    var pixelData = new byte[80 * height];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var compressed = GeoPaintRleCompressor.Compress(pixelData, height);
    var decompressed = GeoPaintRleCompressor.Decompress(compressed, height);

    Assert.That(decompressed, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void Compress_Decompress_AllZeroRows() {
    var height = 10;
    var pixelData = new byte[80 * height];

    var compressed = GeoPaintRleCompressor.Compress(pixelData, height);
    var decompressed = GeoPaintRleCompressor.Decompress(compressed, height);

    Assert.That(decompressed, Is.EqualTo(pixelData));
    // All-zero data should compress well
    Assert.That(compressed.Length, Is.LessThan(pixelData.Length));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_TwoIdenticalBytes() {
    // Two identical non-zero bytes should still round-trip (below repeat threshold of 3)
    var scanline = new byte[] { 0xAA, 0xAA };
    var compressed = GeoPaintRleCompressor.CompressScanline(scanline);
    var offset = 0;
    var decompressed = GeoPaintRleCompressor.DecompressScanline(compressed, ref offset, 2, out _);

    Assert.That(decompressed[0], Is.EqualTo(0xAA));
    Assert.That(decompressed[1], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_ThreeIdenticalBytes() {
    // Three identical non-zero bytes should use repeat encoding
    var scanline = new byte[] { 0xBB, 0xBB, 0xBB };
    var compressed = GeoPaintRleCompressor.CompressScanline(scanline);
    var offset = 0;
    var decompressed = GeoPaintRleCompressor.DecompressScanline(compressed, ref offset, 3, out _);

    Assert.That(decompressed[0], Is.EqualTo(0xBB));
    Assert.That(decompressed[1], Is.EqualTo(0xBB));
    Assert.That(decompressed[2], Is.EqualTo(0xBB));
  }
}
