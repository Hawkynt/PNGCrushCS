using System;

namespace FileFormat.Core.BlockDecoders;

/// <summary>Decodes BC3/DXT5 compressed blocks to RGBA32 pixels.</summary>
public static class Bc3Decoder {

  private const int _BLOCK_SIZE = 16;
  private const int _BLOCK_DIM = 4;
  private const int _PIXELS_PER_BLOCK = _BLOCK_DIM * _BLOCK_DIM;
  private const int _BYTES_PER_PIXEL = 4;

  /// <summary>Decodes a single 4x4 BC3 block (16 bytes) to 64 bytes of RGBA32 pixel data.</summary>
  public static void DecodeBlock(ReadOnlySpan<byte> block, Span<byte> output) {
    Bc1Decoder.DecodeColorBlock(block.Slice(8, 8), output);

    Span<byte> alphas = stackalloc byte[8];
    _InterpolateAlphas(block[0], block[1], alphas);

    Span<byte> indices = stackalloc byte[_PIXELS_PER_BLOCK];
    _Unpack3BitIndices(block.Slice(2, 6), indices);

    for (var i = 0; i < _PIXELS_PER_BLOCK; ++i)
      output[i * _BYTES_PER_PIXEL + 3] = alphas[indices[i]];
  }

  /// <summary>Decodes a full BC3-compressed image to RGBA32 scanline-order pixel data.</summary>
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

  internal static void _InterpolateAlphas(byte alpha0, byte alpha1, Span<byte> alphas) {
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
  }

  internal static void _Unpack3BitIndices(ReadOnlySpan<byte> packed, Span<byte> indices) {
    var bits0 = (uint)packed[0] | ((uint)packed[1] << 8) | ((uint)packed[2] << 16);
    for (var i = 0; i < 8; ++i)
      indices[i] = (byte)((bits0 >> (i * 3)) & 0x07);

    var bits1 = (uint)packed[3] | ((uint)packed[4] << 8) | ((uint)packed[5] << 16);
    for (var i = 0; i < 8; ++i)
      indices[8 + i] = (byte)((bits1 >> (i * 3)) & 0x07);
  }
}
