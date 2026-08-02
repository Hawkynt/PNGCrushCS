using System;

namespace FileFormat.Core.BlockDecoders;

/// <summary>
/// Decodes ASTC-compressed texture blocks (16 bytes per variable-size block).
/// </summary>
/// <remarks>
/// Only void-extent blocks — the ones holding a single colour — are decoded. Everything else needs
/// the whole of ASTC: integer sequences in trits and quints, a weight grid to interpolate, several
/// partitions, dual-plane modes and endpoint decoding. None of that is here.
/// <para/>
/// What it used to do with a block it could not read was fill it with magenta and say nothing, so a
/// picture came back looking decoded and the caller had no way to tell. A file of ordinary ASTC came
/// out as a magenta sheet that counted as a success, which is worse than refusing it: converting one
/// would have written the magenta out as though it were the picture. The count of blocks it could
/// not read is now returned, and the callers refuse the file.
/// </remarks>
public static class AstcBlockDecoder {

  /// <summary>Decodes a single 16-byte ASTC block, returning whether it could.</summary>
  public static bool DecodeBlock(ReadOnlySpan<byte> block, int blockWidth, int blockHeight, Span<byte> output) {
    var pixelCount = blockWidth * blockHeight;

    // Void-extent detection: bits [7:2] all set in byte 0 indicates a void-extent block (2D)
    if ((block[0] & 0xFC) != 0xFC)
      return false;

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

    return true;
  }

  /// <summary>
  /// Decodes a full ASTC image, returning how many blocks it could not read.
  /// </summary>
  /// <returns>Zero when the whole picture was decoded; otherwise the number of blocks left undone.</returns>
  public static int DecodeImage(ReadOnlySpan<byte> data, int width, int height, int blockWidth, int blockHeight, Span<byte> output) {
    var blockPixelCount = blockWidth * blockHeight;
    var blockPixelBytes = blockPixelCount * 4;

    // Rent from stack if block is small enough, otherwise allocate
    Span<byte> blockPixels = blockPixelBytes <= 576
      ? stackalloc byte[blockPixelBytes]
      : new byte[blockPixelBytes];

    var blocksX = (width + blockWidth - 1) / blockWidth;
    var blocksY = (height + blockHeight - 1) / blockHeight;
    var blockIndex = 0;
    var undecoded = 0;

    for (var by = 0; by < blocksY; ++by) {
      for (var bx = 0; bx < blocksX; ++bx) {
        var blockOffset = blockIndex * 16;
        if (blockOffset + 16 > data.Length)
          return undecoded + (blocksY - by) * blocksX - bx;

        if (!DecodeBlock(data.Slice(blockOffset, 16), blockWidth, blockHeight, blockPixels)) {
          ++undecoded;
          blockPixels.Clear();
        }

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

    return undecoded;
  }
}
