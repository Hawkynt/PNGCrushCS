using System;
using System.IO;

namespace FileFormat.Ccitt;

/// <summary>Decodes CCITT Group 3 1D (Modified Huffman) compressed data to raw 1bpp scanlines.</summary>
internal static class CcittG3Decoder {

  /// <summary>Decodes Group 3 1D compressed bytes to 1bpp pixel data.</summary>
  internal static byte[] Decode(byte[] compressedData, int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    var bitReader = new _BitReader(compressedData);

    for (var row = 0; row < height; ++row) {
      var rowOffset = row * bytesPerRow;
      var x = 0;
      var isWhite = true;

      while (x < width) {
        var runLength = _DecodeRunLength(bitReader, isWhite);
        if (runLength < 0)
          break;

        runLength = Math.Min(runLength, width - x);

        if (!isWhite)
          _SetBlackPixels(pixelData, rowOffset, x, runLength);

        x += runLength;
        isWhite = !isWhite;
      }

      // Skip EOL marker
      _SkipEol(bitReader);
    }

    return pixelData;
  }

  private static int _DecodeRunLength(in _BitReader reader, bool isWhite) {
    var totalRun = 0;

    // Read make-up codes (run >= 64)
    while (true) {
      var code = _DecodeNextCode(reader, isWhite);
      if (code < 0)
        return totalRun > 0 ? totalRun : -1;

      totalRun += code;
      if (code < 64)
        break;
    }

    return totalRun;
  }

  private static int _DecodeNextCode(in _BitReader reader, bool isWhite) {
    var termTable = isWhite ? CcittHuffmanTable.WhiteTerminating : CcittHuffmanTable.BlackTerminating;
    var makeUpTable = isWhite ? CcittHuffmanTable.WhiteMakeUp : CcittHuffmanTable.BlackMakeUp;

    var accumulated = 0;
    var bitsRead = 0;
    var maxBits = 13; // Longest code in the tables

    while (bitsRead < maxBits) {
      var bit = reader.ReadBit();
      if (bit < 0)
        return -1;

      accumulated = (accumulated << 1) | bit;
      ++bitsRead;

      // Check terminating codes
      for (var i = 0; i < termTable.Length; ++i)
        if (termTable[i].BitLength == bitsRead && termTable[i].Code == accumulated)
          return i;

      // Check make-up codes
      for (var i = 0; i < makeUpTable.Length; ++i)
        if (makeUpTable[i].BitLength == bitsRead && makeUpTable[i].Code == accumulated)
          return (i + 1) * 64;
    }

    return -1;
  }

  private static void _SkipEol(in _BitReader reader) {
    var accumulated = 0;
    var bitsRead = 0;

    while (bitsRead < CcittHuffmanTable.EolBitLength) {
      var bit = reader.ReadBit();
      if (bit < 0)
        return;

      accumulated = (accumulated << 1) | bit;
      ++bitsRead;

      if (bitsRead >= CcittHuffmanTable.EolBitLength && accumulated == CcittHuffmanTable.EolCode)
        return;
    }
  }

  private static void _SetBlackPixels(byte[] pixelData, int rowOffset, int x, int count) {
    for (var i = 0; i < count; ++i) {
      var px = x + i;
      var byteIndex = rowOffset + (px >> 3);
      var bitIndex = 7 - (px & 7);
      pixelData[byteIndex] |= (byte)(1 << bitIndex);
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
