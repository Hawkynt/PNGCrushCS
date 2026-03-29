using System;
using System.IO;

namespace FileFormat.CameraRaw;

/// <summary>Decompresses Sony ARW (Alpha Raw) compressed sensor data. ARW2 uses 7-bit values with delta coding and optional 4-bit extensions.</summary>
internal static class SonyDecompressor {

  /// <summary>Decompress Sony ARW2 compressed raw data.</summary>
  /// <param name="data">Full file data.</param>
  /// <param name="stripOffset">Offset to the compressed strip data.</param>
  /// <param name="stripLength">Length of the compressed strip data.</param>
  /// <param name="width">Image width.</param>
  /// <param name="height">Image height.</param>
  /// <param name="bitsPerSample">Bit depth (typically 12 or 14).</param>
  /// <returns>Decompressed 16-bit CFA samples in raster order.</returns>
  public static ushort[] Decompress(byte[] data, int stripOffset, int stripLength, int width, int height, int bitsPerSample) {
    ArgumentNullException.ThrowIfNull(data);
    if (stripOffset < 0 || stripOffset + stripLength > data.Length)
      throw new InvalidDataException("Sony strip data out of bounds.");

    var output = new ushort[width * height];
    var maxVal = (1 << bitsPerSample) - 1;

    // ARW2 format: each row is encoded independently
    // Each row consists of:
    //   - 7-bit delta-encoded values for each pair of pixels
    //   - Optional 4-bit extension data appended after the 7-bit section
    // The data is organized in 16-pixel blocks per row

    var rowBytes = stripLength / height;
    if (rowBytes <= 0)
      return _FallbackUnpack(data, stripOffset, stripLength, width, height, bitsPerSample);

    for (var row = 0; row < height; ++row) {
      var rowOffset = stripOffset + row * rowBytes;
      if (rowOffset + rowBytes > data.Length)
        break;

      _DecodeArw2Row(data, rowOffset, rowBytes, output, row * width, width, maxVal);
    }

    return output;
  }

  /// <summary>Decode a single ARW2 row.</summary>
  private static void _DecodeArw2Row(byte[] data, int rowOffset, int rowBytes, ushort[] output, int outOffset, int width, int maxVal) {
    // ARW2 uses a block-based encoding:
    // Each 16-pixel block:
    //   - First byte contains a 4-bit shift value and other control bits
    //   - Following bytes contain 7-bit values for pixel pairs
    //   - Extension bits may follow

    var pos = rowOffset;
    var end = rowOffset + rowBytes;
    var col = 0;

    while (col < width && pos < end) {
      // Read the block header
      if (pos + 1 > end)
        break;

      var header = data[pos++];
      var shift = (header >> 4) & 0x0F;
      var blockPixels = Math.Min(16, width - col);

      // Decode 7-bit values for this block
      var bitBuffer = 0u;
      var bitsLeft = 0;

      for (var i = 0; i < blockPixels; ++i) {
        // Read 7 bits
        while (bitsLeft < 7 && pos < end) {
          bitBuffer = (bitBuffer << 8) | data[pos++];
          bitsLeft += 8;
        }

        if (bitsLeft < 7)
          break;

        bitsLeft -= 7;
        var value7 = (int)((bitBuffer >> bitsLeft) & 0x7F);

        // Apply shift and scale
        var sample = Math.Clamp(value7 << shift, 0, maxVal);
        if (outOffset + col + i < output.Length)
          output[outOffset + col + i] = (ushort)sample;
      }

      col += blockPixels;
    }

    // Fill remaining with zero
    while (col < width) {
      if (outOffset + col < output.Length)
        output[outOffset + col] = 0;
      ++col;
    }
  }

  /// <summary>Fallback: unpack raw bit-packed data (for uncompressed Sony strips).</summary>
  private static ushort[] _FallbackUnpack(byte[] data, int offset, int length, int width, int height, int bitsPerSample) {
    var output = new ushort[width * height];
    var maxVal = (1 << bitsPerSample) - 1;

    var bitBuffer = 0u;
    var bitsLeft = 0;
    var pos = offset;
    var end = offset + length;

    for (var i = 0; i < output.Length; ++i) {
      while (bitsLeft < bitsPerSample && pos < end) {
        bitBuffer = (bitBuffer << 8) | data[pos++];
        bitsLeft += 8;
      }

      if (bitsLeft < bitsPerSample)
        break;

      bitsLeft -= bitsPerSample;
      output[i] = (ushort)(((int)(bitBuffer >> bitsLeft)) & maxVal);
    }

    return output;
  }
}
