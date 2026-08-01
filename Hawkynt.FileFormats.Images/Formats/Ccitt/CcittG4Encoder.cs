using System;
using System.IO;

namespace FileFormat.Ccitt;

/// <summary>Encodes raw 1bpp scanlines to CCITT Group 4 (T.6) compressed data.</summary>
internal static class CcittG4Encoder {

  // Vertical mode codes: V(0), V(-1), V(+1), V(-2), V(+2), V(-3), V(+3)
  private static readonly (int Code, int BitLength)[] _VerticalCodes = [
    (0b1, 1),        // V(0)
    (0b010, 3),      // VL(1)
    (0b011, 3),      // VR(1)
    (0b000010, 6),   // VL(2)
    (0b000011, 6),   // VR(2)
    (0b0000010, 7),  // VL(3)
    (0b0000011, 7),  // VR(3)
  ];

  // Pass mode code
  private const int _PassCode = 0b0001;
  private const int _PassBitLength = 4;

  // Horizontal mode prefix
  private const int _HorizontalCode = 0b001;
  private const int _HorizontalBitLength = 3;

  // EOFB (End of Facsimile Block): two consecutive EOL codes
  private const int _EofbCode = 0b000000000001;
  private const int _EofbBitLength = 12;

  /// <summary>Encodes 1bpp pixel data to Group 4 compressed bytes.</summary>
  /// <remarks>
  /// This mirrors the decoder, and used to mirror it in its mistakes: the two run lengths of a
  /// horizontal code were written with the wrong Huffman table each, and the reference positions
  /// were found by looking for the first pixel of the opposite colour rather than for a changing
  /// element. Both sides agreed, so files round-tripped here and were unreadable anywhere else.
  /// </remarks>
  internal static byte[] Encode(byte[] pixelData, int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    using var ms = new MemoryStream();
    var bitPos = 0;
    var currentByte = 0;

    var reference = new int[width + 2];
    var coding = new int[width + 2];
    var referenceCount = 0;

    for (var row = 0; row < height; ++row) {
      var codingCount = _FindChanges(pixelData, row * bytesPerRow, width, coding);
      _EncodeLine(ref currentByte, ref bitPos, ms, coding, codingCount, reference, referenceCount, width);

      Array.Copy(coding, reference, codingCount);
      referenceCount = codingCount;
    }

    // Write EOFB (two EOL codes)
    _WriteBits(ref currentByte, ref bitPos, ms, _EofbCode, _EofbBitLength);
    _WriteBits(ref currentByte, ref bitPos, ms, _EofbCode, _EofbBitLength);

    if (bitPos > 0)
      ms.WriteByte((byte)(currentByte << (8 - bitPos)));

    return ms.ToArray();
  }

  /// <summary>Lists the positions where a packed row changes colour, taking the row to start white.</summary>
  private static int _FindChanges(byte[] pixelData, int offset, int width, int[] changes) {
    var count = 0;
    var wasBlack = false;

    for (var x = 0; x < width; ++x) {
      var at = offset + (x >> 3);
      var isBlack = at < pixelData.Length && ((pixelData[at] >> (7 - (x & 7))) & 1) != 0;
      if (isBlack == wasBlack)
        continue;

      changes[count++] = x;
      wasBlack = isBlack;
    }

    return count;
  }

  /// <summary>The next change past a position that turns the line the colour it is not currently.</summary>
  private static int _NextChange(int[] changes, int count, int after, bool isBlack, int width) {
    var i = 0;
    while (i < count && changes[i] <= after)
      ++i;

    // An even index turns the line black, so the parity says which colour a change starts.
    if ((i & 1) != (isBlack ? 1 : 0))
      ++i;

    return i < count ? changes[i] : width;
  }

  private static void _EncodeLine(
    ref int currentByte, ref int bitPos, MemoryStream ms,
    int[] coding, int codingCount, int[] reference, int referenceCount, int width) {
    var a0 = -1;
    var isBlack = false;

    while (a0 < width) {
      var a1 = _NextChange(coding, codingCount, a0, isBlack, width);

      var i = 0;
      while (i < referenceCount && reference[i] <= a0)
        ++i;
      if ((i & 1) != (isBlack ? 1 : 0))
        ++i;

      var b1 = i < referenceCount ? reference[i] : width;
      var b2 = i + 1 < referenceCount ? reference[i + 1] : width;

      if (b2 < a1) {
        _WriteBits(ref currentByte, ref bitPos, ms, _PassCode, _PassBitLength);
        a0 = b2;
        continue;
      }

      var diff = a1 - b1;
      if (diff is >= -3 and <= 3) {
        var index = diff switch {
          0 => 0,
          -1 => 1,
          1 => 2,
          -2 => 3,
          2 => 4,
          -3 => 5,
          _ => 6,
        };
        _WriteBits(ref currentByte, ref bitPos, ms, _VerticalCodes[index].Code, _VerticalCodes[index].BitLength);
        a0 = a1;
        isBlack = !isBlack;
        continue;
      }

      _WriteBits(ref currentByte, ref bitPos, ms, _HorizontalCode, _HorizontalBitLength);
      var a2 = _NextChange(coding, codingCount, a1, !isBlack, width);
      var start = a0 < 0 ? 0 : a0;

      _EncodeRunLength(ref currentByte, ref bitPos, ms, a1 - start, isBlack);
      _EncodeRunLength(ref currentByte, ref bitPos, ms, a2 - a1, !isBlack);
      a0 = a2;
    }
  }

  private static void _EncodeRunLength(ref int currentByte, ref int bitPos, MemoryStream ms, int runLength, bool isBlack) {
    var makeUpTable = isBlack ? CcittHuffmanTable.BlackMakeUp : CcittHuffmanTable.WhiteMakeUp;
    var termTable = isBlack ? CcittHuffmanTable.BlackTerminating : CcittHuffmanTable.WhiteTerminating;

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
