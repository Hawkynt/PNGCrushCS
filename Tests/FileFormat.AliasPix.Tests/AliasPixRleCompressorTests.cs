using System;
using FileFormat.AliasPix;

namespace FileFormat.AliasPix.Tests;

[TestFixture]
public sealed class AliasPixRleCompressorTests {

  [Test]
  [Category("Unit")]
  public void Compress_Decompress_RoundTrip_24bpp() {
    var width = 4;
    var height = 2;
    var bytesPerPixel = 3;
    var pixelData = new byte[width * height * bytesPerPixel];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 7 % 256);

    var compressed = AliasPixRleCompressor.Compress(pixelData, width, height, bytesPerPixel);
    var decompressed = AliasPixRleCompressor.Decompress(compressed, width, height, bytesPerPixel);

    Assert.That(decompressed, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void Compress_Decompress_RoundTrip_32bpp() {
    var width = 3;
    var height = 2;
    var bytesPerPixel = 4;
    var pixelData = new byte[width * height * bytesPerPixel];
    for (var i = 0; i < pixelData.Length; ++i)
      pixelData[i] = (byte)(i * 13 % 256);

    var compressed = AliasPixRleCompressor.Compress(pixelData, width, height, bytesPerPixel);
    var decompressed = AliasPixRleCompressor.Decompress(compressed, width, height, bytesPerPixel);

    Assert.That(decompressed, Is.EqualTo(pixelData));
  }

  [Test]
  [Category("Unit")]
  public void Compress_AllSamePixels_CompressesSmaller() {
    var width = 100;
    var height = 1;
    var bytesPerPixel = 3;
    var pixelData = new byte[width * height * bytesPerPixel];
    for (var i = 0; i < pixelData.Length; i += bytesPerPixel) {
      pixelData[i] = 0x42;
      pixelData[i + 1] = 0x84;
      pixelData[i + 2] = 0xC6;
    }

    var compressed = AliasPixRleCompressor.Compress(pixelData, width, height, bytesPerPixel);

    Assert.That(compressed.Length, Is.LessThan(pixelData.Length));
  }

  [Test]
  [Category("Unit")]
  public void Compress_Empty_ReturnsEmpty() {
    var compressed = AliasPixRleCompressor.Compress([], 0, 0, 3);

    Assert.That(compressed, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void Compress_LongRun_SplitsAt255() {
    var width = 300;
    var height = 1;
    var bytesPerPixel = 3;
    var pixelData = new byte[width * height * bytesPerPixel];
    Array.Fill(pixelData, (byte)0xAB);

    var compressed = AliasPixRleCompressor.Compress(pixelData, width, height, bytesPerPixel);
    var decompressed = AliasPixRleCompressor.Decompress(compressed, width, height, bytesPerPixel);

    Assert.That(decompressed, Is.EqualTo(pixelData));
  }
}
