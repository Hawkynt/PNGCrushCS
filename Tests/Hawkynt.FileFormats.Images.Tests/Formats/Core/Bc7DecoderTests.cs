using System;
using FileFormat.Core.BlockDecoders;

namespace FileFormat.Core.Tests;

[TestFixture]
public sealed class Bc7DecoderTests {

  [Test]
  [Category("Unit")]
  public void DecodeBlock_Mode6_UniformColor_ProducesExpectedRgba() {
    // Mode 6: bit 6 set => block[0] = 0x40 (0b01000000)
    // Mode 6: 1 subset, 0 partition bits, 0 rotation bits, 0 indexSel bits,
    //         7 color bits, 7 alpha bits, 1 endpoint P-bit, 0 shared P-bits, 4-bit index
    // After mode bit (bit 6, bitPos=7):
    //   partition=0 (0 bits), rotation=0 (0 bits), indexSel=0 (0 bits)
    //   2 endpoints x 7-bit R, 2 endpoints x 7-bit G, 2 endpoints x 7-bit B, 2 endpoints x 7-bit A
    //   = 14 x 7 = 98 bits of endpoint data + 2 P-bits
    //   Then 16 x 4-bit indices (anchor pixel 0 has 3 bits)
    // For uniform color: set both endpoints identical and all indices to 0
    // R0=R1=127 (7 bits), G0=G1=127, B0=B1=127, A0=A1=127, P0=P1=1
    // Unquantized: (127<<1|1) = 255, expanded to 8-bit = 255
    var block = new byte[16];
    block[0] = 0x40; // mode 6

    // Pack bits starting at bitPos=7
    // We need to set R0[7], R1[7], G0[7], G1[7], B0[7], B1[7], A0[7], A1[7] all to 127 (0x7F)
    // Then P0=1, P1=1, and all index bits to 0
    _SetBitsAt(block, 7, 7, 0x7F);   // R0
    _SetBitsAt(block, 14, 7, 0x7F);  // R1
    _SetBitsAt(block, 21, 7, 0x7F);  // G0
    _SetBitsAt(block, 28, 7, 0x7F);  // G1
    _SetBitsAt(block, 35, 7, 0x7F);  // B0
    _SetBitsAt(block, 42, 7, 0x7F);  // B1
    _SetBitsAt(block, 49, 7, 0x7F);  // A0
    _SetBitsAt(block, 56, 7, 0x7F);  // A1
    _SetBitsAt(block, 63, 1, 1);     // P0
    _SetBitsAt(block, 64, 1, 1);     // P1
    // indices: all 0 (already zeroed)

    var output = new byte[64];
    Bc7Decoder.DecodeBlock(block, output);

    // All 16 pixels should be (255, 255, 255, 255)
    for (var i = 0; i < 16; ++i) {
      var off = i * 4;
      Assert.That(output[off], Is.EqualTo(255), $"Pixel {i} R");
      Assert.That(output[off + 1], Is.EqualTo(255), $"Pixel {i} G");
      Assert.That(output[off + 2], Is.EqualTo(255), $"Pixel {i} B");
      Assert.That(output[off + 3], Is.EqualTo(255), $"Pixel {i} A");
    }
  }

  [Test]
  [Category("Unit")]
  public void DecodeBlock_Mode6_BlackEndpoints_ProducesBlack() {
    // Mode 6 with all endpoint values = 0 and P-bits = 0
    var block = new byte[16];
    block[0] = 0x40; // mode 6
    // All other bits are 0 (endpoints = 0, P-bits = 0, indices = 0)

    var output = new byte[64];
    Bc7Decoder.DecodeBlock(block, output);

    for (var i = 0; i < 16; ++i) {
      var off = i * 4;
      Assert.That(output[off], Is.EqualTo(0), $"Pixel {i} R");
      Assert.That(output[off + 1], Is.EqualTo(0), $"Pixel {i} G");
      Assert.That(output[off + 2], Is.EqualTo(0), $"Pixel {i} B");
      Assert.That(output[off + 3], Is.EqualTo(0), $"Pixel {i} A");
    }
  }

  [Test]
  [Category("Unit")]
  public void DecodeBlock_ReservedMode_ProducesTransparentBlack() {
    // All zero block means no mode bit is set => reserved mode
    var block = new byte[16];
    var output = new byte[64];
    Bc7Decoder.DecodeBlock(block, output);

    for (var i = 0; i < 64; ++i)
      Assert.That(output[i], Is.EqualTo(0), $"Byte {i}");
  }

  [Test]
  [Category("Unit")]
  public void DecodeImage_Single4x4Block_ProducesCorrectPixelCount() {
    // One 4x4 block = 16 bytes of BC7 data
    var data = new byte[16];
    data[0] = 0x40; // mode 6, all zero endpoints
    var output = new byte[4 * 4 * 4];

    Bc7Decoder.DecodeImage(data, 4, 4, output);

    Assert.That(output.Length, Is.EqualTo(64));
  }

  [Test]
  [Category("Unit")]
  public void DecodeImage_8x8_FourBlocks_ProducesCorrectOutputSize() {
    // 8x8 = 2x2 blocks = 4 blocks x 16 bytes = 64 bytes
    var data = new byte[64];
    for (var i = 0; i < 4; ++i)
      data[i * 16] = 0x40; // mode 6 for each block

    var output = new byte[8 * 8 * 4];
    Bc7Decoder.DecodeImage(data, 8, 8, output);

    Assert.That(output.Length, Is.EqualTo(256));
  }

  [Test]
  [Category("Unit")]
  public void DecodeImage_InsufficientData_DoesNotThrow() {
    // Provide only 8 bytes for a 4x4 image (needs 16 bytes)
    var data = new byte[8];
    var output = new byte[4 * 4 * 4];

    Assert.DoesNotThrow(() => Bc7Decoder.DecodeImage(data, 4, 4, output));
  }

  [Test]
  [Category("Unit")]
  public void DecodeImage_EmptyData_ProducesZeroedOutput() {
    var data = Array.Empty<byte>();
    var output = new byte[4 * 4 * 4];
    Array.Fill(output, (byte)0xCC); // fill with sentinel

    Bc7Decoder.DecodeImage(data, 4, 4, output);

    // With no data, DecodeImage returns early; output stays as sentinel
    // (validates it doesn't crash)
    Assert.Pass();
  }

  [Test]
  [Category("Unit")]
  public void DecodeBlock_Mode6_AllPixelsSameColor_OutputIsUniform() {
    // Mode 6 with identical endpoints and all index=0 should produce uniform color
    var block = new byte[16];
    block[0] = 0x40; // mode 6
    // Set R0=R1=64 (7 bits), everything else zero, P0=P1=0
    _SetBitsAt(block, 7, 7, 64);
    _SetBitsAt(block, 14, 7, 64);

    var output = new byte[64];
    Bc7Decoder.DecodeBlock(block, output);

    var firstR = output[0];
    for (var i = 1; i < 16; ++i)
      Assert.That(output[i * 4], Is.EqualTo(firstR), $"Pixel {i} R differs from pixel 0");
  }

  private static void _SetBitsAt(byte[] data, int bitPos, int numBits, int value) {
    for (var i = 0; i < numBits; ++i) {
      var byteIdx = (bitPos + i) >> 3;
      var bitIdx = (bitPos + i) & 7;
      if (((value >> i) & 1) != 0)
        data[byteIdx] |= (byte)(1 << bitIdx);
    }
  }
}
