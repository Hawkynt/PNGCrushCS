using System;

namespace FileFormat.Blp;

/// <summary>Decodes DXT1/DXT3/DXT5 block-compressed pixel data to BGRA32.</summary>
internal static class BlpDxtDecoder {

  private const int _BLOCK_DIM = 4;

  /// <summary>Decodes an RGB565 packed color to its R, G, B components.</summary>
  internal static (byte R, byte G, byte B) DecodeRgb565(ushort value) {
    var r5 = (value >> 11) & 0x1F;
    var g6 = (value >> 5) & 0x3F;
    var b5 = value & 0x1F;
    return (
      (byte)((r5 << 3) | (r5 >> 2)),
      (byte)((g6 << 2) | (g6 >> 4)),
      (byte)((b5 << 3) | (b5 >> 2))
    );
  }

  /// <summary>Decodes a DXT1 color block (8 bytes) and writes BGRA pixels into the output buffer.</summary>
  internal static void DecodeDxt1Block(ReadOnlySpan<byte> block, Span<byte> bgra, int stride, int blockX, int blockY, int imgWidth, int imgHeight) {
    var c0Raw = (ushort)(block[0] | (block[1] << 8));
    var c1Raw = (ushort)(block[2] | (block[3] << 8));

    var (r0, g0, b0) = DecodeRgb565(c0Raw);
    var (r1, g1, b1) = DecodeRgb565(c1Raw);

    Span<byte> palette = stackalloc byte[16]; // 4 entries x 4 (R,G,B,A)
    palette[0] = r0; palette[1] = g0; palette[2] = b0; palette[3] = 255;
    palette[4] = r1; palette[5] = g1; palette[6] = b1; palette[7] = 255;

    if (c0Raw > c1Raw) {
      palette[8] = (byte)((2 * r0 + r1 + 1) / 3);
      palette[9] = (byte)((2 * g0 + g1 + 1) / 3);
      palette[10] = (byte)((2 * b0 + b1 + 1) / 3);
      palette[11] = 255;
      palette[12] = (byte)((r0 + 2 * r1 + 1) / 3);
      palette[13] = (byte)((g0 + 2 * g1 + 1) / 3);
      palette[14] = (byte)((b0 + 2 * b1 + 1) / 3);
      palette[15] = 255;
    } else {
      palette[8] = (byte)((r0 + r1) / 2);
      palette[9] = (byte)((g0 + g1) / 2);
      palette[10] = (byte)((b0 + b1) / 2);
      palette[11] = 255;
      palette[12] = 0;
      palette[13] = 0;
      palette[14] = 0;
      palette[15] = 0; // transparent black
    }

    for (var py = 0; py < _BLOCK_DIM; ++py) {
      var imgY = blockY * _BLOCK_DIM + py;
      if (imgY >= imgHeight)
        break;

      var bits = block[4 + py];
      for (var px = 0; px < _BLOCK_DIM; ++px) {
        var imgX = blockX * _BLOCK_DIM + px;
        if (imgX >= imgWidth)
          continue;

        var index = (bits >> (px * 2)) & 0x03;
        var palOff = index * 4;
        var dst = imgY * stride + imgX * 4;
        bgra[dst] = palette[palOff + 2];     // B
        bgra[dst + 1] = palette[palOff + 1]; // G
        bgra[dst + 2] = palette[palOff];     // R
        bgra[dst + 3] = palette[palOff + 3]; // A
      }
    }
  }

  /// <summary>Decodes a DXT3 block (16 bytes) and writes BGRA pixels into the output buffer.</summary>
  internal static void DecodeDxt3Block(ReadOnlySpan<byte> block, Span<byte> bgra, int stride, int blockX, int blockY, int imgWidth, int imgHeight) {
    // First decode color from the second 8 bytes (same as DXT1 color, but always opaque interpolation)
    var colorBlock = block.Slice(8, 8);
    var c0Raw = (ushort)(colorBlock[0] | (colorBlock[1] << 8));
    var c1Raw = (ushort)(colorBlock[2] | (colorBlock[3] << 8));

    var (r0, g0, b0) = DecodeRgb565(c0Raw);
    var (r1, g1, b1) = DecodeRgb565(c1Raw);

    Span<byte> palette = stackalloc byte[12]; // 4 entries x 3 (R,G,B)
    palette[0] = r0; palette[1] = g0; palette[2] = b0;
    palette[3] = r1; palette[4] = g1; palette[5] = b1;
    palette[6] = (byte)((2 * r0 + r1 + 1) / 3);
    palette[7] = (byte)((2 * g0 + g1 + 1) / 3);
    palette[8] = (byte)((2 * b0 + b1 + 1) / 3);
    palette[9] = (byte)((r0 + 2 * r1 + 1) / 3);
    palette[10] = (byte)((g0 + 2 * g1 + 1) / 3);
    palette[11] = (byte)((b0 + 2 * b1 + 1) / 3);

    for (var py = 0; py < _BLOCK_DIM; ++py) {
      var imgY = blockY * _BLOCK_DIM + py;
      if (imgY >= imgHeight)
        break;

      var colorBits = colorBlock[4 + py];
      // Alpha: 2 bytes per row of 4 pixels, each pixel = 4-bit alpha
      var alphaByteIndex = py * 2;
      var alphaWord = (ushort)(block[alphaByteIndex] | (block[alphaByteIndex + 1] << 8));

      for (var px = 0; px < _BLOCK_DIM; ++px) {
        var imgX = blockX * _BLOCK_DIM + px;
        if (imgX >= imgWidth)
          continue;

        var colorIndex = (colorBits >> (px * 2)) & 0x03;
        var palOff = colorIndex * 3;
        var a4 = (alphaWord >> (px * 4)) & 0x0F;
        var alpha = (byte)((a4 << 4) | a4);

        var dst = imgY * stride + imgX * 4;
        bgra[dst] = palette[palOff + 2];     // B
        bgra[dst + 1] = palette[palOff + 1]; // G
        bgra[dst + 2] = palette[palOff];     // R
        bgra[dst + 3] = alpha;               // A
      }
    }
  }

  /// <summary>Decodes a DXT5 block (16 bytes) and writes BGRA pixels into the output buffer.</summary>
  internal static void DecodeDxt5Block(ReadOnlySpan<byte> block, Span<byte> bgra, int stride, int blockX, int blockY, int imgWidth, int imgHeight) {
    // Alpha interpolation
    var alpha0 = block[0];
    var alpha1 = block[1];

    Span<byte> alphas = stackalloc byte[8];
    alphas[0] = alpha0;
    alphas[1] = alpha1;

    if (alpha0 > alpha1) {
      alphas[2] = (byte)((6 * alpha0 + 1 * alpha1 + 3) / 7);
      alphas[3] = (byte)((5 * alpha0 + 2 * alpha1 + 3) / 7);
      alphas[4] = (byte)((4 * alpha0 + 3 * alpha1 + 3) / 7);
      alphas[5] = (byte)((3 * alpha0 + 4 * alpha1 + 3) / 7);
      alphas[6] = (byte)((2 * alpha0 + 5 * alpha1 + 3) / 7);
      alphas[7] = (byte)((1 * alpha0 + 6 * alpha1 + 3) / 7);
    } else {
      alphas[2] = (byte)((4 * alpha0 + 1 * alpha1 + 2) / 5);
      alphas[3] = (byte)((3 * alpha0 + 2 * alpha1 + 2) / 5);
      alphas[4] = (byte)((2 * alpha0 + 3 * alpha1 + 2) / 5);
      alphas[5] = (byte)((1 * alpha0 + 4 * alpha1 + 2) / 5);
      alphas[6] = 0;
      alphas[7] = 255;
    }

    // Unpack 48 bits (6 bytes) of 3-bit alpha indices for 16 pixels
    Span<byte> alphaIndices = stackalloc byte[16];
    var bits0 = (uint)block[2] | ((uint)block[3] << 8) | ((uint)block[4] << 16);
    for (var i = 0; i < 8; ++i)
      alphaIndices[i] = (byte)((bits0 >> (i * 3)) & 0x07);

    var bits1 = (uint)block[5] | ((uint)block[6] << 8) | ((uint)block[7] << 16);
    for (var i = 0; i < 8; ++i)
      alphaIndices[8 + i] = (byte)((bits1 >> (i * 3)) & 0x07);

    // Color block (bytes 8-15, same as DXT1 but always opaque interpolation)
    var colorBlock = block.Slice(8, 8);
    var c0Raw = (ushort)(colorBlock[0] | (colorBlock[1] << 8));
    var c1Raw = (ushort)(colorBlock[2] | (colorBlock[3] << 8));

    var (r0, g0, b0) = DecodeRgb565(c0Raw);
    var (r1, g1, b1) = DecodeRgb565(c1Raw);

    Span<byte> palette = stackalloc byte[12];
    palette[0] = r0; palette[1] = g0; palette[2] = b0;
    palette[3] = r1; palette[4] = g1; palette[5] = b1;
    palette[6] = (byte)((2 * r0 + r1 + 1) / 3);
    palette[7] = (byte)((2 * g0 + g1 + 1) / 3);
    palette[8] = (byte)((2 * b0 + b1 + 1) / 3);
    palette[9] = (byte)((r0 + 2 * r1 + 1) / 3);
    palette[10] = (byte)((g0 + 2 * g1 + 1) / 3);
    palette[11] = (byte)((b0 + 2 * b1 + 1) / 3);

    for (var py = 0; py < _BLOCK_DIM; ++py) {
      var imgY = blockY * _BLOCK_DIM + py;
      if (imgY >= imgHeight)
        break;

      var colorBits = colorBlock[4 + py];
      for (var px = 0; px < _BLOCK_DIM; ++px) {
        var imgX = blockX * _BLOCK_DIM + px;
        if (imgX >= imgWidth)
          continue;

        var colorIndex = (colorBits >> (px * 2)) & 0x03;
        var palOff = colorIndex * 3;
        var alphaIndex = alphaIndices[py * _BLOCK_DIM + px];

        var dst = imgY * stride + imgX * 4;
        bgra[dst] = palette[palOff + 2];     // B
        bgra[dst + 1] = palette[palOff + 1]; // G
        bgra[dst + 2] = palette[palOff];     // R
        bgra[dst + 3] = alphas[alphaIndex];  // A
      }
    }
  }

  /// <summary>Decodes a full DXT1-compressed image to BGRA32 pixel data.</summary>
  internal static byte[] DecodeDxt1Image(ReadOnlySpan<byte> data, int width, int height) {
    var stride = width * 4;
    var output = new byte[height * stride];
    var blocksX = (width + 3) / 4;
    var blocksY = (height + 3) / 4;

    for (var by = 0; by < blocksY; ++by)
      for (var bx = 0; bx < blocksX; ++bx) {
        var offset = (by * blocksX + bx) * 8;
        if (offset + 8 > data.Length)
          break;
        DecodeDxt1Block(data.Slice(offset, 8), output, stride, bx, by, width, height);
      }

    return output;
  }

  /// <summary>Decodes a full DXT3-compressed image to BGRA32 pixel data.</summary>
  internal static byte[] DecodeDxt3Image(ReadOnlySpan<byte> data, int width, int height) {
    var stride = width * 4;
    var output = new byte[height * stride];
    var blocksX = (width + 3) / 4;
    var blocksY = (height + 3) / 4;

    for (var by = 0; by < blocksY; ++by)
      for (var bx = 0; bx < blocksX; ++bx) {
        var offset = (by * blocksX + bx) * 16;
        if (offset + 16 > data.Length)
          break;
        DecodeDxt3Block(data.Slice(offset, 16), output, stride, bx, by, width, height);
      }

    return output;
  }

  /// <summary>Decodes a full DXT5-compressed image to BGRA32 pixel data.</summary>
  internal static byte[] DecodeDxt5Image(ReadOnlySpan<byte> data, int width, int height) {
    var stride = width * 4;
    var output = new byte[height * stride];
    var blocksX = (width + 3) / 4;
    var blocksY = (height + 3) / 4;

    for (var by = 0; by < blocksY; ++by)
      for (var bx = 0; bx < blocksX; ++bx) {
        var offset = (by * blocksX + bx) * 16;
        if (offset + 16 > data.Length)
          break;
        DecodeDxt5Block(data.Slice(offset, 16), output, stride, bx, by, width, height);
      }

    return output;
  }
}
