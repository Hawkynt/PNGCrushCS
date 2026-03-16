using System;

namespace FileFormat.Core.BlockDecoders;

/// <summary>Decodes BC1/DXT1 compressed blocks to RGBA32 pixels.</summary>
public static class Bc1Decoder {

  private const int _BLOCK_SIZE = 8;
  private const int _BLOCK_DIM = 4;
  private const int _PIXELS_PER_BLOCK = _BLOCK_DIM * _BLOCK_DIM;
  private const int _BYTES_PER_PIXEL = 4;

  /// <summary>Decodes a single 4x4 BC1 block (8 bytes) to 64 bytes of RGBA32 pixel data.</summary>
  public static void DecodeBlock(ReadOnlySpan<byte> block, Span<byte> output) {
    var c0Raw = (ushort)(block[0] | (block[1] << 8));
    var c1Raw = (ushort)(block[2] | (block[3] << 8));

    _DecodeRgb565(c0Raw, out var r0, out var g0, out var b0);
    _DecodeRgb565(c1Raw, out var r1, out var g1, out var b1);

    Span<byte> palette = stackalloc byte[_PIXELS_PER_BLOCK];
    palette[0] = r0;
    palette[1] = g0;
    palette[2] = b0;
    palette[3] = 255;
    palette[4] = r1;
    palette[5] = g1;
    palette[6] = b1;
    palette[7] = 255;

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
      palette[15] = 0;
    }

    for (var i = 0; i < _PIXELS_PER_BLOCK; ++i) {
      var byteIndex = i / 4;
      var bitIndex = (i % 4) * 2;
      var index = (block[4 + byteIndex] >> bitIndex) & 0x03;
      var srcOffset = index * _BYTES_PER_PIXEL;
      var dstOffset = i * _BYTES_PER_PIXEL;
      output[dstOffset] = palette[srcOffset];
      output[dstOffset + 1] = palette[srcOffset + 1];
      output[dstOffset + 2] = palette[srcOffset + 2];
      output[dstOffset + 3] = palette[srcOffset + 3];
    }
  }

  /// <summary>Decodes a full BC1-compressed image to RGBA32 scanline-order pixel data.</summary>
  public static void DecodeImage(ReadOnlySpan<byte> data, int width, int height, Span<byte> output) {
    var blocksX = (width + _BLOCK_DIM - 1) / _BLOCK_DIM;
    var blocksY = (height + _BLOCK_DIM - 1) / _BLOCK_DIM;
    var stride = width * _BYTES_PER_PIXEL;
    Span<byte> blockPixels = stackalloc byte[_PIXELS_PER_BLOCK * _BYTES_PER_PIXEL];

    for (var by = 0; by < blocksY; ++by)
      for (var bx = 0; bx < blocksX; ++bx) {
        var blockOffset = (by * blocksX + bx) * _BLOCK_SIZE;
        DecodeBlock(data.Slice(blockOffset, _BLOCK_SIZE), blockPixels);

        for (var py = 0; py < _BLOCK_DIM; ++py) {
          var imgY = by * _BLOCK_DIM + py;
          if (imgY >= height)
            break;

          for (var px = 0; px < _BLOCK_DIM; ++px) {
            var imgX = bx * _BLOCK_DIM + px;
            if (imgX >= width)
              break;

            var srcOffset = (py * _BLOCK_DIM + px) * _BYTES_PER_PIXEL;
            var dstOffset = imgY * stride + imgX * _BYTES_PER_PIXEL;
            output[dstOffset] = blockPixels[srcOffset];
            output[dstOffset + 1] = blockPixels[srcOffset + 1];
            output[dstOffset + 2] = blockPixels[srcOffset + 2];
            output[dstOffset + 3] = blockPixels[srcOffset + 3];
          }
        }
      }
  }

  internal static void DecodeColorBlock(ReadOnlySpan<byte> block, Span<byte> output) {
    var c0Raw = (ushort)(block[0] | (block[1] << 8));
    var c1Raw = (ushort)(block[2] | (block[3] << 8));

    _DecodeRgb565(c0Raw, out var r0, out var g0, out var b0);
    _DecodeRgb565(c1Raw, out var r1, out var g1, out var b1);

    Span<byte> palette = stackalloc byte[_PIXELS_PER_BLOCK];
    palette[0] = r0;
    palette[1] = g0;
    palette[2] = b0;
    palette[3] = 255;
    palette[4] = r1;
    palette[5] = g1;
    palette[6] = b1;
    palette[7] = 255;
    palette[8] = (byte)((2 * r0 + r1 + 1) / 3);
    palette[9] = (byte)((2 * g0 + g1 + 1) / 3);
    palette[10] = (byte)((2 * b0 + b1 + 1) / 3);
    palette[11] = 255;
    palette[12] = (byte)((r0 + 2 * r1 + 1) / 3);
    palette[13] = (byte)((g0 + 2 * g1 + 1) / 3);
    palette[14] = (byte)((b0 + 2 * b1 + 1) / 3);
    palette[15] = 255;

    for (var i = 0; i < _PIXELS_PER_BLOCK; ++i) {
      var byteIndex = i / 4;
      var bitIndex = (i % 4) * 2;
      var index = (block[4 + byteIndex] >> bitIndex) & 0x03;
      var srcOffset = index * _BYTES_PER_PIXEL;
      var dstOffset = i * _BYTES_PER_PIXEL;
      output[dstOffset] = palette[srcOffset];
      output[dstOffset + 1] = palette[srcOffset + 1];
      output[dstOffset + 2] = palette[srcOffset + 2];
      output[dstOffset + 3] = palette[srcOffset + 3];
    }
  }

  private static void _DecodeRgb565(ushort value, out byte r, out byte g, out byte b) {
    var r5 = (value >> 11) & 0x1F;
    var g6 = (value >> 5) & 0x3F;
    var b5 = value & 0x1F;
    r = (byte)((r5 << 3) | (r5 >> 2));
    g = (byte)((g6 << 2) | (g6 >> 4));
    b = (byte)((b5 << 3) | (b5 >> 2));
  }
}
