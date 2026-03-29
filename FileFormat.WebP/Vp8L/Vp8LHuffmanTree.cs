using System;

namespace FileFormat.WebP.Vp8L;

/// <summary>Canonical Huffman decoder for VP8L with two-level lookup table.</summary>
internal sealed class Vp8LHuffmanTree {

  /// <summary>Number of bits used for the primary lookup table.</summary>
  private const int _PRIMARY_BITS = 8;
  private const int _PRIMARY_SIZE = 1 << _PRIMARY_BITS; // 256

  /// <summary>
  /// Packed lookup entry: low 16 bits = symbol or secondary table offset, high 16 bits = code length.
  /// For primary table: if code length &lt;= PRIMARY_BITS, the entry is final (symbol + length).
  /// Otherwise it points to a secondary table (offset + secondary bits in high word).
  /// </summary>
  private readonly int[] _table;
  private readonly int _totalSize;

  // For single-symbol trees (all code lengths are 0 except one symbol with length 0 => simple tree)
  private readonly bool _isSingleSymbol;
  private readonly int _singleSymbol;

  private Vp8LHuffmanTree(int[] table, int totalSize, bool isSingleSymbol, int singleSymbol) {
    this._table = table;
    this._totalSize = totalSize;
    this._isSingleSymbol = isSingleSymbol;
    this._singleSymbol = singleSymbol;
  }

  /// <summary>Build a Huffman tree from code lengths.</summary>
  public static Vp8LHuffmanTree Build(int[] codeLengths, int alphabetSize) {
    // Count symbols per code length
    var maxLen = 0;
    var numSymbols = 0;
    for (var i = 0; i < alphabetSize; ++i) {
      if (codeLengths[i] == 0)
        continue;

      if (codeLengths[i] > maxLen)
        maxLen = codeLengths[i];
      ++numSymbols;
    }

    // Single-symbol or empty tree
    if (numSymbols <= 1) {
      var sym = -1;
      for (var i = 0; i < alphabetSize; ++i) {
        if (codeLengths[i] <= 0)
          continue;

        sym = i;
        break;
      }

      return new([], 0, true, sym >= 0 ? sym : 0);
    }

    // Count codes per length
    var blCount = new int[maxLen + 1];
    for (var i = 0; i < alphabetSize; ++i)
      if (codeLengths[i] > 0)
        ++blCount[codeLengths[i]];

    // Compute first code for each length (canonical Huffman)
    var nextCode = new int[maxLen + 1];
    var code = 0;
    for (var bits = 1; bits <= maxLen; ++bits) {
      code = (code + blCount[bits - 1]) << 1;
      nextCode[bits] = code;
    }

    // Assign codes to symbols
    var codes = new int[alphabetSize];
    var lengths = new int[alphabetSize];
    for (var i = 0; i < alphabetSize; ++i) {
      var len = codeLengths[i];
      if (len <= 0)
        continue;

      codes[i] = nextCode[len];
      lengths[i] = len;
      ++nextCode[len];
    }

    // Calculate table size: primary table + secondary tables
    var secondaryBitsNeeded = maxLen > _PRIMARY_BITS ? maxLen - _PRIMARY_BITS : 0;
    var totalSize = _PRIMARY_SIZE;

    if (secondaryBitsNeeded > 0) {
      // Count how many secondary tables we need and their sizes
      var secondaryCounts = new int[_PRIMARY_SIZE];
      for (var i = 0; i < alphabetSize; ++i) {
        if (lengths[i] <= _PRIMARY_BITS)
          continue;

        var primaryIndex = _ReverseBits(codes[i], lengths[i]) & (_PRIMARY_SIZE - 1);
        var secBits = lengths[i] - _PRIMARY_BITS;
        if (secBits > secondaryCounts[primaryIndex])
          secondaryCounts[primaryIndex] = secBits;
      }

      for (var i = 0; i < _PRIMARY_SIZE; ++i)
        if (secondaryCounts[i] > 0)
          totalSize += 1 << secondaryCounts[i];
    }

    var table = new int[totalSize];

    // Initialize all entries as invalid
    for (var i = 0; i < totalSize; ++i)
      table[i] = -1;

    // Track secondary table allocation
    var secondaryOffsets = new int[_PRIMARY_SIZE];
    var secondaryBits = new int[_PRIMARY_SIZE];
    var nextSecondary = _PRIMARY_SIZE;

    // First pass: allocate secondary tables for long codes
    if (secondaryBitsNeeded > 0)
      for (var i = 0; i < alphabetSize; ++i) {
        if (lengths[i] <= _PRIMARY_BITS)
          continue;

        var reversed = _ReverseBits(codes[i], lengths[i]);
        var primaryIndex = reversed & (_PRIMARY_SIZE - 1);
        var secBits = lengths[i] - _PRIMARY_BITS;

        if (secondaryBits[primaryIndex] >= secBits)
          continue;

        secondaryBits[primaryIndex] = secBits;
        secondaryOffsets[primaryIndex] = nextSecondary;
        nextSecondary += 1 << secBits;
      }

    // Second pass: actually populate secondary tables
    // We need to recalculate max secondary bits per primary index
    if (secondaryBitsNeeded > 0) {
      // Recalculate offsets properly: need max secBits per primary index
      var maxSecBits = new int[_PRIMARY_SIZE];
      for (var i = 0; i < alphabetSize; ++i) {
        if (lengths[i] <= _PRIMARY_BITS)
          continue;

        var reversed = _ReverseBits(codes[i], lengths[i]);
        var primaryIndex = reversed & (_PRIMARY_SIZE - 1);
        var secBits = lengths[i] - _PRIMARY_BITS;
        if (secBits > maxSecBits[primaryIndex])
          maxSecBits[primaryIndex] = secBits;
      }

      nextSecondary = _PRIMARY_SIZE;
      for (var i = 0; i < _PRIMARY_SIZE; ++i) {
        if (maxSecBits[i] <= 0)
          continue;

        secondaryBits[i] = maxSecBits[i];
        secondaryOffsets[i] = nextSecondary;
        // Store secondary table pointer in primary: offset | (secBits << 16) with length = PRIMARY_BITS + 1 as sentinel
        table[i] = secondaryOffsets[i] | (secondaryBits[i] << 16);
        nextSecondary += 1 << maxSecBits[i];
      }
    }

    // Fill primary table with short codes
    for (var i = 0; i < alphabetSize; ++i) {
      var len = lengths[i];
      if (len <= 0 || len > _PRIMARY_BITS)
        continue;

      var reversed = _ReverseBits(codes[i], len);
      // Fill all entries that share this prefix
      var step = 1 << len;
      for (var j = reversed; j < _PRIMARY_SIZE; j += step)
        table[j] = i | (len << 16);
    }

    // Fill secondary tables with long codes
    for (var i = 0; i < alphabetSize; ++i) {
      var len = lengths[i];
      if (len <= _PRIMARY_BITS)
        continue;

      var reversed = _ReverseBits(codes[i], len);
      var primaryIndex = reversed & (_PRIMARY_SIZE - 1);
      var secReversed = reversed >> _PRIMARY_BITS;
      var secLen = len - _PRIMARY_BITS;
      var secTableSize = 1 << secondaryBits[primaryIndex];
      var offset = secondaryOffsets[primaryIndex];
      var step = 1 << secLen;
      for (var j = secReversed; j < secTableSize; j += step)
        table[offset + j] = i | (len << 16);
    }

    return new(table, nextSecondary, false, 0);
  }

  /// <summary>Read one symbol from the bit reader.</summary>
  public int ReadSymbol(Vp8LBitReader reader) {
    if (this._isSingleSymbol)
      return this._singleSymbol;

    var bits = reader.PeekBits(_PRIMARY_BITS);
    var entry = this._table[(int)bits];

    var len = entry >> 16;
    if (len <= _PRIMARY_BITS) {
      reader.SkipBits(len);
      return entry & 0xFFFF;
    }

    // Secondary table lookup
    var secondaryOffset = entry & 0xFFFF;
    var secondaryBits = len; // stored secBits in high word
    reader.SkipBits(_PRIMARY_BITS);

    var secBits = reader.PeekBits(secondaryBits);
    var secEntry = this._table[secondaryOffset + (int)secBits];
    var secLen = (secEntry >> 16) - _PRIMARY_BITS;
    reader.SkipBits(secLen);
    return secEntry & 0xFFFF;
  }

  /// <summary>Read code lengths for an alphabet using the meta-Huffman code.</summary>
  public static int[] ReadCodeLengths(Vp8LBitReader reader, int alphabetSize) {
    // VP8L code length code order
    ReadOnlySpan<int> codeLengthOrder = [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    var numCodeLengthCodes = (int)reader.ReadBits(4) + 4;
    var codeLengthCodeLengths = new int[19];
    for (var i = 0; i < numCodeLengthCodes; ++i)
      codeLengthCodeLengths[codeLengthOrder[i]] = (int)reader.ReadBits(3);

    var metaTree = Build(codeLengthCodeLengths, 19);

    var codeLengths = new int[alphabetSize];
    var prevCodeLength = 8;
    var i2 = 0;
    while (i2 < alphabetSize) {
      var symbol = metaTree.ReadSymbol(reader);
      switch (symbol) {
        case < 16:
          codeLengths[i2] = symbol;
          if (symbol != 0)
            prevCodeLength = symbol;
          ++i2;
          break;
        case 16: {
          // Repeat previous code length 3-6 times
          var repeatCount = (int)reader.ReadBits(2) + 3;
          for (var j = 0; j < repeatCount && i2 < alphabetSize; ++j)
            codeLengths[i2++] = prevCodeLength;
          break;
        }
        case 17: {
          // Repeat 0 for 3-10 times
          var repeatCount = (int)reader.ReadBits(3) + 3;
          for (var j = 0; j < repeatCount && i2 < alphabetSize; ++j)
            codeLengths[i2++] = 0;
          break;
        }
        case 18: {
          // Repeat 0 for 11-138 times
          var repeatCount = (int)reader.ReadBits(7) + 11;
          for (var j = 0; j < repeatCount && i2 < alphabetSize; ++j)
            codeLengths[i2++] = 0;
          break;
        }
      }
    }

    return codeLengths;
  }

  /// <summary>Read a Huffman tree group (5 trees) or handle simple codes from the bitstream.</summary>
  public static Vp8LHuffmanTree ReadTree(Vp8LBitReader reader, int alphabetSize) {
    var isSimple = reader.ReadBits(1) == 1;
    if (isSimple)
      return _ReadSimpleCode(reader, alphabetSize);

    var codeLengths = ReadCodeLengths(reader, alphabetSize);
    return Build(codeLengths, alphabetSize);
  }

  private static Vp8LHuffmanTree _ReadSimpleCode(Vp8LBitReader reader, int alphabetSize) {
    var numSymbols = (int)reader.ReadBits(1) + 1;
    var isFirst8Bits = reader.ReadBits(1);
    var firstSymbol = (int)reader.ReadBits(isFirst8Bits == 1 ? 8 : 1);

    if (numSymbols == 1) {
      // Single symbol: code length 0 effectively, always this symbol
      var codeLens = new int[alphabetSize];
      if (firstSymbol < alphabetSize)
        codeLens[firstSymbol] = 1;
      return Build(codeLens, alphabetSize);
    }

    // Two symbols: assign code lengths of 1 to both
    var secondSymbol = (int)reader.ReadBits(8);
    var cl = new int[alphabetSize];
    if (firstSymbol < alphabetSize)
      cl[firstSymbol] = 1;
    if (secondSymbol < alphabetSize)
      cl[secondSymbol] = 1;
    return Build(cl, alphabetSize);
  }

  /// <summary>Reverse the lowest <paramref name="numBits"/> bits of <paramref name="val"/>.</summary>
  private static int _ReverseBits(int val, int numBits) {
    var result = 0;
    for (var i = 0; i < numBits; ++i) {
      result = (result << 1) | (val & 1);
      val >>= 1;
    }

    return result;
  }
}
