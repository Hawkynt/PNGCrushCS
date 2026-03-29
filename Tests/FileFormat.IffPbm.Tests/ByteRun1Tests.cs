using System;
using FileFormat.IffPbm;

namespace FileFormat.IffPbm.Tests;

[TestFixture]
public sealed class ByteRun1Tests {

  [Test]
  [Category("Unit")]
  public void Encode_EmptyData_ReturnsEmpty() {
    var result = ByteRun1Compressor.Encode(ReadOnlySpan<byte>.Empty);
    Assert.That(result, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllSameBytes() {
    var data = new byte[200];
    Array.Fill(data, (byte)0xAA);

    var compressed = ByteRun1Compressor.Encode(data);
    var decompressed = ByteRun1Compressor.Decode(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllDifferentBytes() {
    var data = new byte[100];
    for (var i = 0; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    var compressed = ByteRun1Compressor.Encode(data);
    var decompressed = ByteRun1Compressor.Decode(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_MixedData() {
    var data = new byte[] {
      0x01, 0x02, 0x03, 0x04,
      0xAA, 0xAA, 0xAA, 0xAA, 0xAA,
      0x10, 0x20, 0x30,
      0xFF, 0xFF, 0xFF,
    };

    var compressed = ByteRun1Compressor.Encode(data);
    var decompressed = ByteRun1Compressor.Decode(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void Decode_KnownSequence() {
    var compressed = new byte[] {
      0x01, 0x03, 0x04, // literal: header=1 -> 2 bytes
      0xFE, 0xAA,       // run: header=-2 -> 3 repeats of 0xAA
    };

    var result = ByteRun1Compressor.Decode(compressed, 5);

    Assert.That(result, Is.EqualTo(new byte[] { 0x03, 0x04, 0xAA, 0xAA, 0xAA }));
  }

  [Test]
  [Category("Unit")]
  public void Encode_AllSameBytes_CompressesWell() {
    var data = new byte[256];
    Array.Fill(data, (byte)0x42);

    var compressed = ByteRun1Compressor.Encode(data);

    Assert.That(compressed.Length, Is.LessThan(data.Length));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_SingleByte() {
    var data = new byte[] { 0x55 };

    var compressed = ByteRun1Compressor.Encode(data);
    var decompressed = ByteRun1Compressor.Decode(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_LargeData() {
    var data = new byte[4096];
    var rng = new Random(42);
    rng.NextBytes(data);

    var compressed = ByteRun1Compressor.Encode(data);
    var decompressed = ByteRun1Compressor.Decode(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }
}
