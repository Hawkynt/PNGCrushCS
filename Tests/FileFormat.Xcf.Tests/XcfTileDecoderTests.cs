using System;
using FileFormat.Xcf;

namespace FileFormat.Xcf.Tests;

[TestFixture]
public sealed class XcfTileDecoderTests {

  [Test]
  [Category("Unit")]
  public void DecodeUncompressed_ValidData_ReturnsDeinterleavedPixels() {
    // 2x2 image, 3 bpp (RGB), channel-planar storage:
    // channel0: R0, R1, R2, R3
    // channel1: G0, G1, G2, G3
    // channel2: B0, B1, B2, B3
    var planar = new byte[] {
      10, 20, 30, 40,  // R values
      50, 60, 70, 80,  // G values
      90, 100, 110, 120 // B values
    };

    var result = XcfTileDecoder.DecodeUncompressed(planar, 3, 2, 2);

    // Expected interleaved: R0,G0,B0, R1,G1,B1, R2,G2,B2, R3,G3,B3
    Assert.That(result[0], Is.EqualTo(10));  // R0
    Assert.That(result[1], Is.EqualTo(50));  // G0
    Assert.That(result[2], Is.EqualTo(90));  // B0
    Assert.That(result[3], Is.EqualTo(20));  // R1
    Assert.That(result[4], Is.EqualTo(60));  // G1
    Assert.That(result[5], Is.EqualTo(100)); // B1
  }

  [Test]
  [Category("Unit")]
  public void DecodeRle_AllSame_ReturnsRepeatedValue() {
    // RLE for 4 pixels, 1 bpp: repeat byte 0xAA four times
    // n = 257 - 4 = 253, value = 0xAA
    var compressed = new byte[] { 253, 0xAA };
    var result = XcfTileDecoder.DecodeRle(compressed, 1, 2, 2);

    Assert.That(result.Length, Is.EqualTo(4));
    for (var i = 0; i < 4; ++i)
      Assert.That(result[i], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void DecodeRle_Literal_ReturnsOriginalBytes() {
    // RLE literal: n = 3 (4 literal bytes), followed by 4 bytes
    var compressed = new byte[] { 3, 10, 20, 30, 40 };
    var result = XcfTileDecoder.DecodeRle(compressed, 1, 2, 2);

    Assert.That(result.Length, Is.EqualTo(4));
    Assert.That(result[0], Is.EqualTo(10));
    Assert.That(result[1], Is.EqualTo(20));
    Assert.That(result[2], Is.EqualTo(30));
    Assert.That(result[3], Is.EqualTo(40));
  }

  [Test]
  [Category("Unit")]
  public void EncodeRle_DecodeRle_RoundTrip() {
    var original = new byte[] { 100, 100, 100, 50, 60, 70, 80, 80 };
    var compressed = XcfTileDecoder.EncodeRle(original, 1, 4, 2);
    var restored = XcfTileDecoder.DecodeRle(compressed, 1, 4, 2);

    Assert.That(restored, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void DecodeRle_MultiChannel_InterleavesCorrectly() {
    // 2 pixels, 2 bpp (e.g., GrayA)
    // Channel 0: literal 2 bytes (n=1): 100, 200
    // Channel 1: literal 2 bytes (n=1): 255, 128
    var compressed = new byte[] {
      1, 100, 200,  // channel 0: 2 literal bytes
      1, 255, 128   // channel 1: 2 literal bytes
    };

    var result = XcfTileDecoder.DecodeRle(compressed, 2, 2, 1);

    Assert.That(result.Length, Is.EqualTo(4));
    Assert.That(result[0], Is.EqualTo(100)); // pixel0 channel0
    Assert.That(result[1], Is.EqualTo(255)); // pixel0 channel1
    Assert.That(result[2], Is.EqualTo(200)); // pixel1 channel0
    Assert.That(result[3], Is.EqualTo(128)); // pixel1 channel1
  }
}
