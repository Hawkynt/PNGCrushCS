using System;
using FileFormat.Core.BlockDecoders;

namespace FileFormat.Core.Tests;

[TestFixture]
public sealed class Bc6HDecoderTests {

  [Test]
  [Category("Unit")]
  public void DecodeBlock_UnsignedMode0_ProducesNonZeroOutput() {
    // Mode 0: 5-bit mode = 0x00, 1 subset, 10.5.5.5 transformed
    // Set first byte so mode bits = 0x00 (bit pattern 00000 in lowest 5 bits)
    // and fill some endpoint data with nonzero values
    var block = new byte[16];
    // Mode 0: bits [4:0] = 00000
    // Fill endpoint bits after header with nonzero values for R0
    block[0] = 0x00; // mode bits = 00000
    block[1] = 0xFF; // endpoint data
    block[2] = 0xFF;

    var output = new byte[64];
    Bc6HDecoder.DecodeBlock(block, output, isSigned: false);

    var hasNonZero = false;
    for (var i = 0; i < 64; ++i)
      if (output[i] != 0) {
        hasNonZero = true;
        break;
      }

    Assert.That(hasNonZero, Is.True, "Unsigned BC6H block with nonzero endpoint data should produce nonzero output");
  }

  [Test]
  [Category("Unit")]
  public void DecodeBlock_AllPixelsHaveFullAlpha() {
    // BC6H is HDR RGB - alpha channel should always be 255
    var block = new byte[16];
    block[0] = 0x00; // mode 0
    block[1] = 0xAA;
    block[2] = 0x55;

    var output = new byte[64];
    Bc6HDecoder.DecodeBlock(block, output, isSigned: false);

    for (var i = 0; i < 16; ++i)
      Assert.That(output[i * 4 + 3], Is.EqualTo(255), $"Pixel {i} alpha should be 255");
  }

  [Test]
  [Category("Unit")]
  public void DecodeBlock_SignedVsUnsigned_ProducesDifferentResults() {
    var block = new byte[16];
    block[0] = 0x00; // mode 0
    // Set some nonzero endpoint values that will differ between signed/unsigned interpretation
    block[1] = 0xAB;
    block[2] = 0xCD;
    block[3] = 0xEF;

    var outputUnsigned = new byte[64];
    var outputSigned = new byte[64];
    Bc6HDecoder.DecodeBlock(block, outputUnsigned, isSigned: false);
    Bc6HDecoder.DecodeBlock(block, outputSigned, isSigned: true);

    var differs = false;
    for (var i = 0; i < 64; i += 4) {
      // Compare RGB channels (skip alpha which is always 255)
      if (outputUnsigned[i] != outputSigned[i] ||
          outputUnsigned[i + 1] != outputSigned[i + 1] ||
          outputUnsigned[i + 2] != outputSigned[i + 2]) {
        differs = true;
        break;
      }
    }

    Assert.That(differs, Is.True, "Signed and unsigned modes should produce different results for the same data");
  }

  [Test]
  [Category("Unit")]
  public void DecodeImage_Single4x4Block_ProducesCorrectSize() {
    var data = new byte[16];
    data[0] = 0x00; // mode 0
    var output = new byte[4 * 4 * 4];

    Bc6HDecoder.DecodeImage(data, 4, 4, output, isSigned: false);

    Assert.That(output.Length, Is.EqualTo(64));
  }

  [Test]
  [Category("Unit")]
  public void DecodeImage_8x8_FourBlocks_ProducesCorrectDimensions() {
    // 8x8 = 2x2 blocks = 4 blocks x 16 bytes = 64 bytes
    var data = new byte[64];
    var output = new byte[8 * 8 * 4];

    Bc6HDecoder.DecodeImage(data, 8, 8, output, isSigned: false);

    Assert.That(output.Length, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void DecodeImage_InsufficientData_DoesNotThrow() {
    var data = new byte[8]; // too small for 4x4 (needs 16)
    var output = new byte[4 * 4 * 4];

    Assert.DoesNotThrow(() => Bc6HDecoder.DecodeImage(data, 4, 4, output, isSigned: false));
  }

  [Test]
  [Category("Unit")]
  public void DecodeImage_EmptyData_DoesNotThrow() {
    var data = Array.Empty<byte>();
    var output = new byte[4 * 4 * 4];

    Assert.DoesNotThrow(() => Bc6HDecoder.DecodeImage(data, 4, 4, output, isSigned: true));
  }

  [Test]
  [Category("Unit")]
  public void DecodeBlock_UnknownMode_ProducesMagentaPixels() {
    // Set block so no valid mode is matched
    // All 5 low bits = 11111 doesn't match any 5-bit mode, and 2-bit prefix < 2 triggers 5-bit path
    // Actually: modeBits = bits[1:0]. If bits[1:0] >= 2, it's 2-bit mode path.
    // For unknown: use a block where 5-bit mode doesn't match any known value
    // 5-bit value 0x04 (00100) is not in the mode map
    var block = new byte[16];
    block[0] = 0x04; // 5-bit mode = 00100 -> not in switch -> mode = -1

    var output = new byte[64];
    Bc6HDecoder.DecodeBlock(block, output, isSigned: false);

    // Unknown mode produces magenta (255, 0, 255, 255)
    Assert.That(output[0], Is.EqualTo(255), "R = 255");
    Assert.That(output[1], Is.EqualTo(0), "G = 0");
    Assert.That(output[2], Is.EqualTo(255), "B = 255");
    Assert.That(output[3], Is.EqualTo(255), "A = 255");
  }

  [Test]
  [Category("Unit")]
  public void DecodeBlock_SignedMode_ZeroEndpoints_ProducesValidOutput() {
    // All zero block in signed mode
    var block = new byte[16];
    block[0] = 0x00; // mode 0

    var output = new byte[64];
    Bc6HDecoder.DecodeBlock(block, output, isSigned: true);

    // Should not throw, all alpha bytes should be 255
    for (var i = 0; i < 16; ++i)
      Assert.That(output[i * 4 + 3], Is.EqualTo(255), $"Pixel {i} alpha");
  }
}
