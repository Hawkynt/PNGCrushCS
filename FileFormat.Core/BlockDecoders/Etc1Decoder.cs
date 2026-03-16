using System;

namespace FileFormat.Core.BlockDecoders;

/// <summary>Decodes ETC1-compressed texture blocks (8 bytes per 4x4 pixel block, big-endian).</summary>
public static class Etc1Decoder {

  /// <summary>ETC1 intensity modifier table per the Khronos spec. Each entry yields modifiers { big, small, -small, -big }.</summary>
  private static readonly int[][] _ModifierTable = [
    [2, 8],
    [5, 17],
    [9, 29],
    [13, 42],
    [18, 60],
    [24, 80],
    [33, 106],
    [47, 183],
  ];

  /// <summary>Decodes a single 8-byte ETC1 block into 64 bytes of RGBA pixel data (4x4 pixels, row-major, 4 bytes/pixel).</summary>
  public static void DecodeBlock(ReadOnlySpan<byte> block, Span<byte> output) {
    var diff = (block[3] & 2) != 0;
    var flip = (block[3] & 1) != 0;

    int r1, g1, b1, r2, g2, b2;

    if (diff) {
      // Differential mode: 5-bit base + 3-bit signed delta
      r1 = (block[0] >> 3) & 0x1F;
      g1 = (block[1] >> 3) & 0x1F;
      b1 = (block[2] >> 3) & 0x1F;

      var dr = _SignExtend3(block[0] & 7);
      var dg = _SignExtend3(block[1] & 7);
      var db = _SignExtend3(block[2] & 7);

      r2 = r1 + dr;
      g2 = g1 + dg;
      b2 = b1 + db;

      // Expand 5-bit to 8-bit
      r1 = (r1 << 3) | (r1 >> 2);
      g1 = (g1 << 3) | (g1 >> 2);
      b1 = (b1 << 3) | (b1 >> 2);
      r2 = (r2 << 3) | (r2 >> 2);
      g2 = (g2 << 3) | (g2 >> 2);
      b2 = (b2 << 3) | (b2 >> 2);
    } else {
      // Individual mode: two independent 4-bit colors
      r1 = (block[0] >> 4) & 0xF;
      g1 = (block[1] >> 4) & 0xF;
      b1 = (block[2] >> 4) & 0xF;
      r2 = block[0] & 0xF;
      g2 = block[1] & 0xF;
      b2 = block[2] & 0xF;

      // Expand 4-bit to 8-bit
      r1 = (r1 << 4) | r1;
      g1 = (g1 << 4) | g1;
      b1 = (b1 << 4) | b1;
      r2 = (r2 << 4) | r2;
      g2 = (g2 << 4) | g2;
      b2 = (b2 << 4) | b2;
    }

    var cw1 = (block[3] >> 5) & 7;
    var cw2 = (block[3] >> 2) & 7;

    var mod1 = _ModifierTable[cw1];
    var mod2 = _ModifierTable[cw2];

    for (var y = 0; y < 4; ++y) {
      for (var x = 0; x < 4; ++x) {
        // Pixel index bits are in column-major order
        var idx = x * 4 + y;
        var msb = (block[4 + (idx >> 3)] >> (7 - (idx & 7))) & 1;
        var lsb = (block[6 + (idx >> 3)] >> (7 - (idx & 7))) & 1;
        var pixelIndex = (msb << 1) | lsb;

        // Sub-block assignment
        bool useSubBlock1;
        if (flip)
          useSubBlock1 = y < 2;
        else
          useSubBlock1 = x < 2;

        int baseR, baseG, baseB;
        int[] modTable;
        if (useSubBlock1) {
          baseR = r1;
          baseG = g1;
          baseB = b1;
          modTable = mod1;
        } else {
          baseR = r2;
          baseG = g2;
          baseB = b2;
          modTable = mod2;
        }

        // Modifier mapping: 0=+big, 1=+small, 2=-small, 3=-big
        var modifier = pixelIndex switch {
          0 => modTable[1],
          1 => modTable[0],
          2 => -modTable[0],
          _ => -modTable[1],
        };

        var outOffset = (y * 4 + x) * 4;
        output[outOffset] = _Clamp(baseR + modifier);
        output[outOffset + 1] = _Clamp(baseG + modifier);
        output[outOffset + 2] = _Clamp(baseB + modifier);
        output[outOffset + 3] = 255;
      }
    }
  }

  /// <summary>Decodes a full ETC1 image from compressed data into RGBA pixel data.</summary>
  public static void DecodeImage(ReadOnlySpan<byte> data, int width, int height, Span<byte> output) {
    Span<byte> blockPixels = stackalloc byte[64];
    var blocksX = (width + 3) / 4;
    var blocksY = (height + 3) / 4;
    var blockIndex = 0;

    for (var by = 0; by < blocksY; ++by) {
      for (var bx = 0; bx < blocksX; ++bx) {
        var blockOffset = blockIndex * 8;
        if (blockOffset + 8 > data.Length)
          return;

        DecodeBlock(data.Slice(blockOffset, 8), blockPixels);

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

  /// <summary>Sign-extends a 3-bit value to a signed integer.</summary>
  private static int _SignExtend3(int value) => value >= 4 ? value - 8 : value;

  /// <summary>Clamps an integer to [0, 255] and returns as byte.</summary>
  private static byte _Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
}
