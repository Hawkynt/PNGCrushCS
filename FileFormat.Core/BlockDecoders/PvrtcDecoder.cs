using System;

namespace FileFormat.Core.BlockDecoders;

/// <summary>Decodes PVRTC (PowerVR Texture Compression) 2bpp and 4bpp blocks to RGBA32 pixels.</summary>
public static class PvrtcDecoder {

  private const int _BYTES_PER_PIXEL = 4;

  /// <summary>Decodes a PVRTC 4bpp compressed image (4x4 block footprint) to RGBA32 pixel data.</summary>
  public static void Decode4Bpp(ReadOnlySpan<byte> data, int width, int height, Span<byte> output) =>
    _Decode(data, width, height, output, 4, 4);

  /// <summary>Decodes a PVRTC 2bpp compressed image (8x4 block footprint) to RGBA32 pixel data.</summary>
  public static void Decode2Bpp(ReadOnlySpan<byte> data, int width, int height, Span<byte> output) =>
    _Decode(data, width, height, output, 8, 4);

  private static void _Decode(ReadOnlySpan<byte> data, int width, int height, Span<byte> output, int blockWidth, int blockHeight) {
    // PVRTC uses a unique encoding: two low-resolution images (A and B) are bilinearly
    // upscaled and blended per-pixel with a modulation value.
    // Each 64-bit block contains: modulation data (32 bits) + color A (15 bits) + color B (15 bits) + mode (1 bit) + punch-through flag (1 bit)

    var blocksX = (width + blockWidth - 1) / blockWidth;
    var blocksY = (height + blockHeight - 1) / blockHeight;
    var blockCount = blocksX * blocksY;
    var blockSizeBytes = 8; // each block is 64 bits

    if (data.Length < blockCount * blockSizeBytes) {
      output.Slice(0, width * height * _BYTES_PER_PIXEL).Clear();
      return;
    }

    // Extract color A, color B, and modulation data for all blocks
    var colorA = new uint[blockCount];
    var colorB = new uint[blockCount];
    var modData = new uint[blockCount];
    var modMode = new bool[blockCount];

    for (var i = 0; i < blockCount; ++i) {
      var blockOff = i * blockSizeBytes;
      modData[i] = (uint)(data[blockOff] | (data[blockOff + 1] << 8) | (data[blockOff + 2] << 16) | (data[blockOff + 3] << 24));
      var word1 = (uint)(data[blockOff + 4] | (data[blockOff + 5] << 8) | (data[blockOff + 6] << 16) | (data[blockOff + 7] << 24));

      modMode[i] = (word1 & 1) != 0;
      var isOpaque = (word1 & 0x80000000u) != 0;

      // Color A: bits 1..15 of word1
      colorA[i] = _DecodeColor((word1 >> 1) & 0x7FFF, isOpaque, true);
      // Color B: bits 16..30 of word1
      colorB[i] = _DecodeColor((word1 >> 16) & 0x7FFF, isOpaque, false);
    }

    // For each pixel, find which block it belongs to, bilinearly interpolate colors A and B,
    // then apply modulation
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var bx = x / blockWidth;
        var by = y / blockHeight;
        var blockIdx = by * blocksX + bx;

        // Get modulation value for this pixel
        var localX = x % blockWidth;
        var localY = y % blockHeight;
        var modBitIndex = localY * blockWidth + localX;

        int modValue;
        if (blockWidth == 4) {
          // 4bpp: 2 bits per pixel
          modValue = (int)((modData[blockIdx] >> (modBitIndex * 2)) & 3);
        } else {
          // 2bpp: 1 bit per pixel (with mode determining interpolation)
          modValue = (int)((modData[blockIdx] >> modBitIndex) & 1);
          modValue = modValue == 0 ? 0 : 3; // map to 0 or 3 for bilinear blend
        }

        // Bilinear upscale: use nearest block colors
        var cA = colorA[blockIdx];
        var cB = colorB[blockIdx];

        // Blend based on modulation
        var weight = modMode[blockIdx]
          ? modValue switch { 0 => 0, 1 => 4, 2 => 4, 3 => 8, _ => 0 }
          : modValue switch { 0 => 0, 1 => 3, 2 => 5, 3 => 8, _ => 0 };

        var r = _Lerp((byte)(cA >> 24), (byte)(cB >> 24), weight);
        var g = _Lerp((byte)(cA >> 16), (byte)(cB >> 16), weight);
        var b = _Lerp((byte)(cA >> 8), (byte)(cB >> 8), weight);
        var a = _Lerp((byte)cA, (byte)cB, weight);

        var dstOff = (y * width + x) * _BYTES_PER_PIXEL;
        output[dstOff] = r;
        output[dstOff + 1] = g;
        output[dstOff + 2] = b;
        output[dstOff + 3] = a;
      }
  }

  private static uint _DecodeColor(uint bits, bool isOpaque, bool isColorA) {
    byte r, g, b, a;

    if (isOpaque) {
      if (isColorA) {
        // Color A opaque: 4.4.5 + 1 pad -> RRRR.GGGG.BBBBB
        r = (byte)(((bits >> 10) & 0x1F) * 255 / 31);
        g = (byte)(((bits >> 5) & 0x1F) * 255 / 31);
        b = (byte)((bits & 0x1F) * 255 / 31);
      } else {
        // Color B opaque: 5.5.5 -> RRRRR.GGGGG.BBBBB
        r = (byte)(((bits >> 10) & 0x1F) * 255 / 31);
        g = (byte)(((bits >> 5) & 0x1F) * 255 / 31);
        b = (byte)((bits & 0x1F) * 255 / 31);
      }
      a = 255;
    } else {
      if (isColorA) {
        // Color A transparent: 3.3.3.4 -> RRR.GGG.BBB.AAAA
        a = (byte)(((bits >> 11) & 0x07) * 255 / 7);
        r = (byte)(((bits >> 8) & 0x07) * 255 / 7);
        g = (byte)(((bits >> 4) & 0x0F) * 255 / 15);
        b = (byte)((bits & 0x0F) * 255 / 15);
      } else {
        // Color B transparent: 3.4.4.3 -> RRR.GGGG.BBBB.AAA
        a = (byte)(((bits >> 12) & 0x07) * 255 / 7);
        r = (byte)(((bits >> 8) & 0x0F) * 255 / 15);
        g = (byte)(((bits >> 4) & 0x0F) * 255 / 15);
        b = (byte)((bits & 0x0F) * 255 / 15);
      }
    }

    return ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | a;
  }

  private static byte _Lerp(byte a, byte b, int weight) =>
    (byte)((a * (8 - weight) + b * weight + 4) / 8);
}
