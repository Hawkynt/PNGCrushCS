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
    // A repeat of L bytes is the opcode L - 1, below 128.
    var compressed = new byte[] { 4 - 1, 0xAA };
    var result = XcfTileDecoder.DecodeRle(compressed, 1, 2, 2);

    Assert.That(result.Length, Is.EqualTo(4));
    for (var i = 0; i < 4; ++i)
      Assert.That(result[i], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void DecodeRle_LongRuns_TakeATwoByteCount() {
    // Opcodes 127 and 128 carry their length in two bytes, not four: 127 escapes a repeat, 128 a
    // literal, matching which side of 128 each opcode class sits on.
    var longRepeat = new byte[] { 127, 0, 4, 0x5A };
    var longLiteral = new byte[] { 128, 0, 4, 10, 20, 30, 40 };

    Assert.Multiple(() => {
      Assert.That(XcfTileDecoder.DecodeRle(longLiteral, 1, 2, 2), Is.EqualTo(new byte[] { 10, 20, 30, 40 }));
      Assert.That(XcfTileDecoder.DecodeRle(longRepeat, 1, 2, 2), Is.EqualTo(new byte[] { 0x5A, 0x5A, 0x5A, 0x5A }));
    });
  }

  [Test]
  [Category("Unit")]
  public void EncodeRle_RoundTrips() {
    var pixels = new byte[64 * 4];
    for (var i = 0; i < 64; ++i) {
      pixels[i * 4] = (byte)(i < 40 ? 0x11 : i);
      pixels[i * 4 + 1] = 0x22;
      pixels[i * 4 + 2] = (byte)(i * 7);
      pixels[i * 4 + 3] = 0xFF;
    }

    var restored = XcfTileDecoder.DecodeRle(XcfTileDecoder.EncodeRle(pixels, 4, 8, 8), 4, 8, 8);

    Assert.That(restored, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void DecodeRle_Literal_ReturnsOriginalBytes() {
    // A literal of L bytes is the opcode 256 - L, at or above 128: four bytes is 252.
    var compressed = new byte[] { 256 - 4, 10, 20, 30, 40 };
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
    var compressed = new byte[] {
      256 - 2, 100, 200,  // channel 0: 2 literal bytes
      256 - 2, 255, 128   // channel 1: 2 literal bytes
    };

    var result = XcfTileDecoder.DecodeRle(compressed, 2, 2, 1);

    Assert.That(result.Length, Is.EqualTo(4));
    Assert.That(result[0], Is.EqualTo(100)); // pixel0 channel0
    Assert.That(result[1], Is.EqualTo(255)); // pixel0 channel1
    Assert.That(result[2], Is.EqualTo(200)); // pixel1 channel0
    Assert.That(result[3], Is.EqualTo(128)); // pixel1 channel1
  }
}
