using System;
using FileFormat.IffRgb8;

namespace FileFormat.IffRgb8.Tests;

[TestFixture]
public sealed class Rgb8ByteRun1Tests {

  [Test]
  [Category("Unit")]
  public void Encode_EmptyInput_ReturnsEmpty() {
    var result = Rgb8ByteRun1Compressor.Encode(ReadOnlySpan<byte>.Empty);
    Assert.That(result, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void Decode_EmptyInput_ReturnsZeroFilledOutput() {
    var result = Rgb8ByteRun1Compressor.Decode(ReadOnlySpan<byte>.Empty, 8);
    Assert.That(result, Has.Length.EqualTo(8));
    Assert.That(result, Is.All.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllSameGroups() {
    // 4 identical 4-byte groups
    var data = new byte[16];
    for (var i = 0; i < 4; ++i) {
      data[i * 4] = 0xAA;
      data[i * 4 + 1] = 0xBB;
      data[i * 4 + 2] = 0xCC;
      data[i * 4 + 3] = 0x00;
    }

    var compressed = Rgb8ByteRun1Compressor.Encode(data);
    var decompressed = Rgb8ByteRun1Compressor.Decode(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllDifferentGroups() {
    var data = new byte[12]; // 3 groups of 4
    for (var i = 0; i < 12; ++i)
      data[i] = (byte)(i * 17 % 256);

    var compressed = Rgb8ByteRun1Compressor.Encode(data);
    var decompressed = Rgb8ByteRun1Compressor.Decode(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_MixedData() {
    // 8 groups: first 4 identical, then 4 all different
    var data = new byte[32];
    for (var i = 0; i < 4; ++i) {
      data[i * 4] = 0x11;
      data[i * 4 + 1] = 0x22;
      data[i * 4 + 2] = 0x33;
      data[i * 4 + 3] = 0x00;
    }
    for (var i = 4; i < 8; ++i) {
      data[i * 4] = (byte)(i * 10);
      data[i * 4 + 1] = (byte)(i * 20);
      data[i * 4 + 2] = (byte)(i * 30);
      data[i * 4 + 3] = 0x00;
    }

    var compressed = Rgb8ByteRun1Compressor.Encode(data);
    var decompressed = Rgb8ByteRun1Compressor.Decode(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void Encode_AllSameGroups_CompressesSmaller() {
    // 16 identical groups = should compress well
    var data = new byte[64];
    for (var i = 0; i < 16; ++i) {
      data[i * 4] = 0xFF;
      data[i * 4 + 1] = 0x00;
      data[i * 4 + 2] = 0x80;
      data[i * 4 + 3] = 0x00;
    }

    var compressed = Rgb8ByteRun1Compressor.Encode(data);
    Assert.That(compressed.Length, Is.LessThan(data.Length));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_SingleGroup() {
    var data = new byte[] { 0x12, 0x34, 0x56, 0x00 };

    var compressed = Rgb8ByteRun1Compressor.Encode(data);
    var decompressed = Rgb8ByteRun1Compressor.Decode(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_LargeData() {
    var groupCount = 256;
    var data = new byte[groupCount * 4];
    for (var i = 0; i < groupCount; ++i) {
      data[i * 4] = (byte)(i % 256);
      data[i * 4 + 1] = (byte)((i * 3) % 256);
      data[i * 4 + 2] = (byte)((i * 7) % 256);
      data[i * 4 + 3] = 0x00;
    }

    var compressed = Rgb8ByteRun1Compressor.Encode(data);
    var decompressed = Rgb8ByteRun1Compressor.Decode(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }
}
