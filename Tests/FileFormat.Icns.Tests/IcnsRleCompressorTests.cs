using System;
using FileFormat.Icns;

namespace FileFormat.Icns.Tests;

[TestFixture]
public sealed class IcnsRleCompressorTests {

  [Test]
  [Category("Unit")]
  public void Decompress_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IcnsRleCompressor.Decompress(null!, 10));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_ZeroPixelCount_ThrowsArgumentOutOfRangeException() {
    Assert.Throws<ArgumentOutOfRangeException>(() => IcnsRleCompressor.Decompress(new byte[10], 0));
  }

  [Test]
  [Category("Unit")]
  public void Compress_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IcnsRleCompressor.Compress(null!, 10));
  }

  [Test]
  [Category("Unit")]
  public void Compress_ZeroPixelCount_ThrowsArgumentOutOfRangeException() {
    Assert.Throws<ArgumentOutOfRangeException>(() => IcnsRleCompressor.Compress(new byte[10], 0));
  }

  [Test]
  [Category("Unit")]
  public void Compress_DataTooShort_ThrowsArgumentException() {
    Assert.Throws<ArgumentException>(() => IcnsRleCompressor.Compress(new byte[5], 4));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllSameColor_DataPreserved() {
    var pixelCount = 16;
    var rgb = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      rgb[i * 3] = 200;     // R
      rgb[i * 3 + 1] = 100; // G
      rgb[i * 3 + 2] = 50;  // B
    }

    var compressed = IcnsRleCompressor.Compress(rgb, pixelCount);
    var decompressed = IcnsRleCompressor.Decompress(compressed, pixelCount);

    Assert.That(decompressed, Is.EqualTo(rgb));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllSameColor_CompressesSmaller() {
    var pixelCount = 64;
    var rgb = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      rgb[i * 3] = 42;
      rgb[i * 3 + 1] = 84;
      rgb[i * 3 + 2] = 126;
    }

    var compressed = IcnsRleCompressor.Compress(rgb, pixelCount);

    Assert.That(compressed.Length, Is.LessThan(rgb.Length));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_MixedData_DataPreserved() {
    var pixelCount = 16;
    var rgb = new byte[pixelCount * 3];
    for (var i = 0; i < rgb.Length; ++i)
      rgb[i] = (byte)(i * 17 % 256);

    var compressed = IcnsRleCompressor.Compress(rgb, pixelCount);
    var decompressed = IcnsRleCompressor.Decompress(compressed, pixelCount);

    Assert.That(decompressed, Is.EqualTo(rgb));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_GradientData_DataPreserved() {
    var pixelCount = 32;
    var rgb = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      rgb[i * 3] = (byte)(i * 8);
      rgb[i * 3 + 1] = (byte)(255 - i * 8);
      rgb[i * 3 + 2] = (byte)(i * 4);
    }

    var compressed = IcnsRleCompressor.Compress(rgb, pixelCount);
    var decompressed = IcnsRleCompressor.Decompress(compressed, pixelCount);

    Assert.That(decompressed, Is.EqualTo(rgb));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_SinglePixel_DataPreserved() {
    var rgb = new byte[] { 10, 20, 30 };
    var compressed = IcnsRleCompressor.Compress(rgb, 1);
    var decompressed = IcnsRleCompressor.Decompress(compressed, 1);

    Assert.That(decompressed, Is.EqualTo(rgb));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_LargeImage_DataPreserved() {
    var pixelCount = 128 * 128;
    var rgb = new byte[pixelCount * 3];
    for (var i = 0; i < rgb.Length; ++i)
      rgb[i] = (byte)(i % 256);

    var compressed = IcnsRleCompressor.Compress(rgb, pixelCount);
    var decompressed = IcnsRleCompressor.Decompress(compressed, pixelCount);

    Assert.That(decompressed, Is.EqualTo(rgb));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AlternatingRunsAndLiterals_DataPreserved() {
    var pixelCount = 20;
    var rgb = new byte[pixelCount * 3];
    // First 8 pixels: same R channel
    for (var i = 0; i < 8; ++i) {
      rgb[i * 3] = 100;
      rgb[i * 3 + 1] = (byte)(i * 10);
      rgb[i * 3 + 2] = (byte)(i * 20);
    }
    // Next 12 pixels: different R channel
    for (var i = 8; i < 20; ++i) {
      rgb[i * 3] = (byte)(i * 7);
      rgb[i * 3 + 1] = 50;
      rgb[i * 3 + 2] = 25;
    }

    var compressed = IcnsRleCompressor.Compress(rgb, pixelCount);
    var decompressed = IcnsRleCompressor.Decompress(compressed, pixelCount);

    Assert.That(decompressed, Is.EqualTo(rgb));
  }

  [Test]
  [Category("Unit")]
  public void Decompress_OutputLength_MatchesPixelCountTimes3() {
    var pixelCount = 10;
    var rgb = new byte[pixelCount * 3];
    var compressed = IcnsRleCompressor.Compress(rgb, pixelCount);
    var decompressed = IcnsRleCompressor.Decompress(compressed, pixelCount);

    Assert.That(decompressed.Length, Is.EqualTo(pixelCount * 3));
  }
}
