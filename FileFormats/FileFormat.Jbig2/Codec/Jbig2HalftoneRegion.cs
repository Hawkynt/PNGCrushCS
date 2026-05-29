using System;
using System.Collections.Generic;

namespace FileFormat.Jbig2.Codec;

/// <summary>Halftone region decoder/encoder for JBIG2 (ITU-T T.88 section 6.6).
/// Uses pattern dictionaries for continuous-tone encoding. Patterns are placed
/// on a regular grid and selected via Gray-code indices.</summary>
internal static class Jbig2HalftoneRegion {

  /// <summary>Decodes a halftone region segment.</summary>
  /// <param name="data">Segment data bytes.</param>
  /// <param name="offset">Starting offset (after 17-byte region header).</param>
  /// <param name="regionWidth">Region width in pixels.</param>
  /// <param name="regionHeight">Region height in pixels.</param>
  /// <param name="patterns">Available patterns from referred pattern dictionary.</param>
  /// <param name="patternWidth">Width of each pattern.</param>
  /// <param name="patternHeight">Height of each pattern.</param>
  /// <returns>1bpp packed bitmap of the halftone region.</returns>
  internal static byte[] Decode(
    byte[] data,
    int offset,
    int regionWidth,
    int regionHeight,
    byte[][] patterns,
    int patternWidth,
    int patternHeight
  ) {
    var bytesPerRow = (regionWidth + 7) / 8;
    var result = new byte[bytesPerRow * regionHeight];

    if (data.Length - offset < 17 || patterns.Length == 0)
      return result;

    // Halftone region flags (1 byte)
    var flags = data[offset++];
    var htMmr = (flags & 0x01) != 0;
    var htTemplate = (flags >> 1) & 0x03;
    var enableSkip = (flags & 0x08) != 0;
    var defaultPixel = (flags >> 4) & 0x01;
    var combinationOp = (flags >> 5) & 0x07;

    // Grid parameters
    if (offset + 16 > data.Length)
      return result;

    var gridWidth = _ReadInt32BE(data, offset);
    offset += 4;
    var gridHeight = _ReadInt32BE(data, offset);
    offset += 4;
    var gridX = _ReadInt32BE(data, offset);
    offset += 4;
    var gridY = _ReadInt32BE(data, offset);
    offset += 4;

    // Grid vectors (2 bytes each)
    if (offset + 4 > data.Length)
      return result;

    var stepX = (short)((data[offset] << 8) | data[offset + 1]);
    offset += 2;
    var stepY = (short)((data[offset] << 8) | data[offset + 1]);
    offset += 2;

    // Fill with default pixel
    if (defaultPixel != 0)
      Array.Fill(result, (byte)0xFF);

    // Number of bits per Gray-code value
    var numPatterns = patterns.Length;
    var bitsPerValue = 0;
    var temp = 1;
    while (temp < numPatterns) {
      ++bitsPerValue;
      temp <<= 1;
    }
    if (bitsPerValue == 0)
      bitsPerValue = 1;

    // Decode Gray-code bit planes
    var grayBitPlanes = new byte[bitsPerValue][];

    if (htMmr) {
      // Decode each bit plane using MMR
      for (var i = bitsPerValue - 1; i >= 0; --i) {
        var planeWidth = gridWidth;
        var planeHeight = gridHeight;
        grayBitPlanes[i] = MmrCodec.Decode(
          _SubArray(data, offset, data.Length - offset),
          planeWidth, planeHeight
        );
      }
    } else {
      // Decode each bit plane using arithmetic coding
      var decoder = new Jbig2ArithmeticDecoder(data, offset);
      var atX = new sbyte[] { htTemplate == 0 ? (sbyte)3 : (sbyte)2, -3, 2, -2 };
      var atY = new sbyte[] { -1, -1, -2, -2 };

      for (var i = bitsPerValue - 1; i >= 0; --i) {
        grayBitPlanes[i] = Jbig2GenericRegion.Decode(
          decoder, gridWidth, gridHeight,
          htTemplate, false, atX, atY
        );
      }
    }

    // Convert Gray-code bit planes to pattern indices and place patterns
    var gridBytesPerRow = (gridWidth + 7) / 8;
    var patBytesPerRow = (patternWidth + 7) / 8;

    for (var gy = 0; gy < gridHeight; ++gy) {
      for (var gx = 0; gx < gridWidth; ++gx) {
        // Read Gray-code value from bit planes
        var grayValue = 0;
        for (var b = 0; b < bitsPerValue; ++b) {
          var planeByteIndex = gy * gridBytesPerRow + (gx >> 3);
          if (planeByteIndex < grayBitPlanes[b].Length) {
            var bit = (grayBitPlanes[b][planeByteIndex] >> (7 - (gx & 7))) & 1;
            grayValue |= bit << b;
          }
        }

        // Convert Gray code to binary
        var patternIndex = _GrayToBinary(grayValue);
        if (patternIndex >= numPatterns)
          patternIndex = numPatterns - 1;

        // Calculate grid position
        var px = gridX + gx * stepX + gy * stepY;
        var py = gridY + gy * stepX - gx * stepY;

        // Place pattern
        _PlacePattern(result, regionWidth, regionHeight, bytesPerRow, patterns[patternIndex], patternWidth, patternHeight, patBytesPerRow, px, py, combinationOp);
      }
    }

    return result;
  }

  /// <summary>Decodes a pattern dictionary segment (T.88 section 6.7).</summary>
  /// <param name="data">Segment data.</param>
  /// <param name="offset">Starting offset.</param>
  /// <returns>Array of pattern bitmaps, pattern width, and pattern height.</returns>
  internal static (byte[][] Patterns, int PatternWidth, int PatternHeight) DecodePatternDictionary(
    byte[] data, int offset
  ) {
    if (data.Length - offset < 7)
      return ([], 0, 0);

    // Pattern dictionary flags (1 byte)
    var flags = data[offset++];
    var pdMmr = (flags & 0x01) != 0;
    var pdTemplate = (flags >> 1) & 0x03;

    // Pattern width and height (1 byte each)
    var patWidth = data[offset++] & 0xFF;
    var patHeight = data[offset++] & 0xFF;

    // Largest Gray-code value (4 bytes BE)
    var grayMax = _ReadInt32BE(data, offset);
    offset += 4;

    var numPatterns = grayMax + 1;
    var collectiveWidth = numPatterns * patWidth;
    var collectiveBytesPerRow = (collectiveWidth + 7) / 8;

    // Decode the collective bitmap containing all patterns side by side
    byte[] collectiveBitmap;

    if (pdMmr) {
      var compData = _SubArray(data, offset, data.Length - offset);
      collectiveBitmap = MmrCodec.Decode(compData, collectiveWidth, patHeight);
    } else {
      var decoder = new Jbig2ArithmeticDecoder(data, offset);
      var atX = new sbyte[] { pdTemplate == 0 ? (sbyte)3 : (sbyte)2, -3, 2, -2 };
      var atY = new sbyte[] { -1, -1, -2, -2 };
      collectiveBitmap = Jbig2GenericRegion.Decode(
        decoder, collectiveWidth, patHeight,
        pdTemplate, false, atX, atY
      );
    }

    // Extract individual patterns
    var patterns = new byte[numPatterns][];
    var patBytesPerRow = (patWidth + 7) / 8;

    for (var i = 0; i < numPatterns; ++i) {
      patterns[i] = new byte[patBytesPerRow * patHeight];
      for (var y = 0; y < patHeight; ++y) {
        for (var x = 0; x < patWidth; ++x) {
          var srcX = i * patWidth + x;
          var srcByteIndex = y * collectiveBytesPerRow + (srcX >> 3);
          if (srcByteIndex >= collectiveBitmap.Length)
            continue;
          var bit = (collectiveBitmap[srcByteIndex] >> (7 - (srcX & 7))) & 1;
          if (bit != 0) {
            var dstByteIndex = y * patBytesPerRow + (x >> 3);
            patterns[i][dstByteIndex] |= (byte)(1 << (7 - (x & 7)));
          }
        }
      }
    }

    return (patterns, patWidth, patHeight);
  }

  private static int _GrayToBinary(int gray) {
    var n = gray;
    while (gray > 0) {
      gray >>= 1;
      n ^= gray;
    }
    return n;
  }

  private static void _PlacePattern(
    byte[] result, int resultWidth, int resultHeight, int resultBytesPerRow,
    byte[] pattern, int patWidth, int patHeight, int patBytesPerRow,
    int placeX, int placeY, int combinationOp
  ) {
    for (var py = 0; py < patHeight; ++py) {
      var ry = placeY + py;
      if (ry < 0 || ry >= resultHeight)
        continue;

      for (var px = 0; px < patWidth; ++px) {
        var rx = placeX + px;
        if (rx < 0 || rx >= resultWidth)
          continue;

        var srcIdx = py * patBytesPerRow + (px >> 3);
        if (srcIdx >= pattern.Length)
          continue;

        var srcBit = (pattern[srcIdx] >> (7 - (px & 7))) & 1;
        var dstIdx = ry * resultBytesPerRow + (rx >> 3);
        var dstShift = 7 - (rx & 7);
        var dstBit = (result[dstIdx] >> dstShift) & 1;

        var combined = combinationOp switch {
          0 => dstBit | srcBit,
          1 => dstBit & srcBit,
          2 => dstBit ^ srcBit,
          3 => ~(dstBit ^ srcBit) & 1,
          4 => srcBit,
          _ => dstBit | srcBit,
        };

        if (combined != 0)
          result[dstIdx] |= (byte)(1 << dstShift);
        else
          result[dstIdx] &= (byte)~(1 << dstShift);
      }
    }
  }

  private static byte[] _SubArray(byte[] data, int offset, int length) {
    var result = new byte[length];
    Array.Copy(data, offset, result, 0, length);
    return result;
  }

  private static int _ReadInt32BE(byte[] data, int offset)
    => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
