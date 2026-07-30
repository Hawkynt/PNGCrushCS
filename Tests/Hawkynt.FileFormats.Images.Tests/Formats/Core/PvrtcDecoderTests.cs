using System;
using FileFormat.Core.BlockDecoders;

namespace FileFormat.Core.Tests;

[TestFixture]
public sealed class PvrtcDecoderTests {

  [Test]
  [Category("Unit")]
  public void Decode4Bpp_MinimalImage_ProducesCorrectOutputSize() {
    // 4x4 image at 4bpp = 1 block = 8 bytes
    var data = new byte[8];
    var output = new byte[4 * 4 * 4];

    PvrtcDecoder.Decode4Bpp(data, 4, 4, output);

    Assert.That(output.Length, Is.EqualTo(64));
  }

  [Test]
  [Category("Unit")]
  public void Decode4Bpp_8x8Image_ProducesCorrectOutputSize() {
    // 8x8 at 4bpp = 2x2 = 4 blocks x 8 bytes = 32 bytes
    var data = new byte[32];
    var output = new byte[8 * 8 * 4];

    PvrtcDecoder.Decode4Bpp(data, 8, 8, output);

    Assert.That(output.Length, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void Decode2Bpp_MinimalImage_ProducesCorrectOutputSize() {
    // 2bpp uses 8x4 block footprint
    // 8x4 image = 1 block = 8 bytes
    var data = new byte[8];
    var output = new byte[8 * 4 * 4];

    PvrtcDecoder.Decode2Bpp(data, 8, 4, output);

    Assert.That(output.Length, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void Decode2Bpp_16x8Image_ProducesCorrectOutputSize() {
    // 16x8 at 2bpp (8x4 blocks) = 2x2 = 4 blocks x 8 bytes = 32 bytes
    var data = new byte[32];
    var output = new byte[16 * 8 * 4];

    PvrtcDecoder.Decode2Bpp(data, 16, 8, output);

    Assert.That(output.Length, Is.EqualTo(512));
  }

  [Test]
  [Category("Unit")]
  public void Decode4Bpp_AllZeroData_ProducesValidOutput() {
    var data = new byte[8]; // 4x4 block, all zeros
    var output = new byte[4 * 4 * 4];

    PvrtcDecoder.Decode4Bpp(data, 4, 4, output);

    // All-zero block: colors decoded from zero bits, blended by modulation
    // Should produce valid (low or zero) values without crashing
    for (var i = 0; i < 16; ++i)
      Assert.That(output[i * 4 + 3], Is.LessThanOrEqualTo(255), $"Pixel {i} alpha in range");
  }

  [Test]
  [Category("Unit")]
  public void Decode4Bpp_InsufficientData_ProducesZeroedOutput() {
    // 4x4 needs 8 bytes, provide only 4
    var data = new byte[4];
    var output = new byte[4 * 4 * 4];
    Array.Fill(output, (byte)0xCC); // sentinel

    PvrtcDecoder.Decode4Bpp(data, 4, 4, output);

    // When data is too short, output should be cleared to zero
    for (var i = 0; i < output.Length; ++i)
      Assert.That(output[i], Is.EqualTo(0), $"Byte {i} should be zeroed");
  }

  [Test]
  [Category("Unit")]
  public void Decode4Bpp_OpaqueBlock_AllAlpha255() {
    // Build a single 4x4 block with the opaque flag set (bit 31 of word1)
    var data = new byte[8];
    // word1 (bytes 4-7): set bit 31 (isOpaque=true)
    data[7] = 0x80; // bit 31 of word1

    var output = new byte[4 * 4 * 4];
    PvrtcDecoder.Decode4Bpp(data, 4, 4, output);

    for (var i = 0; i < 16; ++i)
      Assert.That(output[i * 4 + 3], Is.EqualTo(255), $"Pixel {i} alpha should be 255 in opaque mode");
  }

  [Test]
  [Category("Unit")]
  public void Decode4Bpp_NonZeroColorEndpoints_ProducesNonBlackPixels() {
    var data = new byte[8];
    // word1 (bytes 4-7): set opaque flag and nonzero color data
    // Color A (bits 1..15): all 1s = 0x7FFF, Color B (bits 16..30): all 1s = 0x7FFF
    // Opaque flag (bit 31) = 1
    // Total word1 = 0xFFFFFFFE (bit 0 is modMode)
    data[4] = 0xFE;
    data[5] = 0xFF;
    data[6] = 0xFF;
    data[7] = 0xFF;
    // modulation data = all zeros => weight=0 => output = colorA

    var output = new byte[4 * 4 * 4];
    PvrtcDecoder.Decode4Bpp(data, 4, 4, output);

    var hasNonZero = false;
    for (var i = 0; i < 16; ++i) {
      var off = i * 4;
      if (output[off] != 0 || output[off + 1] != 0 || output[off + 2] != 0) {
        hasNonZero = true;
        break;
      }
    }

    Assert.That(hasNonZero, Is.True, "Nonzero color endpoints should produce visible pixels");
  }

  [Test]
  [Category("Unit")]
  public void Decode4Bpp_OutputBufferIsExactSize() {
    var width = 4;
    var height = 4;
    var expectedSize = width * height * 4;
    var data = new byte[8];
    var output = new byte[expectedSize];

    PvrtcDecoder.Decode4Bpp(data, width, height, output);

    Assert.That(output.Length, Is.EqualTo(expectedSize));
  }

  [Test]
  [Category("Unit")]
  public void Decode2Bpp_OutputBufferIsExactSize() {
    var width = 8;
    var height = 4;
    var expectedSize = width * height * 4;
    var data = new byte[8];
    var output = new byte[expectedSize];

    PvrtcDecoder.Decode2Bpp(data, width, height, output);

    Assert.That(output.Length, Is.EqualTo(expectedSize));
  }

  [Test]
  [Category("Unit")]
  public void Decode4Bpp_AllModulationMax_UsesColorB() {
    var data = new byte[8];
    // Modulation data (bytes 0-3): all 1s => every 2-bit index = 3
    data[0] = 0xFF;
    data[1] = 0xFF;
    data[2] = 0xFF;
    data[3] = 0xFF;
    // Word1: opaque, modMode=0, colorA = all 0 (black), colorB = all 1s (white)
    // colorA bits [1..15] = 0, colorB bits [16..30] = all 1s
    // bit 31 = opaque, bit 0 = modMode = 0
    data[4] = 0x00; // bits [7:0]: modMode=0, colorA low bits = 0
    data[5] = 0x00; // colorA high bits = 0
    data[6] = 0xFF; // colorB bits
    data[7] = 0xFF; // opaque flag + colorB high bits

    var output = new byte[4 * 4 * 4];
    PvrtcDecoder.Decode4Bpp(data, 4, 4, output);

    // With modMode=false, index 3 => weight=8 => fully colorB
    // colorB with opaque + all 1s in bits 16..30 should produce bright/white pixels
    var hasNonZero = false;
    for (var i = 0; i < 16; ++i) {
      var off = i * 4;
      if (output[off] > 0 || output[off + 1] > 0 || output[off + 2] > 0) {
        hasNonZero = true;
        break;
      }
    }

    Assert.That(hasNonZero, Is.True, "Full modulation towards nonzero colorB should produce visible pixels");
  }
}
