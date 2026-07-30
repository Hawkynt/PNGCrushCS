using System;
using FileFormat.Blp;

namespace FileFormat.Blp.Tests;

[TestFixture]
public sealed class BlpDxtDecoderTests {

  [Test]
  [Category("Unit")]
  public void DecodeRgb565_AllZero_ReturnsBlack() {
    var (r, g, b) = BlpDxtDecoder.DecodeRgb565(0);
    Assert.That(r, Is.EqualTo(0));
    Assert.That(g, Is.EqualTo(0));
    Assert.That(b, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void DecodeRgb565_AllOnes_ReturnsWhite() {
    var (r, g, b) = BlpDxtDecoder.DecodeRgb565(0xFFFF);
    Assert.That(r, Is.EqualTo(255));
    Assert.That(g, Is.EqualTo(255));
    Assert.That(b, Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void DecodeRgb565_PureRed_ReturnsRed() {
    // R=31, G=0, B=0 -> 0xF800
    var (r, g, b) = BlpDxtDecoder.DecodeRgb565(0xF800);
    Assert.That(r, Is.EqualTo(255));
    Assert.That(g, Is.EqualTo(0));
    Assert.That(b, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void DecodeRgb565_PureGreen_ReturnsGreen() {
    // R=0, G=63, B=0 -> 0x07E0
    var (r, g, b) = BlpDxtDecoder.DecodeRgb565(0x07E0);
    Assert.That(r, Is.EqualTo(0));
    Assert.That(g, Is.EqualTo(255));
    Assert.That(b, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void DecodeRgb565_PureBlue_ReturnsBlue() {
    // R=0, G=0, B=31 -> 0x001F
    var (r, g, b) = BlpDxtDecoder.DecodeRgb565(0x001F);
    Assert.That(r, Is.EqualTo(0));
    Assert.That(g, Is.EqualTo(0));
    Assert.That(b, Is.EqualTo(255));
  }

  [Test]
  [Category("Unit")]
  public void DecodeDxt1Block_AllZeros_ProducesBlackPixels() {
    var block = new byte[8];
    var bgra = new byte[4 * 4 * 4]; // 4x4 image
    BlpDxtDecoder.DecodeDxt1Block(block, bgra, 4 * 4, 0, 0, 4, 4);

    // All pixels should be black (index 0 = c0 = black)
    for (var i = 0; i < 16; ++i) {
      Assert.That(bgra[i * 4], Is.EqualTo(0));     // B
      Assert.That(bgra[i * 4 + 1], Is.EqualTo(0)); // G
      Assert.That(bgra[i * 4 + 2], Is.EqualTo(0)); // R
    }
  }

  [Test]
  [Category("Unit")]
  public void DecodeDxt1Block_C0GreaterC1_OpaquePalette() {
    // c0 = 0xFFFF (white), c1 = 0x0000 (black), c0 > c1 => 4-color opaque
    var block = new byte[8];
    block[0] = 0xFF; block[1] = 0xFF; // c0 = white
    block[2] = 0x00; block[3] = 0x00; // c1 = black
    // Index byte 0: all 0b01 = color1 (black) for all 4 pixels in row 0
    block[4] = 0x55; // 01 01 01 01
    // Remaining rows: index 0 = c0 (white)
    block[5] = 0x00;
    block[6] = 0x00;
    block[7] = 0x00;

    var bgra = new byte[4 * 4 * 4];
    BlpDxtDecoder.DecodeDxt1Block(block, bgra, 4 * 4, 0, 0, 4, 4);

    // Row 0, pixel 0 should be black (index 1)
    Assert.That(bgra[0], Is.EqualTo(0));   // B
    Assert.That(bgra[1], Is.EqualTo(0));   // G
    Assert.That(bgra[2], Is.EqualTo(0));   // R
    Assert.That(bgra[3], Is.EqualTo(255)); // A (opaque)

    // Row 1, pixel 0 should be white (index 0)
    var row1Offset = 4 * 4; // stride = 16 bytes per row
    Assert.That(bgra[row1Offset], Is.EqualTo(255));     // B
    Assert.That(bgra[row1Offset + 1], Is.EqualTo(255)); // G
    Assert.That(bgra[row1Offset + 2], Is.EqualTo(255)); // R
    Assert.That(bgra[row1Offset + 3], Is.EqualTo(255)); // A
  }

  [Test]
  [Category("Unit")]
  public void DecodeDxt1Block_C0LessEqualC1_HasTransparentEntry() {
    // c0 = 0x0000, c1 = 0xFFFF => c0 <= c1 => 3 colors + transparent
    var block = new byte[8];
    block[0] = 0x00; block[1] = 0x00; // c0 = black
    block[2] = 0xFF; block[3] = 0xFF; // c1 = white
    // All indices = 3 = transparent
    block[4] = 0xFF; // 11 11 11 11
    block[5] = 0xFF;
    block[6] = 0xFF;
    block[7] = 0xFF;

    var bgra = new byte[4 * 4 * 4];
    BlpDxtDecoder.DecodeDxt1Block(block, bgra, 4 * 4, 0, 0, 4, 4);

    // Index 3 with c0 <= c1 means transparent (alpha=0)
    Assert.That(bgra[3], Is.EqualTo(0)); // A = 0
  }

  [Test]
  [Category("Unit")]
  public void DecodeDxt3Block_ExplicitAlpha() {
    // Build a DXT3 block: 8 bytes alpha + 8 bytes color
    var block = new byte[16];
    // Alpha: set all pixels to max alpha (0xFF per nibble)
    for (var i = 0; i < 8; ++i)
      block[i] = 0xFF;
    // Color block: c0 = white, c1 = black, index 0
    block[8] = 0xFF; block[9] = 0xFF;   // c0 = white
    block[10] = 0x00; block[11] = 0x00; // c1 = black
    // All index 0
    block[12] = 0x00; block[13] = 0x00;
    block[14] = 0x00; block[15] = 0x00;

    var bgra = new byte[4 * 4 * 4];
    BlpDxtDecoder.DecodeDxt3Block(block, bgra, 4 * 4, 0, 0, 4, 4);

    // All pixels should be white with full alpha
    Assert.That(bgra[0], Is.EqualTo(255));   // B
    Assert.That(bgra[1], Is.EqualTo(255));   // G
    Assert.That(bgra[2], Is.EqualTo(255));   // R
    Assert.That(bgra[3], Is.EqualTo(255));   // A
  }

  [Test]
  [Category("Unit")]
  public void DecodeDxt3Block_HalfAlpha() {
    var block = new byte[16];
    // Set all alpha nibbles to 0x7 (half alpha = 0x77)
    for (var i = 0; i < 8; ++i)
      block[i] = 0x77;
    // Color block: black
    // All zeros = c0=black, c1=black, indices all 0
    var bgra = new byte[4 * 4 * 4];
    BlpDxtDecoder.DecodeDxt3Block(block, bgra, 4 * 4, 0, 0, 4, 4);

    // Alpha should be 0x77 (4-bit 7 expanded to 8-bit: (7<<4)|7 = 119)
    Assert.That(bgra[3], Is.EqualTo(119));
  }

  [Test]
  [Category("Unit")]
  public void DecodeDxt5Block_InterpolatedAlpha() {
    var block = new byte[16];
    // Alpha endpoints: a0=255, a1=0 => a0 > a1 => 8 alpha values interpolated
    block[0] = 255; // alpha0
    block[1] = 0;   // alpha1
    // All alpha indices = 0 => use alpha0 = 255
    block[2] = 0; block[3] = 0; block[4] = 0;
    block[5] = 0; block[6] = 0; block[7] = 0;
    // Color: white, all index 0
    block[8] = 0xFF; block[9] = 0xFF;
    block[10] = 0x00; block[11] = 0x00;
    block[12] = 0; block[13] = 0; block[14] = 0; block[15] = 0;

    var bgra = new byte[4 * 4 * 4];
    BlpDxtDecoder.DecodeDxt5Block(block, bgra, 4 * 4, 0, 0, 4, 4);

    Assert.That(bgra[3], Is.EqualTo(255)); // Alpha = alpha0
  }

  [Test]
  [Category("Unit")]
  public void DecodeDxt5Block_AlphaIndex1_UsesAlpha1() {
    var block = new byte[16];
    block[0] = 200; // alpha0
    block[1] = 50;  // alpha1
    // Set all alpha indices to 1 (each 3 bits = 001)
    // For first 8 pixels: bits = 001 001 001 001 001 001 001 001 = 0x249249
    var val0 = 0x249249U;
    block[2] = (byte)(val0 & 0xFF);
    block[3] = (byte)((val0 >> 8) & 0xFF);
    block[4] = (byte)((val0 >> 16) & 0xFF);
    block[5] = (byte)(val0 & 0xFF);
    block[6] = (byte)((val0 >> 8) & 0xFF);
    block[7] = (byte)((val0 >> 16) & 0xFF);
    // Color: black
    var bgra = new byte[4 * 4 * 4];
    BlpDxtDecoder.DecodeDxt5Block(block, bgra, 4 * 4, 0, 0, 4, 4);

    Assert.That(bgra[3], Is.EqualTo(50)); // Alpha = alpha1
  }

  [Test]
  [Category("Unit")]
  public void DecodeDxt1Image_4x4_Produces64Bytes() {
    var data = new byte[8]; // One 4x4 DXT1 block
    var result = BlpDxtDecoder.DecodeDxt1Image(data, 4, 4);

    Assert.That(result.Length, Is.EqualTo(4 * 4 * 4));
  }

  [Test]
  [Category("Unit")]
  public void DecodeDxt3Image_4x4_Produces64Bytes() {
    var data = new byte[16]; // One 4x4 DXT3 block
    var result = BlpDxtDecoder.DecodeDxt3Image(data, 4, 4);

    Assert.That(result.Length, Is.EqualTo(4 * 4 * 4));
  }

  [Test]
  [Category("Unit")]
  public void DecodeDxt5Image_4x4_Produces64Bytes() {
    var data = new byte[16]; // One 4x4 DXT5 block
    var result = BlpDxtDecoder.DecodeDxt5Image(data, 4, 4);

    Assert.That(result.Length, Is.EqualTo(4 * 4 * 4));
  }

  [Test]
  [Category("Unit")]
  public void DecodeDxt1Image_8x8_HandlesMultipleBlocks() {
    // 8x8 = 2x2 blocks = 4 blocks x 8 bytes = 32 bytes
    var data = new byte[32];
    var result = BlpDxtDecoder.DecodeDxt1Image(data, 8, 8);

    Assert.That(result.Length, Is.EqualTo(8 * 8 * 4));
  }
}
