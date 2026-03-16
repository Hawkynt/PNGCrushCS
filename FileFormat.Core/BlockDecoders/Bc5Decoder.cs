using System;

namespace FileFormat.Core.BlockDecoders;

/// <summary>Decodes BC5 (dual-channel) compressed blocks to RGBA32 pixels.</summary>
public static class Bc5Decoder {

  private const int _BLOCK_SIZE = 16;
  private const int _BLOCK_DIM = 4;
  private const int _PIXELS_PER_BLOCK = _BLOCK_DIM * _BLOCK_DIM;
  private const int _BYTES_PER_PIXEL = 4;

  /// <summary>Decodes a single 4x4 BC5 block (16 bytes) to 64 bytes of RGBA32 pixel data (R from first block, G from second, B=0, A=255).</summary>
  public static void DecodeBlock(ReadOnlySpan<byte> block, Span<byte> output) {
    Span<byte> redValues = stackalloc byte[8];
    Bc3Decoder._InterpolateAlphas(block[0], block[1], redValues);

    Span<byte> redIndices = stackalloc byte[_PIXELS_PER_BLOCK];
    Bc3Decoder._Unpack3BitIndices(block.Slice(2, 6), redIndices);

    Span<byte> greenValues = stackalloc byte[8];
    Bc3Decoder._InterpolateAlphas(block[8], block[9], greenValues);

    Span<byte> greenIndices = stackalloc byte[_PIXELS_PER_BLOCK];
    Bc3Decoder._Unpack3BitIndices(block.Slice(10, 6), greenIndices);

    for (var i = 0; i < _PIXELS_PER_BLOCK; ++i) {
      var dstOffset = i * _BYTES_PER_PIXEL;
      output[dstOffset] = redValues[redIndices[i]];
      output[dstOffset + 1] = greenValues[greenIndices[i]];
      output[dstOffset + 2] = 0;
      output[dstOffset + 3] = 255;
    }
  }

  /// <summary>Decodes a full BC5-compressed image to RGBA32 scanline-order pixel data.</summary>
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
