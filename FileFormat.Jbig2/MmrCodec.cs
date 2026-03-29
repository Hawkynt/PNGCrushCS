using System;
using System.IO;

namespace FileFormat.Jbig2;

/// <summary>MMR (Modified Modified READ) codec implementing CCITT Group 4 encoding/decoding for JBIG2 generic regions.</summary>
internal static class MmrCodec {

  #region Huffman Tables

  private static readonly (int Code, int BitLength)[] _WhiteTerminating = [
    (0b00110101, 8),   // 0
    (0b000111, 6),     // 1
    (0b0111, 4),       // 2
    (0b1000, 4),       // 3
    (0b1011, 4),       // 4
    (0b1100, 4),       // 5
    (0b1110, 4),       // 6
    (0b1111, 4),       // 7
    (0b10011, 5),      // 8
    (0b10100, 5),      // 9
    (0b00111, 5),      // 10
    (0b01000, 5),      // 11
    (0b001000, 6),     // 12
    (0b000011, 6),     // 13
    (0b110100, 6),     // 14
    (0b110101, 6),     // 15
    (0b101010, 6),     // 16
    (0b101011, 6),     // 17
    (0b0100111, 7),    // 18
    (0b0001100, 7),    // 19
    (0b0001000, 7),    // 20
    (0b0010111, 7),    // 21
    (0b0000011, 7),    // 22
    (0b0000100, 7),    // 23
    (0b0101000, 7),    // 24
    (0b0101011, 7),    // 25
    (0b0010011, 7),    // 26
    (0b0100100, 7),    // 27
    (0b0011000, 7),    // 28
    (0b00000010, 8),   // 29
    (0b00000011, 8),   // 30
    (0b00011010, 8),   // 31
    (0b00011011, 8),   // 32
    (0b00010010, 8),   // 33
    (0b00010011, 8),   // 34
    (0b00010100, 8),   // 35
    (0b00010101, 8),   // 36
    (0b00010110, 8),   // 37
    (0b00010111, 8),   // 38
    (0b00101000, 8),   // 39
    (0b00101001, 8),   // 40
    (0b00101010, 8),   // 41
    (0b00101011, 8),   // 42
    (0b00101100, 8),   // 43
    (0b00101101, 8),   // 44
    (0b00000100, 8),   // 45
    (0b00000101, 8),   // 46
    (0b00001010, 8),   // 47
    (0b00001011, 8),   // 48
    (0b01010010, 8),   // 49
    (0b01010011, 8),   // 50
    (0b01010100, 8),   // 51
    (0b01010101, 8),   // 52
    (0b00100100, 8),   // 53
    (0b00100101, 8),   // 54
    (0b01011000, 8),   // 55
    (0b01011001, 8),   // 56
    (0b01011010, 8),   // 57
    (0b01011011, 8),   // 58
    (0b01001010, 8),   // 59
    (0b01001011, 8),   // 60
    (0b00110010, 8),   // 61
    (0b00110011, 8),   // 62
    (0b00110100, 8),   // 63
  ];

  private static readonly (int Code, int BitLength)[] _BlackTerminating = [
    (0b0000110111, 10),  // 0
    (0b010, 3),          // 1
    (0b11, 2),           // 2
    (0b10, 2),           // 3
    (0b011, 3),          // 4
    (0b0011, 4),         // 5
    (0b0010, 4),         // 6
    (0b00011, 5),        // 7
    (0b000101, 6),       // 8
    (0b000100, 6),       // 9
    (0b0000100, 7),      // 10
    (0b0000101, 7),      // 11
    (0b0000111, 7),      // 12
    (0b00000100, 8),     // 13
    (0b00000111, 8),     // 14
    (0b000011000, 9),    // 15
    (0b0000010111, 10),  // 16
    (0b0000011000, 10),  // 17
    (0b0000001000, 10),  // 18
    (0b00001100111, 11), // 19
    (0b00001101000, 11), // 20
    (0b00001101100, 11), // 21
    (0b00000110111, 11), // 22
    (0b00000101000, 11), // 23
    (0b00000010111, 11), // 24
    (0b00000011000, 11), // 25
    (0b000011001010, 12), // 26
    (0b000011001011, 12), // 27
    (0b000011001100, 12), // 28
    (0b000011001101, 12), // 29
    (0b000001101000, 12), // 30
    (0b000001101001, 12), // 31
    (0b000001101010, 12), // 32
    (0b000001101011, 12), // 33
    (0b000011010010, 12), // 34
    (0b000011010011, 12), // 35
    (0b000011010100, 12), // 36
    (0b000011010101, 12), // 37
    (0b000011010110, 12), // 38
    (0b000011010111, 12), // 39
    (0b000001101100, 12), // 40
    (0b000001101101, 12), // 41
    (0b000011011010, 12), // 42
    (0b000011011011, 12), // 43
    (0b000001010100, 12), // 44
    (0b000001010101, 12), // 45
    (0b000001010110, 12), // 46
    (0b000001010111, 12), // 47
    (0b000001100100, 12), // 48
    (0b000001100101, 12), // 49
    (0b000001010010, 12), // 50
    (0b000001010011, 12), // 51
    (0b000000100100, 12), // 52
    (0b000000110111, 12), // 53
    (0b000000111000, 12), // 54
    (0b000000100111, 12), // 55
    (0b000000101000, 12), // 56
    (0b000001011000, 12), // 57
    (0b000001011001, 12), // 58
    (0b000000101011, 12), // 59
    (0b000000101100, 12), // 60
    (0b000001011010, 12), // 61
    (0b000001100110, 12), // 62
    (0b000001100111, 12), // 63
  ];

  private static readonly (int Code, int BitLength)[] _WhiteMakeUp = [
    (0b11011, 5),       // 64
    (0b10010, 5),       // 128
    (0b010111, 6),      // 192
    (0b0110111, 7),     // 256
    (0b00110110, 8),    // 320
    (0b00110111, 8),    // 384
    (0b01100100, 8),    // 448
    (0b01100101, 8),    // 512
    (0b01101000, 8),    // 576
    (0b01100111, 8),    // 640
    (0b011001100, 9),   // 704
    (0b011001101, 9),   // 768
    (0b011010010, 9),   // 832
    (0b011010011, 9),   // 896
    (0b011010100, 9),   // 960
    (0b011010101, 9),   // 1024
    (0b011010110, 9),   // 1088
    (0b011010111, 9),   // 1152
    (0b011011000, 9),   // 1216
    (0b011011001, 9),   // 1280
    (0b011011010, 9),   // 1344
    (0b011011011, 9),   // 1408
    (0b010011000, 9),   // 1472
    (0b010011001, 9),   // 1536
    (0b010011010, 9),   // 1600
    (0b011000, 6),      // 1664
    (0b010011011, 9),   // 1728
  ];

  private static readonly (int Code, int BitLength)[] _BlackMakeUp = [
    (0b0000001111, 10),   // 64
    (0b000011001000, 12), // 128
    (0b000011001001, 12), // 192
    (0b000001011011, 12), // 256
    (0b000000110011, 12), // 320
    (0b000000110100, 12), // 384
    (0b000000110101, 12), // 448
    (0b0000001101100, 13), // 512
    (0b0000001101101, 13), // 576
    (0b0000001001010, 13), // 640
    (0b0000001001011, 13), // 704
    (0b0000001001100, 13), // 768
    (0b0000001001101, 13), // 832
    (0b0000001110010, 13), // 896
    (0b0000001110011, 13), // 960
    (0b0000001110100, 13), // 1024
    (0b0000001110101, 13), // 1088
    (0b0000001110110, 13), // 1152
    (0b0000001110111, 13), // 1216
    (0b0000001010010, 13), // 1280
    (0b0000001010011, 13), // 1344
    (0b0000001010100, 13), // 1408
    (0b0000001010101, 13), // 1472
    (0b0000001011010, 13), // 1536
    (0b0000001011011, 13), // 1600
    (0b0000001100100, 13), // 1664
    (0b0000001100101, 13), // 1728
  ];

  // Vertical mode codes: V(0), VL(1), VR(1), VL(2), VR(2), VL(3), VR(3)
  private static readonly (int Code, int BitLength)[] _VerticalCodes = [
    (0b1, 1),        // V(0)
    (0b010, 3),      // VL(1)
    (0b011, 3),      // VR(1)
    (0b000010, 6),   // VL(2)
    (0b000011, 6),   // VR(2)
    (0b0000010, 7),  // VL(3)
    (0b0000011, 7),  // VR(3)
  ];

  private const int _PassCode = 0b0001;
  private const int _PassBitLength = 4;
  private const int _HorizontalCode = 0b001;
  private const int _HorizontalBitLength = 3;
  private const int _EofbCode = 0b000000000001;
  private const int _EofbBitLength = 12;

  #endregion

  #region Encoder

  /// <summary>Encodes 1bpp pixel data to MMR (CCITT Group 4) compressed bytes for JBIG2.</summary>
  internal static byte[] Encode(byte[] pixelData, int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    using var ms = new MemoryStream();
    var bitPos = 0;
    var currentByte = 0;

    var refLine = new byte[bytesPerRow];
    var codingLine = new byte[bytesPerRow];

    for (var row = 0; row < height; ++row) {
      Array.Copy(pixelData, row * bytesPerRow, codingLine, 0, bytesPerRow);
      _EncodeLine(ref currentByte, ref bitPos, ms, codingLine, refLine, width);
      Array.Copy(codingLine, refLine, bytesPerRow);
    }

    // JBIG2 MMR: write two EOFB codes to terminate
    _WriteBits(ref currentByte, ref bitPos, ms, _EofbCode, _EofbBitLength);
    _WriteBits(ref currentByte, ref bitPos, ms, _EofbCode, _EofbBitLength);

    if (bitPos > 0)
      ms.WriteByte((byte)(currentByte << (8 - bitPos)));

    return ms.ToArray();
  }

  private static void _EncodeLine(ref int currentByte, ref int bitPos, MemoryStream ms, byte[] codingLine, byte[] refLine, int width) {
    var a0 = -1;
    var a0Color = false; // false = white

    while (a0 < width) {
      var a1 = _FindChangingElement(codingLine, a0 < 0 ? 0 : a0, width, a0Color);
      var b1 = _FindChangingElement(refLine, a0 < 0 ? 0 : a0, width, a0Color);
      var b2 = b1 < width ? _FindChangingElement(refLine, b1, width, !a0Color) : width;

      if (b2 < a1) {
        _WriteBits(ref currentByte, ref bitPos, ms, _PassCode, _PassBitLength);
        a0 = b2;
      } else {
        var diff = a1 - b1;
        if (diff >= -3 && diff <= 3) {
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
          _WriteBits(ref currentByte, ref bitPos, ms, _HorizontalCode, _HorizontalBitLength);
          var a2 = _FindChangingElement(codingLine, a1, width, !a0Color);
          var runA0A1 = a1 - (a0 < 0 ? 0 : a0);
          var runA1A2 = a2 - a1;
          _EncodeRunLength(ref currentByte, ref bitPos, ms, runA0A1, !a0Color);
          _EncodeRunLength(ref currentByte, ref bitPos, ms, runA1A2, a0Color);
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
    var makeUpTable = isBlack ? _BlackMakeUp : _WhiteMakeUp;
    var termTable = isBlack ? _BlackTerminating : _WhiteTerminating;

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

  #endregion

  #region Decoder

  /// <summary>Decodes MMR (CCITT Group 4) compressed bytes to 1bpp pixel data for JBIG2.</summary>
  internal static byte[] Decode(byte[] compressedData, int width, int height) {
    var bytesPerRow = (width + 7) / 8;
    var pixelData = new byte[bytesPerRow * height];
    var reader = new _BitReader(compressedData);

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
    var a0Color = false;

    while (a0 < width) {
      var mode = _ReadMode(reader);
      if (mode < 0)
        return;

      switch (mode) {
        case 0: {
          var b1 = _FindChangingElement(refLine, a0 < 0 ? 0 : a0, width, a0Color);
          var b2 = b1 < width ? _FindChangingElement(refLine, b1, width, !a0Color) : width;
          if (a0Color)
            _SetBlackPixels(codingLine, a0 < 0 ? 0 : a0, b2 - (a0 < 0 ? 0 : a0));
          a0 = b2;
          break;
        }
        case 1: {
          var runA = _DecodeRunLength(reader, !a0Color);
          var runB = _DecodeRunLength(reader, a0Color);
          var startPos = a0 < 0 ? 0 : a0;
          if (!a0Color)
            _SetBlackPixels(codingLine, startPos + runA, runB);
          else
            _SetBlackPixels(codingLine, startPos, runA);
          a0 = startPos + runA + runB;
          break;
        }
        default: {
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

  private static int _ReadMode(_BitReader reader) {
    var bit = reader.ReadBit();
    if (bit < 0)
      return -1;

    if (bit == 1)
      return 2; // V(0)

    bit = reader.ReadBit();
    if (bit < 0)
      return -1;

    if (bit == 1) {
      bit = reader.ReadBit();
      if (bit < 0)
        return -1;

      return bit == 0 ? 3 : 4; // VL(1) / VR(1)
    }

    bit = reader.ReadBit();
    if (bit < 0)
      return -1;

    if (bit == 1)
      return 1; // Horizontal

    bit = reader.ReadBit();
    if (bit < 0)
      return -1;

    if (bit == 1)
      return 0; // Pass

    bit = reader.ReadBit();
    if (bit < 0)
      return -1;

    if (bit == 1) {
      bit = reader.ReadBit();
      if (bit < 0)
        return -1;

      return bit == 0 ? 5 : 6; // VL(2) / VR(2)
    }

    bit = reader.ReadBit();
    if (bit < 0)
      return -1;

    if (bit == 1) {
      bit = reader.ReadBit();
      if (bit < 0)
        return -1;

      return bit == 0 ? 7 : 8; // VL(3) / VR(3)
    }

    return -1; // EOFB or invalid
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
    var termTable = isBlack ? _BlackTerminating : _WhiteTerminating;
    var makeUpTable = isBlack ? _BlackMakeUp : _WhiteMakeUp;

    var accumulated = 0;
    var bitsRead = 0;
    const int maxBits = 13;

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

  private static void _SetBlackPixels(byte[] line, int start, int count) {
    for (var i = 0; i < count; ++i) {
      var px = start + i;
      var byteIndex = px >> 3;
      var bitIndex = 7 - (px & 7);
      if (byteIndex < line.Length)
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

  #endregion
}
