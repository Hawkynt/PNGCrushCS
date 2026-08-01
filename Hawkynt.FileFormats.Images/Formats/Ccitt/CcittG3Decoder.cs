using System;
using System.IO;

namespace FileFormat.Ccitt;

/// <summary>Decodes CCITT Group 3 1D (Modified Huffman) compressed data to raw 1bpp scanlines.</summary>
internal static class CcittG3Decoder {

  /// <summary>Decodes Group 3 1D compressed bytes to 1bpp pixel data.</summary>
  internal static byte[] Decode(byte[] compressedData, int width, int height)
    => Decode(compressedData, width, height, out _);

  /// <summary>Decodes, reporting how many rows the coding actually held.</summary>
  /// <remarks>
  /// A bare fax stream carries no page size, so the only way to learn its height is to decode until
  /// the coding runs out. Callers that already know the height can ignore the count.
  /// </remarks>
  internal static byte[] Decode(byte[] compressedData, int width, int height, out int rowsDecoded) {
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    var bitReader = new _BitReader(compressedData);

    rowsDecoded = 0;

    // A stream commonly opens with a marker, and one sits between rows; skipping before each row
    // rather than only after handles both, since a stream without them is left alone.
    for (var row = 0; row < height; ++row) {
      _SkipEol(bitReader);

      var rowOffset = row * bytesPerRow;
      var x = 0;
      var isWhite = true;
      var ranOut = false;

      while (x < width) {
        var runLength = _DecodeRunLength(bitReader, isWhite);
        if (runLength < 0) {
          ranOut = true;
          break;
        }

        runLength = Math.Min(runLength, width - x);

        if (!isWhite)
          _SetBlackPixels(pixelData, rowOffset, x, runLength);

        x += runLength;
        isWhite = !isWhite;
      }

      // A row that ran out mid-way is not a row; it is where the coding stopped.
      if (ranOut && x == 0)
        break;

      rowsDecoded = row + 1;
      if (ranOut)
        break;

    }

    return pixelData;
  }

  private static int _DecodeRunLength(_BitReader reader, bool isWhite) {
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

  /// <summary>Steps over an end-of-line marker and any fill bits before it, if one is there.</summary>
  /// <remarks>
  /// This used to swallow twelve bits whichever they were, so a stream without markers lost the
  /// first twelve bits of every row after the first. A marker is padded on its left with as many
  /// zero bits as the sender liked, so the fill has to be walked past one bit at a time — and only
  /// while a marker is still ahead, or the walk eats a legitimate run of zeros.
  /// </remarks>
  private static void _SkipEol(_BitReader reader) {
    if (reader.AtEnd)
      return;

    // The marker is eleven zeros and a one, and a sender may pad in front of it with more zeros.
    // Nothing else in the coding has a run of zeros that long, so counting them first tells a marker
    // from a run code without consuming anything — which matters, because most run codes start with
    // a zero and taking those for fill destroys them.
    const int zerosInMarker = CcittHuffmanTable.EolBitLength - 1;
    var zeros = reader.PeekZeroRun(_MaximumFillBits);
    if (zeros < zerosInMarker || zeros >= _MaximumFillBits)
      return;

    for (var i = 0; i <= zeros; ++i)
      reader.ReadBit();
  }

  /// <summary>How much padding to tolerate in front of a marker before giving up on finding one.</summary>
  private const int _MaximumFillBits = 4096;

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

    /// <summary>How many zero bits come next, without consuming anything. Stops at a given limit.</summary>
    public int PeekZeroRun(int limit) {
      int bytePos = _bytePos, bitPos = _bitPos;

      for (var count = 0; count < limit; ++count) {
        if (bytePos >= data.Length)
          return count;

        if (((data[bytePos] >> bitPos) & 1) != 0)
          return count;

        if (--bitPos >= 0)
          continue;

        bitPos = 7;
        ++bytePos;
      }

      return limit;
    }

    /// <summary>Whether there is anything left to read.</summary>
    public bool AtEnd => _bytePos >= data.Length;

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
