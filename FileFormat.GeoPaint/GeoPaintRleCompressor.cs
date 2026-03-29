using System;
using System.IO;

namespace FileFormat.GeoPaint;

/// <summary>
/// GEOS GeoPaint scanline RLE compressor/decompressor.
/// Encoding per scanline:
///   0x00-0x7F: literal run -- (n + 1) raw bytes follow.
///   0x80-0xBF: repeat run -- next byte repeated (n - 0x80 + 1) times.
///   0xC0-0xFE: zero run -- (n - 0xC0 + 1) zero bytes.
///   0xFF: end of data marker.
/// </summary>
internal static class GeoPaintRleCompressor {

  private const byte _END_MARKER = 0xFF;
  private const int _LITERAL_MAX = 128; // 0x00..0x7F => 1..128 bytes
  private const int _REPEAT_MIN = 0x80;
  private const int _REPEAT_MAX = 0xBF;
  private const int _ZERO_MIN = 0xC0;
  private const int _ZERO_MAX = 0xFE;

  /// <summary>Compresses a single uncompressed scanline (80 bytes) into GEOS RLE.</summary>
  public static byte[] CompressScanline(byte[] scanline) {
    if (scanline.Length == 0)
      return [_END_MARKER];

    using var ms = new MemoryStream();
    var i = 0;

    while (i < scanline.Length) {
      // Check for zero run
      if (scanline[i] == 0x00) {
        var runStart = i;
        while (i < scanline.Length && scanline[i] == 0x00 && i - runStart < _ZERO_MAX - _ZERO_MIN + 1)
          ++i;

        var count = i - runStart; // 1..63
        ms.WriteByte((byte)(_ZERO_MIN + count - 1));
        continue;
      }

      // Check for repeat run (same non-zero byte)
      {
        var value = scanline[i];
        var runStart = i;
        while (i < scanline.Length && scanline[i] == value && i - runStart < _REPEAT_MAX - _REPEAT_MIN + 1)
          ++i;

        var count = i - runStart;
        if (count >= 3) {
          ms.WriteByte((byte)(_REPEAT_MIN + count - 1));
          ms.WriteByte(value);
          continue;
        }

        // Rewind -- not worth encoding as repeat
        i = runStart;
      }

      // Collect literals
      {
        var literalStart = i;
        while (i < scanline.Length && i - literalStart < _LITERAL_MAX) {
          // Stop if a zero run of 3+ starts here
          if (scanline[i] == 0x00) {
            var zeroCount = 0;
            for (var j = i; j < scanline.Length && scanline[j] == 0x00 && j - i < 3; ++j)
              ++zeroCount;
            if (zeroCount >= 3)
              break;
          }

          // Stop if a repeat run of 3+ starts here
          if (i + 2 < scanline.Length && scanline[i] == scanline[i + 1] && scanline[i] == scanline[i + 2])
            break;

          ++i;
        }

        var literalCount = i - literalStart;
        if (literalCount > 0) {
          ms.WriteByte((byte)(literalCount - 1));
          ms.Write(scanline, literalStart, literalCount);
        }
      }
    }

    ms.WriteByte(_END_MARKER);
    return ms.ToArray();
  }

  /// <summary>Decompresses a GEOS RLE-encoded scanline into <paramref name="bytesPerRow"/> bytes.</summary>
  /// <param name="bytesDecoded">The number of output bytes actually written (0 if only an end marker was found).</param>
  public static byte[] DecompressScanline(byte[] data, ref int offset, int bytesPerRow, out int bytesDecoded) {
    var output = new byte[bytesPerRow];
    var outIdx = 0;
    var hitEndMarker = false;

    while (offset < data.Length && outIdx < bytesPerRow) {
      var code = data[offset++];

      if (code == _END_MARKER) {
        hitEndMarker = true;
        break;
      }

      if (code <= 0x7F) {
        // Literal run: (code + 1) raw bytes follow
        var count = code + 1;
        for (var j = 0; j < count && offset < data.Length && outIdx < bytesPerRow; ++j)
          output[outIdx++] = data[offset++];
      } else if (code <= 0xBF) {
        // Repeat run: next byte repeated (code - 0x80 + 1) times
        if (offset >= data.Length)
          break;

        var count = code - 0x80 + 1;
        var value = data[offset++];
        for (var j = 0; j < count && outIdx < bytesPerRow; ++j)
          output[outIdx++] = value;
      } else {
        // Zero run: (code - 0xC0 + 1) zero bytes
        var count = code - 0xC0 + 1;
        for (var j = 0; j < count && outIdx < bytesPerRow; ++j)
          output[outIdx++] = 0x00;
      }
    }

    // If the output buffer filled before hitting the end marker, skip forward
    // until we consume the end marker so the offset is positioned correctly
    // for the next scanline.
    if (!hitEndMarker) {
      while (offset < data.Length) {
        var code = data[offset++];
        if (code == _END_MARKER)
          break;

        // Skip the payload of each token we're discarding
        if (code <= 0x7F)
          offset = Math.Min(offset + code + 1, data.Length); // literal: skip (code + 1) data bytes
        else if (code <= 0xBF)
          offset = Math.Min(offset + 1, data.Length); // repeat: skip the value byte
        // zero run (0xC0-0xFE): no additional bytes to skip
      }
    }

    bytesDecoded = outIdx;
    return output;
  }

  /// <summary>Compresses all scanlines of a GeoPaint image.</summary>
  public static byte[] Compress(byte[] pixelData, int height) {
    using var ms = new MemoryStream();
    var scanline = new byte[GeoPaintFile.BytesPerRow];

    for (var row = 0; row < height; ++row) {
      var srcOffset = row * GeoPaintFile.BytesPerRow;
      var available = Math.Min(GeoPaintFile.BytesPerRow, pixelData.Length - srcOffset);
      if (available > 0)
        Array.Copy(pixelData, srcOffset, scanline, 0, available);
      if (available < GeoPaintFile.BytesPerRow)
        Array.Clear(scanline, available, GeoPaintFile.BytesPerRow - available);

      var compressed = CompressScanline(scanline);
      ms.Write(compressed, 0, compressed.Length);
    }

    return ms.ToArray();
  }

  /// <summary>Decompresses all scanlines of a GeoPaint image.</summary>
  public static byte[] Decompress(byte[] data, int height) {
    var pixelData = new byte[GeoPaintFile.BytesPerRow * height];
    var offset = 0;

    for (var row = 0; row < height && offset < data.Length; ++row) {
      var scanline = DecompressScanline(data, ref offset, GeoPaintFile.BytesPerRow, out _);
      Array.Copy(scanline, 0, pixelData, row * GeoPaintFile.BytesPerRow, GeoPaintFile.BytesPerRow);
    }

    return pixelData;
  }
}
