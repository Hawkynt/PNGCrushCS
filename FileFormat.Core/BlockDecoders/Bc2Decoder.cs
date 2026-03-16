using System;

namespace FileFormat.Core.BlockDecoders;

/// <summary>Decodes BC2/DXT3 compressed blocks to RGBA32 pixels.</summary>
public static class Bc2Decoder {

  private const int _BLOCK_SIZE = 16;
  private const int _BLOCK_DIM = 4;
  private const int _PIXELS_PER_BLOCK = _BLOCK_DIM * _BLOCK_DIM;
  private const int _BYTES_PER_PIXEL = 4;

  /// <summary>Decodes a single 4x4 BC2 block (16 bytes) to 64 bytes of RGBA32 pixel data.</summary>
  public static void DecodeBlock(ReadOnlySpan<byte> block, Span<byte> output) {
    Bc1Decoder.DecodeColorBlock(block.Slice(8, 8), output);

    for (var i = 0; i < _PIXELS_PER_BLOCK; ++i) {
      var byteIndex = i / 2;
      var a4 = (i & 1) == 0 ? block[byteIndex] & 0x0F : (block[byteIndex] >> 4) & 0x0F;
      output[i * _BYTES_PER_PIXEL + 3] = (byte)((a4 << 4) | a4);
    }
  }

  /// <summary>Decodes a full BC2-compressed image to RGBA32 scanline-order pixel data.</summary>
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
}
