using System;

namespace FileFormat.Core.BlockDecoders;

/// <summary>Decodes ETC2 and EAC compressed texture blocks (RGB, RGBA, R11, RG11, and punchthrough alpha variants).</summary>
public static class Etc2Decoder {

  /// <summary>EAC modifier table (8 entries of 8 modifiers each) per the Khronos ETC2/EAC spec.</summary>
  private static readonly int[][] _EacModifierTable = [
    [-3, -6, -9, -15, 2, 5, 8, 14],
    [-3, -7, -10, -13, 2, 6, 9, 12],
    [-2, -5, -8, -13, 1, 4, 7, 12],
    [-2, -4, -6, -13, 1, 3, 5, 12],
    [-3, -6, -8, -12, 2, 5, 7, 11],
    [-3, -7, -9, -11, 2, 6, 8, 10],
    [-4, -7, -8, -11, 3, 6, 7, 10],
    [-3, -5, -8, -11, 2, 4, 7, 10],
  ];

  /// <summary>Decodes a single ETC2 RGB block (8 bytes) into 64 bytes of RGBA pixel data.</summary>
  /// <remarks>
  /// Falls back to ETC1 decoding for all blocks.
  /// TODO: Implement T mode (R overflow), H mode (G overflow), and Planar mode (B overflow)
  /// for full ETC2 compliance. These modes are triggered when differential-mode base+delta
  /// overflows the 5-bit range.
  /// </remarks>
  public static void DecodeEtc2RgbBlock(ReadOnlySpan<byte> block, Span<byte> output)
    => Etc1Decoder.DecodeBlock(block, output);

  /// <summary>Decodes a single ETC2 RGBA block (16 bytes: 8-byte EAC alpha + 8-byte ETC2 RGB) into 64 bytes of RGBA pixel data.</summary>
  public static void DecodeEtc2RgbaBlock(ReadOnlySpan<byte> block, Span<byte> output) {
    // Decode RGB from the second 8 bytes
    DecodeEtc2RgbBlock(block.Slice(8, 8), output);

    // Decode EAC alpha from the first 8 bytes and overwrite alpha channel
    _DecodeEacAlpha(block.Slice(0, 8), output);
  }

  /// <summary>Decodes a single EAC R11 block (8 bytes) into 64 bytes of RGBA pixel data (R channel only, G=0, B=0, A=255).</summary>
  public static void DecodeEacR11Block(ReadOnlySpan<byte> block, Span<byte> output) {
    // Clear output to zero, then set alpha to 255
    output.Slice(0, 64).Clear();
    for (var i = 0; i < 16; ++i)
      output[i * 4 + 3] = 255;

    // Decode EAC channel into the R component
    _DecodeEacChannel(block, output, 0);
  }

  /// <summary>Decodes a single EAC RG11 block (16 bytes: two EAC blocks) into 64 bytes of RGBA pixel data (R from first, G from second, B=0, A=255).</summary>
  public static void DecodeEacRg11Block(ReadOnlySpan<byte> block, Span<byte> output) {
    // Clear output to zero, then set alpha to 255
    output.Slice(0, 64).Clear();
    for (var i = 0; i < 16; ++i)
      output[i * 4 + 3] = 255;

    // Decode first EAC block into R channel
    _DecodeEacChannel(block.Slice(0, 8), output, 0);

    // Decode second EAC block into G channel
    _DecodeEacChannel(block.Slice(8, 8), output, 1);
  }

  /// <summary>Decodes a single ETC2 RGB with punchthrough alpha block (8 bytes) into 64 bytes of RGBA pixel data.</summary>
  /// <remarks>
  /// TODO: Implement proper punchthrough alpha where opaque=0 causes index 2 to produce
  /// transparent black (RGBA 0,0,0,0). Currently falls back to ETC1 with full alpha=255.
  /// </remarks>
  public static void DecodeEtc2RgbA1Block(ReadOnlySpan<byte> block, Span<byte> output)
    => Etc1Decoder.DecodeBlock(block, output);

  /// <summary>Decodes a full ETC2 RGB image (8 bytes/block) into RGBA pixel data.</summary>
  public static void DecodeEtc2RgbImage(ReadOnlySpan<byte> data, int width, int height, Span<byte> output)
    => _DecodeImage(data, width, height, 8, output, DecodeEtc2RgbBlock);

  /// <summary>Decodes a full ETC2 RGBA image (16 bytes/block) into RGBA pixel data.</summary>
  public static void DecodeEtc2RgbaImage(ReadOnlySpan<byte> data, int width, int height, Span<byte> output)
    => _DecodeImage(data, width, height, 16, output, DecodeEtc2RgbaBlock);

  /// <summary>Decodes a full EAC R11 image (8 bytes/block) into RGBA pixel data.</summary>
  public static void DecodeEacR11Image(ReadOnlySpan<byte> data, int width, int height, Span<byte> output)
    => _DecodeImage(data, width, height, 8, output, DecodeEacR11Block);

  /// <summary>Decodes a full EAC RG11 image (16 bytes/block) into RGBA pixel data.</summary>
  public static void DecodeEacRg11Image(ReadOnlySpan<byte> data, int width, int height, Span<byte> output)
    => _DecodeImage(data, width, height, 16, output, DecodeEacRg11Block);

  /// <summary>Decodes a full ETC2 punchthrough alpha image (8 bytes/block) into RGBA pixel data.</summary>
  public static void DecodeEtc2RgbA1Image(ReadOnlySpan<byte> data, int width, int height, Span<byte> output)
    => _DecodeImage(data, width, height, 8, output, DecodeEtc2RgbA1Block);

  /// <summary>Decodes EAC alpha data (8 bytes) and writes the alpha channel into existing RGBA output.</summary>
  private static void _DecodeEacAlpha(ReadOnlySpan<byte> block, Span<byte> output) {
    var baseAlpha = block[0];
    var multiplier = (block[1] >> 4) & 0xF;
    var tableIndex = block[1] & 0xF;
    var modifiers = _EacModifierTable[tableIndex];

    // Extract 3-bit indices for 16 pixels from bytes 2-7 (48 bits total, MSB first)
    // Bit layout is column-major: pixel (x,y) -> bit index x*4+y
    for (var x = 0; x < 4; ++x) {
      for (var y = 0; y < 4; ++y) {
        var pixelBitIndex = x * 4 + y;
        var bitOffset = pixelBitIndex * 3;
        var pixelIdx = _Extract3Bits(block.Slice(2, 6), bitOffset);
        var modifier = modifiers[pixelIdx];

        int alpha;
        if (multiplier != 0)
          alpha = Math.Clamp(baseAlpha + multiplier * modifier, 0, 255);
        else
          alpha = Math.Clamp(baseAlpha + modifier, 0, 255);

        var outOffset = (y * 4 + x) * 4 + 3;
        output[outOffset] = (byte)alpha;
      }
    }
  }

  /// <summary>Decodes an EAC channel (8 bytes) and writes values into a specific channel offset of existing RGBA output.</summary>
  private static void _DecodeEacChannel(ReadOnlySpan<byte> block, Span<byte> output, int channelOffset) {
    var baseValue = block[0];
    var multiplier = (block[1] >> 4) & 0xF;
    var tableIndex = block[1] & 0xF;
    var modifiers = _EacModifierTable[tableIndex];

    for (var x = 0; x < 4; ++x) {
      for (var y = 0; y < 4; ++y) {
        var pixelBitIndex = x * 4 + y;
        var bitOffset = pixelBitIndex * 3;
        var pixelIdx = _Extract3Bits(block.Slice(2, 6), bitOffset);
        var modifier = modifiers[pixelIdx];

        int value;
        if (multiplier != 0)
          value = Math.Clamp(baseValue + multiplier * modifier, 0, 255);
        else
          value = Math.Clamp(baseValue + modifier, 0, 255);

        var outOffset = (y * 4 + x) * 4 + channelOffset;
        output[outOffset] = (byte)value;
      }
    }
  }

  /// <summary>Extracts a 3-bit value from a 6-byte span at the given bit offset (MSB first, big-endian bit order).</summary>
  private static int _Extract3Bits(ReadOnlySpan<byte> data, int bitOffset) {
    // Total 48 bits packed into 6 bytes, MSB first
    var bytePos = bitOffset >> 3;
    var bitPos = bitOffset & 7;

    // Read 16 bits starting from the byte containing our 3-bit value
    int raw;
    if (bytePos + 1 < data.Length)
      raw = (data[bytePos] << 8) | data[bytePos + 1];
    else
      raw = data[bytePos] << 8;

    // Extract 3 bits at the correct position
    var shift = 16 - bitPos - 3;
    return (raw >> shift) & 7;
  }

  /// <summary>Generic block-based image decoder that iterates 4x4 blocks and copies to the output buffer.</summary>
  private static void _DecodeImage(ReadOnlySpan<byte> data, int width, int height, int blockBytes, Span<byte> output, _BlockDecoder decoder) {
    Span<byte> blockPixels = stackalloc byte[64];
    var blocksX = (width + 3) / 4;
    var blocksY = (height + 3) / 4;
    var blockIndex = 0;

    for (var by = 0; by < blocksY; ++by) {
      for (var bx = 0; bx < blocksX; ++bx) {
        var blockOffset = blockIndex * blockBytes;
        if (blockOffset + blockBytes > data.Length)
          return;

        decoder(data.Slice(blockOffset, blockBytes), blockPixels);

        var px = bx * 4;
        var py = by * 4;
        for (var y = 0; y < 4 && py + y < height; ++y)
          for (var x = 0; x < 4 && px + x < width; ++x) {
            var srcOffset = (y * 4 + x) * 4;
            var dstOffset = ((py + y) * width + (px + x)) * 4;
            output[dstOffset] = blockPixels[srcOffset];
            output[dstOffset + 1] = blockPixels[srcOffset + 1];
            output[dstOffset + 2] = blockPixels[srcOffset + 2];
            output[dstOffset + 3] = blockPixels[srcOffset + 3];
          }

        ++blockIndex;
      }
    }
  }

  private delegate void _BlockDecoder(ReadOnlySpan<byte> block, Span<byte> output);
}
