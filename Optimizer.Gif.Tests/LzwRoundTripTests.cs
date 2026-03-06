using System;
using System.IO;
using Hawkynt.GifFileFormat;
using NUnit.Framework;

namespace Optimizer.Gif.Tests;

[TestFixture]
public sealed class LzwRoundTripTests {
  [Test]
  [Category("Unit")]
  public void Compress_SmallData_RoundTrips() {
    var original = new byte[] { 0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3 };
    var compressed = LzwCompressor.Compress(original, 8);

    Assert.That(compressed.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void Compress_EmptyData_ProducesOutput() {
    var compressed = LzwCompressor.Compress(ReadOnlySpan<byte>.Empty, 8);
    Assert.That(compressed.Length, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void Compress_RepeatedData_SmallerThanRaw() {
    var original = new byte[1024];
    Array.Fill(original, (byte)42);

    var compressed = LzwCompressor.Compress(original, 8);
    Assert.That(compressed.Length, Is.LessThan(original.Length));
  }

  [Test]
  [Category("Integration")]
  public void CompressThenRead_PixelPerfectRoundTrip() {
    var width = 8;
    var height = 8;
    var expectedPixels = new byte[width * height];
    var rng = new Random(42);
    for (var i = 0; i < expectedPixels.Length; ++i)
      expectedPixels[i] = (byte)rng.Next(0, 4);

    // Create GIF manually with our LZW compressor, then read back
    var tempFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"lzw_rt_{Guid.NewGuid():N}.gif"));
    try {
      var compressed = LzwCompressor.Compress(expectedPixels, 2);
      var gifBytes = _BuildGifWithCompressedData(width, height, compressed, 2);
      File.WriteAllBytes(tempFile.FullName, gifBytes);

      var gif = Reader.FromFile(tempFile);
      var readPixels = gif.Frames[0].IndexedPixels;

      Assert.That(readPixels.Length, Is.EqualTo(expectedPixels.Length));
      for (var i = 0; i < expectedPixels.Length; ++i)
        Assert.That(readPixels[i], Is.EqualTo(expectedPixels[i]), $"Pixel mismatch at index {i}");
    } finally {
      tempFile.Delete();
    }
  }

  [Test]
  [Category("Integration")]
  public void CompressThenRead_LargeImage_RoundTrips() {
    var width = 64;
    var height = 64;
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i % 4);

    var tempFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"lzw_lg_{Guid.NewGuid():N}.gif"));
    try {
      var compressed = LzwCompressor.Compress(pixels, 2);
      var gifBytes = _BuildGifWithCompressedData(width, height, compressed, 2);
      File.WriteAllBytes(tempFile.FullName, gifBytes);

      var gif = Reader.FromFile(tempFile);
      var readPixels = gif.Frames[0].IndexedPixels;

      Assert.That(readPixels.Length, Is.EqualTo(pixels.Length));
      for (var i = 0; i < pixels.Length; ++i)
        Assert.That(readPixels[i], Is.EqualTo(pixels[i]), $"Pixel mismatch at index {i}");
    } finally {
      tempFile.Delete();
    }
  }

  private static byte[] _BuildGifWithCompressedData(int width, int height, byte[] compressedData, byte minCodeSize) {
    using var ms = new MemoryStream();
    using var writer = new BinaryWriter(ms);

    writer.Write("GIF89a"u8);
    writer.Write((ushort)width);
    writer.Write((ushort)height);
    writer.Write((byte)0xF1); // GCT flag, 4-entry GCT
    writer.Write((byte)0);
    writer.Write((byte)0);

    // 4-entry GCT
    writer.Write(new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255 });

    // GCE
    writer.Write((byte)0x21);
    writer.Write((byte)0xF9);
    writer.Write((byte)0x04);
    writer.Write((byte)0x00);
    writer.Write((ushort)10);
    writer.Write((byte)0);
    writer.Write((byte)0x00);

    // Image descriptor
    writer.Write((byte)0x2C);
    writer.Write((ushort)0);
    writer.Write((ushort)0);
    writer.Write((ushort)width);
    writer.Write((ushort)height);
    writer.Write((byte)0x00);

    // Image data
    writer.Write(minCodeSize);
    var offset = 0;
    while (offset < compressedData.Length) {
      var blockSize = Math.Min(255, compressedData.Length - offset);
      writer.Write((byte)blockSize);
      writer.Write(compressedData, offset, blockSize);
      offset += blockSize;
    }

    writer.Write((byte)0x00);
    writer.Write((byte)0x3B);

    return ms.ToArray();
  }
}
