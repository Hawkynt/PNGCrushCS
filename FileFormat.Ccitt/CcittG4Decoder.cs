using System;

namespace FileFormat.Ccitt;

/// <summary>Decodes CCITT Group 4 (T.6) compressed data to raw 1bpp scanlines.</summary>
internal static class CcittG4Decoder {

  /// <summary>Decodes Group 4 compressed bytes to 1bpp pixel data.</summary>
  internal static byte[] Decode(byte[] compressedData, int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    var reader = new _BitReader(compressedData);

    // Reference line starts as all-white imaginary line
    var refLine = new byte[bytesPerRow];
    var codingLine = new byte[bytesPerRow];

    for (var row = 0; row < height; ++row) {
      Array.Clear(codingLine, 0, bytesPerRow);
      _DecodeLine(reader, codingLine, refLine, width);
      Array.Copy(codingLine, 0, pixelData, row * bytesPerRow, bytesPerRow);
      Array.Copy(codingLine, refLine, bytesPerRow);
    }

    return pixelData;
  }

  private static void _DecodeLine(_BitReader reader, byte[] codingLine, byte[] refLine, int width) {
    var a0 = -1;
    var a0Color = false; // false = white

    while (a0 < width) {
      var mode = _ReadMode(reader);
      if (mode < 0)
        return;

      switch (mode) {
        case 0: { // Pass mode
          var b1 = _FindChangingElement(refLine, a0 < 0 ? 0 : a0, width, a0Color);
          var b2 = b1 < width ? _FindChangingElement(refLine, b1, width, !a0Color) : width;
          if (a0Color)
            _SetBlackPixels(codingLine, a0 < 0 ? 0 : a0, b2 - (a0 < 0 ? 0 : a0));
          a0 = b2;
          break;
        }
        case 1: { // Horizontal mode
          var runA = _DecodeRunLength(reader, !a0Color); // same color as a0
          var runB = _DecodeRunLength(reader, a0Color);  // opposite color
          var startPos = a0 < 0 ? 0 : a0;
          if (!a0Color) {
            // a0 is white, first run is white (skip), second run is black
            _SetBlackPixels(codingLine, startPos + runA, runB);
          } else {
            // a0 is black, first run is black, second run is white (skip)
            _SetBlackPixels(codingLine, startPos, runA);
          }
          a0 = startPos + runA + runB;
          break;
        }
        default: { // Vertical mode (mode - 2 gives offset index)
          var verticalIndex = mode - 2;
          var diff = verticalIndex switch {
            0 => 0,
            1 => -1,
            2 => 1,
            3 => -2,
            4 => 2,
            5 => -3,
            6 => 3,
            _ => 0
          };
          var b1 = _FindChangingElement(refLine, a0 < 0 ? 0 : a0, width, a0Color);
          var a1 = Math.Max(0, Math.Min(b1 + diff, width));
          if (a0Color)
            _SetBlackPixels(codingLine, a0 < 0 ? 0 : a0, a1 - (a0 < 0 ? 0 : a0));
          a0 = a1;
          a0Color = !a0Color;
          break;
        }
      }
    }
  }

  /// <summary>Reads the next mode from the bitstream. Returns: 0=pass, 1=horizontal, 2-8=vertical modes.</summary>
  private static int _ReadMode(_BitReader reader) {
    // V(0) = 1
    var bit = reader.ReadBit();
    if (bit < 0)
      return -1;

    if (bit == 1)
      return 2; // V(0)

    // 0...
    bit = reader.ReadBit();
    if (bit < 0)
      return -1;

    if (bit == 1) {
      // 01...
      bit = reader.ReadBit();
      if (bit < 0)
        return -1;

      return bit == 0 ? 3 : 4; // VL(1) = 010, VR(1) = 011
    }

    // 00...
    bit = reader.ReadBit();
    if (bit < 0)
      return -1;

    if (bit == 1)
      return 1; // Horizontal = 001

    // 000...
    bit = reader.ReadBit();
    if (bit < 0)
      return -1;

    if (bit == 1)
      return 0; // Pass = 0001

    // 0000...
    bit = reader.ReadBit();
    if (bit < 0)
      return -1;

    if (bit == 1) {
      // 00001...
      bit = reader.ReadBit();
      if (bit < 0)
        return -1;

      return bit == 0 ? 5 : 6; // VL(2) = 000010, VR(2) = 000011
    }

    // 00000...
    bit = reader.ReadBit();
    if (bit < 0)
      return -1;

    if (bit == 1) {
      // 000001...
      bit = reader.ReadBit();
      if (bit < 0)
        return -1;

      return bit == 0 ? 7 : 8; // VL(3) = 0000010, VR(3) = 0000011
    }

    // EOFB or invalid
    return -1;
  }

  private static int _DecodeRunLength(_BitReader reader, bool isBlack) {
    var totalRun = 0;

    while (true) {
      var code = _DecodeNextCode(reader, isBlack);
      if (code < 0)
        return totalRun;

      totalRun += code;
      if (code < 64)
        break;
    }

    return totalRun;
  }

  private static int _DecodeNextCode(_BitReader reader, bool isBlack) {
    var termTable = isBlack ? CcittHuffmanTable.BlackTerminating : CcittHuffmanTable.WhiteTerminating;
    var makeUpTable = isBlack ? CcittHuffmanTable.BlackMakeUp : CcittHuffmanTable.WhiteMakeUp;

    var accumulated = 0;
    var bitsRead = 0;
    var maxBits = 13;

    while (bitsRead < maxBits) {
      var bit = reader.ReadBit();
      if (bit < 0)
        return -1;

      accumulated = (accumulated << 1) | bit;
      ++bitsRead;

      for (var i = 0; i < termTable.Length; ++i)
        if (termTable[i].BitLength == bitsRead && termTable[i].Code == accumulated)
          return i;

      for (var i = 0; i < makeUpTable.Length; ++i)
        if (makeUpTable[i].BitLength == bitsRead && makeUpTable[i].Code == accumulated)
          return (i + 1) * 64;
    }

    return -1;
  }

  private static int _FindChangingElement(byte[] line, int start, int width, bool currentColor) {
    for (var x = start; x < width; ++x) {
      var byteIndex = x >> 3;
      var bitIndex = 7 - (x & 7);
      var isBlack = ((line[byteIndex] >> bitIndex) & 1) != 0;
      if (isBlack != currentColor)
        return x;
    }

    return width;
  }

  private static void _SetBlackPixels(byte[] line, int start, int count) {
    for (var i = 0; i < count; ++i) {
      var px = start + i;
      var byteIndex = px >> 3;
      var bitIndex = 7 - (px & 7);
      line[byteIndex] |= (byte)(1 << bitIndex);
    }
  }

  private sealed class _BitReader(byte[] data) {
    private int _bytePos;
    private int _bitPos = 7;

    public int ReadBit() {
      if (_bytePos >= data.Length)
        return -1;

      var bit = (data[_bytePos] >> _bitPos) & 1;
      --_bitPos;
      if (_bitPos < 0) {
        _bitPos = 7;
        ++_bytePos;
      }

      return bit;
    }
  }
}
