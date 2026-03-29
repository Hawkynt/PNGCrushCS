using System;
using FileFormat.IffDeep;

namespace FileFormat.IffDeep.Tests;

[TestFixture]
public sealed class ByteRun1CompressorTests {

  [Test]
  [Category("Unit")]
  public void RoundTrip_Empty() {
    var original = ReadOnlySpan<byte>.Empty;
    var encoded = ByteRun1Compressor.Encode(original);
    var decoded = ByteRun1Compressor.Decode(encoded, 0);

    Assert.That(decoded, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllSame() {
    var original = new byte[64];
    Array.Fill(original, (byte)0xAB);

    var encoded = ByteRun1Compressor.Encode(original);
    var decoded = ByteRun1Compressor.Decode(encoded, original.Length);

    Assert.That(decoded, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_AllDifferent() {
    var original = new byte[64];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 3 % 256);

    var encoded = ByteRun1Compressor.Encode(original);
    var decoded = ByteRun1Compressor.Decode(encoded, original.Length);

    Assert.That(decoded, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_MixedData() {
    var original = new byte[] { 0x01, 0x01, 0x01, 0x02, 0x03, 0x04, 0x04, 0x04, 0x04, 0x05 };

    var encoded = ByteRun1Compressor.Encode(original);
    var decoded = ByteRun1Compressor.Decode(encoded, original.Length);

    Assert.That(decoded, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_LargeData() {
    var original = new byte[1024];
    for (var i = 0; i < original.Length; ++i)
      original[i] = (byte)(i * 7 % 256);

    var encoded = ByteRun1Compressor.Encode(original);
    var decoded = ByteRun1Compressor.Decode(encoded, original.Length);

    Assert.That(decoded, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void Encode_AllSame_CompressesWell() {
    var original = new byte[256];
    Array.Fill(original, (byte)0xFF);

    var encoded = ByteRun1Compressor.Encode(original);

    Assert.That(encoded.Length, Is.LessThan(original.Length));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_SingleByte() {
    var original = new byte[] { 0x42 };

    var encoded = ByteRun1Compressor.Encode(original);
    var decoded = ByteRun1Compressor.Decode(encoded, original.Length);

    Assert.That(decoded, Is.EqualTo(original));
  }
}
