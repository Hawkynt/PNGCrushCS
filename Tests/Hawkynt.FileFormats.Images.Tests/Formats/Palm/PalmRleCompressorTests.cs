using System;
using FileFormat.Palm;

namespace FileFormat.Palm.Tests;

[TestFixture]
public sealed class PalmRleCompressorTests {

  [Test]
  [Category("Unit")]
  public void Compress_Decompress_RoundTrip() {
    var bytesPerRow = 8;
    var height = 4;
    var data = new byte[bytesPerRow * height];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 13);

    var compressed = PalmRleCompressor.Compress(data, bytesPerRow, height);
    var decompressed = PalmRleCompressor.Decompress(compressed, bytesPerRow, height);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void Compress_AllSame_CompressesSmaller() {
    var bytesPerRow = 16;
    var height = 8;
    var data = new byte[bytesPerRow * height];
    Array.Fill(data, (byte)42);

    var compressed = PalmRleCompressor.Compress(data, bytesPerRow, height);

    Assert.That(compressed.Length, Is.LessThan(data.Length));
  }

  [Test]
  [Category("Unit")]
  public void Compress_Mixed_DecompressesCorrectly() {
    var bytesPerRow = 10;
    var height = 3;
    var data = new byte[bytesPerRow * height];

    // Row 0: all same
    Array.Fill(data, (byte)0xAA, 0, bytesPerRow);
    // Row 1: alternating
    for (var i = 0; i < bytesPerRow; ++i)
      data[bytesPerRow + i] = (byte)(i % 2 == 0 ? 0x11 : 0x22);
    // Row 2: ramp
    for (var i = 0; i < bytesPerRow; ++i)
      data[bytesPerRow * 2 + i] = (byte)i;

    var compressed = PalmRleCompressor.Compress(data, bytesPerRow, height);
    var decompressed = PalmRleCompressor.Decompress(compressed, bytesPerRow, height);

    Assert.That(decompressed, Is.EqualTo(data));
  }
}
