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
  internal static byte[] Encode(byte[] pixelData, int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    using var ms = new MemoryStream();
    var bitPos = 0;
    var currentByte = 0;

    // Reference line starts as all-white imaginary line
    var refLine = new byte[bytesPerRow];
    var codingLine = new byte[bytesPerRow];

    for (var row = 0; row < height; ++row) {
      Array.Copy(pixelData, row * bytesPerRow, codingLine, 0, bytesPerRow);
      _EncodeLine(ref currentByte, ref bitPos, ms, codingLine, refLine, width);
      Array.Copy(codingLine, refLine, bytesPerRow);
    }

    // Write EOFB (two EOL codes)
    _WriteBits(ref currentByte, ref bitPos, ms, _EofbCode, _EofbBitLength);
    _WriteBits(ref currentByte, ref bitPos, ms, _EofbCode, _EofbBitLength);

    if (bitPos > 0)
      ms.WriteByte((byte)(currentByte << (8 - bitPos)));

    return ms.ToArray();
  }

  private static void _EncodeLine(ref int currentByte, ref int bitPos, MemoryStream ms, byte[] codingLine, byte[] refLine, int width) {
    var a0 = -1; // Current coding position (-1 means before the start)
    var a0Color = false; // false = white

    while (a0 < width) {
      var a1 = _FindChangingElement(codingLine, a0 < 0 ? 0 : a0, width, a0Color);
      var b1 = _FindChangingElement(refLine, a0 < 0 ? 0 : a0, width, a0Color);
      var b2 = b1 < width ? _FindChangingElement(refLine, b1, width, !a0Color) : width;

      if (b2 < a1) {
        // Pass mode
        _WriteBits(ref currentByte, ref bitPos, ms, _PassCode, _PassBitLength);
        a0 = b2;
      } else {
        var diff = a1 - b1;
        if (diff >= -3 && diff <= 3) {
          // Vertical mode
          var index = diff switch {
            0 => 0,
            -1 => 1,
            1 => 2,
            -2 => 3,
            2 => 4,
            -3 => 5,
            3 => 6,
            _ => 0
          };
          _WriteBits(ref currentByte, ref bitPos, ms, _VerticalCodes[index].Code, _VerticalCodes[index].BitLength);
          a0 = a1;
          a0Color = !a0Color;
        } else {
          // Horizontal mode
          _WriteBits(ref currentByte, ref bitPos, ms, _HorizontalCode, _HorizontalBitLength);
          var a2 = _FindChangingElement(codingLine, a1, width, !a0Color);
          var runA0A1 = a1 - (a0 < 0 ? 0 : a0);
          var runA1A2 = a2 - a1;
          _EncodeRunLength(ref currentByte, ref bitPos, ms, runA0A1, !a0Color); // a0 color
          _EncodeRunLength(ref currentByte, ref bitPos, ms, runA1A2, a0Color);  // opposite color
          a0 = a2;
        }
      }
    }
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
