using System;
using FileFormat.Ilbm;

namespace FileFormat.Ilbm.Tests;

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
      0x01, 0x02, 0x03, 0x04, // literal run
      0xAA, 0xAA, 0xAA, 0xAA, 0xAA, // repeat run
      0x10, 0x20, 0x30, // literal run
      0xFF, 0xFF, 0xFF // repeat run
    };

    var compressed = ByteRun1Compressor.Encode(data);
    var decompressed = ByteRun1Compressor.Decode(compressed, data.Length);

    Assert.That(decompressed, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void Decode_KnownSequence() {
    // Literal: 2 bytes (0x03, 0x04), Run: 3x 0xAA
    var compressed = new byte[] {
      0x01, 0x03, 0x04, // literal: header=1 -> 2 bytes
      0xFE, 0xAA        // run: header=-2 -> 3 repeats of 0xAA
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

    // Runs of 128 bytes compress to 2 bytes each; 256 bytes -> 2 runs -> 4 bytes
    Assert.That(compressed.Length, Is.LessThan(data.Length));
  }
}
