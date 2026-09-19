using System;
using System.IO;

namespace FileFormat.Ccitt;

/// <summary>Encodes raw 1bpp scanlines to CCITT Group 3 1D (Modified Huffman) compressed data.</summary>
internal static class CcittG3Encoder {

  /// <summary>How many end-of-line words make T.4's return-to-control.</summary>
  private const int _RETURN_TO_CONTROL_MARKERS = 6;

  /// <summary>Encodes 1bpp pixel data to Group 3 1D compressed bytes.</summary>
  /// <param name="pixelData">Packed rows, a set bit being ink.</param>
  /// <param name="width">Pixels across.</param>
  /// <param name="height">Rows.</param>
  /// <param name="leadingEndOfLine">
  /// Whether to open the stream with an end-of-line word as well as closing every row with one.
  /// T.4 puts one in front of the first row and decoders that hunt for it to find their bit
  /// alignment — XnView's among them — read a stream without it as the wrong picture entirely, not
  /// as a shifted one, because the first codeword lands mid-byte. Off by default so that the
  /// streams this has always written are unchanged.
  /// </param>
  /// <param name="returnToControl">
  /// Whether to close the stream with T.4's return-to-control word, which is six end-of-line words
  /// in a row and is how a page says it has ended. Without it the coding simply stops, and a decoder
  /// that has been told nothing about the height cannot tell the last row from a truncated file —
  /// it reports the row it was reading when the bits ran out as unreadable. Off by default so that
  /// the wrappers which state their own height are unchanged.
  /// </param>
  internal static byte[] Encode(byte[] pixelData, int width, int height, bool leadingEndOfLine = false, bool returnToControl = false) {
    var bytesPerRow = (width + 7) / 8;
    using var ms = new MemoryStream();
    var bitPos = 0;
    var currentByte = 0;

    if (leadingEndOfLine)
      _WriteBits(ref currentByte, ref bitPos, ms, CcittHuffmanTable.EolCode, CcittHuffmanTable.EolBitLength);

    for (var row = 0; row < height; ++row) {
      var rowOffset = row * bytesPerRow;
      var runs = _ExtractRuns(pixelData, rowOffset, width);

      var isWhite = true;
      foreach (var runLength in runs) {
        _EncodeRunLength(ref currentByte, ref bitPos, ms, runLength, isWhite);
        isWhite = !isWhite;
      }

      _WriteBits(ref currentByte, ref bitPos, ms, CcittHuffmanTable.EolCode, CcittHuffmanTable.EolBitLength);
    }

    // Every row has just written one, and return-to-control is six of them in a row, so five more
    // finish it rather than six — a seventh would be fill, not punctuation.
    if (returnToControl)
      for (var i = 1; i < _RETURN_TO_CONTROL_MARKERS; ++i)
        _WriteBits(ref currentByte, ref bitPos, ms, CcittHuffmanTable.EolCode, CcittHuffmanTable.EolBitLength);

    if (bitPos > 0)
      ms.WriteByte((byte)(currentByte << (8 - bitPos)));

    return ms.ToArray();
  }

  private static int[] _ExtractRuns(byte[] pixelData, int rowOffset, int width) {
    // CCITT G3 always starts with a white run (may be zero-length)
    var runs = new System.Collections.Generic.List<int>();
    var isWhite = true; // Start counting white pixels
    var runLength = 0;

    for (var x = 0; x < width; ++x) {
      var byteIndex = rowOffset + (x >> 3);
      var bitIndex = 7 - (x & 7);
      var pixelIsWhite = ((pixelData[byteIndex] >> bitIndex) & 1) == 0;

      if (pixelIsWhite == isWhite) {
        ++runLength;
      } else {
        runs.Add(runLength);
        isWhite = !isWhite;
        runLength = 1;
      }
    }

    runs.Add(runLength);
    return [.. runs];
  }

  private static void _EncodeRunLength(ref int currentByte, ref int bitPos, MemoryStream ms, int runLength, bool isWhite) {
    var makeUpTable = isWhite ? CcittHuffmanTable.WhiteMakeUp : CcittHuffmanTable.BlackMakeUp;
    var termTable = isWhite ? CcittHuffmanTable.WhiteTerminating : CcittHuffmanTable.BlackTerminating;

    while (runLength >= 64) {
      var makeUpIndex = Math.Min(runLength / 64, makeUpTable.Length) - 1;
      var makeUpLength = (makeUpIndex + 1) * 64;
      var (code, bitLength) = makeUpTable[makeUpIndex];
      _WriteBits(ref currentByte, ref bitPos, ms, code, bitLength);
      runLength -= makeUpLength;
    }

    var term = termTable[runLength];
    _WriteBits(ref currentByte, ref bitPos, ms, term.Code, term.BitLength);
  }

  private static void _WriteBits(ref int currentByte, ref int bitPos, MemoryStream ms, int code, int bitLength) {
    for (var i = bitLength - 1; i >= 0; --i) {
      currentByte = (currentByte << 1) | ((code >> i) & 1);
      ++bitPos;
      if (bitPos == 8) {
        ms.WriteByte((byte)currentByte);
        currentByte = 0;
        bitPos = 0;
      }
    }
  }
}
