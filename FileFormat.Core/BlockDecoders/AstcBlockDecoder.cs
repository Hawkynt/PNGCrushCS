using System;

namespace FileFormat.Core.BlockDecoders;

/// <summary>
/// Decodes ASTC-compressed texture blocks (16 bytes per variable-size block).
/// Currently handles void-extent blocks (single-color fill) and produces a magenta/black
/// checkerboard placeholder for all other block types.
/// </summary>
/// <remarks>
/// TODO: Full ASTC decoding requires integer sequence encoding (quints/trits/bits),
/// weight grid interpolation, multi-partition support, dual-plane modes, and
/// color endpoint decoding. This simplified decoder covers void-extent blocks
/// and provides a visible placeholder for unsupported modes.
/// </remarks>
public static class AstcBlockDecoder {

  /// <summary>Decodes a single 16-byte ASTC block into RGBA pixel data for a block of the given dimensions.</summary>
  public static void DecodeBlock(ReadOnlySpan<byte> block, int blockWidth, int blockHeight, Span<byte> output) {
    var pixelCount = blockWidth * blockHeight;

    // Void-extent detection: bits [7:2] all set in byte 0 indicates a void-extent block (2D)
    if ((block[0] & 0xFC) == 0xFC) {
      // Void-extent block: RGBA16 values at bytes 8-15 (little-endian), take high byte for 8-bit
      var r = block[9];
      var g = block[11];
      var b = block[13];
      var a = block[15];

      for (var i = 0; i < pixelCount; ++i) {
        var offset = i * 4;
        output[offset] = r;
        output[offset + 1] = g;
        output[offset + 2] = b;
        output[offset + 3] = a;
      }

      return;
    }

    // Non-void-extent: output magenta/black checkerboard as a placeholder for unimplemented modes
    for (var i = 0; i < pixelCount; ++i) {
      var x = i % blockWidth;
      var y = i / blockWidth;
      var checker = (x + y) & 1;
      var offset = i * 4;
      output[offset] = checker == 0 ? (byte)255 : (byte)0;
      output[offset + 1] = 0;
      output[offset + 2] = checker == 0 ? (byte)255 : (byte)0;
      output[offset + 3] = 255;
    }
  }

  /// <summary>Decodes a full ASTC image from compressed data into RGBA pixel data.</summary>
  public static void DecodeImage(ReadOnlySpan<byte> data, int width, int height, int blockWidth, int blockHeight, Span<byte> output) {
    var blockPixelCount = blockWidth * blockHeight;
    var blockPixelBytes = blockPixelCount * 4;

    // Rent from stack if block is small enough, otherwise allocate
    Span<byte> blockPixels = blockPixelBytes <= 576
      ? stackalloc byte[blockPixelBytes]
      : new byte[blockPixelBytes];

    var blocksX = (width + blockWidth - 1) / blockWidth;
    var blocksY = (height + blockHeight - 1) / blockHeight;
    var blockIndex = 0;

    for (var by = 0; by < blocksY; ++by) {
      for (var bx = 0; bx < blocksX; ++bx) {
        var blockOffset = blockIndex * 16;
        if (blockOffset + 16 > data.Length)
          return;

        DecodeBlock(data.Slice(blockOffset, 16), blockWidth, blockHeight, blockPixels);

        var px = bx * blockWidth;
        var py = by * blockHeight;
        for (var y = 0; y < blockHeight && py + y < height; ++y)
          for (var x = 0; x < blockWidth && px + x < width; ++x) {
            var srcOffset = (y * blockWidth + x) * 4;
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
}
