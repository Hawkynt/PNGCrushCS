using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Wsq;

/// <summary>Huffman coding for WSQ quantized coefficients.</summary>
internal static class WsqHuffman {

  /// <summary>A Huffman table definition with code lengths and values.</summary>
  public sealed class HuffmanTable {
    public byte[] CodeLengths { get; init; } = new byte[16]; // counts for code lengths 1..16
    public byte[] Values { get; init; } = [];

    // Derived decode structures
    internal int[]? MinCode;
    internal int[]? MaxCode;
    internal int[]? ValPtr;

    public void BuildDerived() {
      MinCode = new int[17];
      MaxCode = new int[17];
      ValPtr = new int[17];

      var code = 0;
      var valIdx = 0;
      for (var bits = 1; bits <= 16; ++bits) {
        MinCode[bits] = code;
        ValPtr[bits] = valIdx;
        var count = CodeLengths[bits - 1];
        if (count == 0) {
          MaxCode[bits] = -1;
        } else {
          MaxCode[bits] = code + count - 1;
          valIdx += count;
        }
        code = (code + count) << 1;
      }
    }
  }

  /// <summary>Bit-level writer for Huffman encoding.</summary>
  public sealed class BitWriter {
    private readonly MemoryStream _stream = new();
    private int _buffer;
    private int _bitsInBuffer;

    public void WriteBits(int value, int numBits) {
      for (var i = numBits - 1; i >= 0; --i) {
        _buffer = (_buffer << 1) | ((value >> i) & 1);
        ++_bitsInBuffer;
        if (_bitsInBuffer == 8) {
          _stream.WriteByte((byte)_buffer);
          _buffer = 0;
          _bitsInBuffer = 0;
        }
      }
    }

    public byte[] Flush() {
      if (_bitsInBuffer > 0) {
        _buffer <<= 8 - _bitsInBuffer;
        _stream.WriteByte((byte)_buffer);
        _buffer = 0;
        _bitsInBuffer = 0;
      }
      return _stream.ToArray();
    }
  }

  /// <summary>Bit-level reader for Huffman decoding.</summary>
  public sealed class BitReader {
    private readonly byte[] _data;
    private int _bytePos;
    private int _bitPos;

    public BitReader(byte[] data, int offset) {
      _data = data;
      _bytePos = offset;
      _bitPos = 7;
    }

    public int ReadBit() {
      if (_bytePos >= _data.Length)
        throw new InvalidDataException("Unexpected end of Huffman data.");

      var bit = (_data[_bytePos] >> _bitPos) & 1;
      --_bitPos;
      if (_bitPos < 0) {
        _bitPos = 7;
        ++_bytePos;
      }
      return bit;
    }

    public int ReadBits(int numBits) {
      var value = 0;
      for (var i = 0; i < numBits; ++i)
        value = (value << 1) | ReadBit();
      return value;
    }

    public int Position => _bytePos;
    public int BitPosition => _bitPos;
    public bool HasMore => _bytePos < _data.Length;
  }

  /// <summary>Decodes one Huffman symbol using the given table.</summary>
  public static int DecodeSymbol(BitReader reader, HuffmanTable table) {
    var code = 0;
    for (var bits = 1; bits <= 16; ++bits) {
      code = (code << 1) | reader.ReadBit();
      if (table.MaxCode![bits] >= 0 && code <= table.MaxCode[bits]) {
        var idx = table.ValPtr![bits] + code - table.MinCode![bits];
        return table.Values[idx];
      }
    }
    throw new InvalidDataException("Invalid Huffman code.");
  }

  /// <summary>Builds Huffman code assignments from a table for encoding.</summary>
  public static Dictionary<int, (int Code, int Length)> BuildEncodeTable(HuffmanTable table) {
    var result = new Dictionary<int, (int Code, int Length)>();
    var code = 0;
    var valIdx = 0;
    for (var bits = 1; bits <= 16; ++bits) {
      var count = table.CodeLengths[bits - 1];
      for (var i = 0; i < count; ++i) {
        result[table.Values[valIdx]] = (code, bits);
        ++code;
        ++valIdx;
      }
      code <<= 1;
    }
    return result;
  }

  /// <summary>Encodes a sequence of quantized indices with run-length coding for zeros, then Huffman encodes.</summary>
  public static byte[] Encode(int[] indices, HuffmanTable table) {
    var encodeMap = BuildEncodeTable(table);
    var writer = new BitWriter();

    var i = 0;
    while (i < indices.Length) {
      if (indices[i] == 0) {
        // Count zero run
        var runLen = 0;
        while (i < indices.Length && indices[i] == 0 && runLen < 255) {
          ++runLen;
          ++i;
        }
        // Encode zero-run: symbol 0 followed by run length (as 8-bit value)
        if (encodeMap.TryGetValue(0, out var zeroCode))
          writer.WriteBits(zeroCode.Code, zeroCode.Length);
        writer.WriteBits(runLen, 8);
      } else {
        var val = indices[i];
        var sign = val < 0 ? 1 : 0;
        var magnitude = Math.Abs(val);

        // Encode: category (bit length of magnitude) as Huffman symbol, then sign + magnitude bits
        var category = _BitLength(magnitude);
        if (encodeMap.TryGetValue(category, out var catCode))
          writer.WriteBits(catCode.Code, catCode.Length);

        // Write sign bit then magnitude bits
        writer.WriteBits(sign, 1);
        if (category > 1)
          writer.WriteBits(magnitude, category);
        // category == 1: magnitude is always 1, sign already written

        ++i;
      }
    }

    return writer.Flush();
  }

  /// <summary>Decodes Huffman-coded data back to quantized indices.</summary>
  public static int[] Decode(byte[] data, int dataOffset, int totalCoeffs, HuffmanTable table) {
    table.BuildDerived();
    var reader = new BitReader(data, dataOffset);
    var indices = new int[totalCoeffs];
    var pos = 0;

    while (pos < totalCoeffs) {
      if (!reader.HasMore)
        break;

      var symbol = DecodeSymbol(reader, table);

      if (symbol == 0) {
        // Zero-run: read 8-bit run length
        var runLen = reader.ReadBits(8);
        for (var j = 0; j < runLen && pos < totalCoeffs; ++j)
          indices[pos++] = 0;
      } else {
        // Category decode: read sign bit and magnitude
        var category = symbol;
        var sign = reader.ReadBit();
        var magnitude = category > 1 ? reader.ReadBits(category) : 1;
        indices[pos++] = sign == 1 ? -magnitude : magnitude;
      }
    }

    return indices;
  }

  /// <summary>Builds a Huffman table from coefficient frequency analysis.</summary>
  public static HuffmanTable BuildFromIndices(int[] indices) {
    // Count symbol frequencies (categories + zero-run marker)
    var freq = new Dictionary<int, int>();
    var i = 0;
    while (i < indices.Length) {
      if (indices[i] == 0) {
        freq.TryGetValue(0, out var c);
        freq[0] = c + 1;
        // Skip zero run
        while (i < indices.Length && indices[i] == 0)
          ++i;
      } else {
        var category = _BitLength(Math.Abs(indices[i]));
        freq.TryGetValue(category, out var c);
        freq[category] = c + 1;
        ++i;
      }
    }

    if (freq.Count == 0) {
      freq[0] = 1;
      freq[1] = 1;
    } else if (freq.Count == 1) {
      // Need at least 2 symbols for a valid Huffman tree
      var existing = new List<int>(freq.Keys)[0];
      var other = existing == 0 ? 1 : 0;
      freq[other] = 1;
    }

    // Build Huffman tree using package-merge-like approach
    // Simple approach: sort by frequency, assign code lengths
    var symbols = new List<(int Symbol, int Freq)>();
    foreach (var kvp in freq)
      symbols.Add((kvp.Key, kvp.Value));
    symbols.Sort((a, b) => a.Freq != b.Freq ? b.Freq.CompareTo(a.Freq) : a.Symbol.CompareTo(b.Symbol));

    // Assign code lengths (simple heuristic: most frequent gets shortest code)
    var codeLengths = new Dictionary<int, int>();
    var maxBits = Math.Min(16, Math.Max(2, _CeilLog2(symbols.Count)));
    for (var j = 0; j < symbols.Count; ++j) {
      var bits = Math.Min(16, 1 + j * (maxBits - 1) / Math.Max(1, symbols.Count - 1));
      codeLengths[symbols[j].Symbol] = bits;
    }

    // Convert to canonical Huffman table
    return _BuildCanonicalTable(codeLengths);
  }

  private static HuffmanTable _BuildCanonicalTable(Dictionary<int, int> codeLengths) {
    var lengthCounts = new byte[16];
    var sortedSymbols = new List<(int Symbol, int Length)>();
    foreach (var kvp in codeLengths)
      sortedSymbols.Add((kvp.Key, kvp.Value));
    sortedSymbols.Sort((a, b) => a.Length != b.Length ? a.Length.CompareTo(b.Length) : a.Symbol.CompareTo(b.Symbol));

    var values = new byte[sortedSymbols.Count];
    for (var j = 0; j < sortedSymbols.Count; ++j) {
      values[j] = (byte)sortedSymbols[j].Symbol;
      if (sortedSymbols[j].Length >= 1 && sortedSymbols[j].Length <= 16)
        ++lengthCounts[sortedSymbols[j].Length - 1];
    }

    // Verify the Huffman code assignment is valid (Kraft inequality)
    // If not, redistribute to make it valid
    _AdjustCodeLengths(lengthCounts, values.Length);

    return new HuffmanTable { CodeLengths = lengthCounts, Values = values };
  }

  private static void _AdjustCodeLengths(byte[] codeLengths, int numSymbols) {
    // Verify Kraft inequality: sum(2^(-Li)) <= 1
    var kraftSum = 0.0;
    for (var bits = 1; bits <= 16; ++bits)
      kraftSum += codeLengths[bits - 1] * Math.Pow(2.0, -bits);

    if (kraftSum > 1.0) {
      // Redistribute: use simple balanced assignment
      Array.Clear(codeLengths);
      if (numSymbols <= 0)
        return;

      var optBits = Math.Max(1, _CeilLog2(numSymbols));
      if (optBits > 16)
        optBits = 16;

      // Assign most symbols to optBits length
      var remaining = numSymbols;
      var capacity = 1 << optBits;
      if (capacity >= remaining) {
        codeLengths[optBits - 1] = (byte)remaining;
      } else {
        codeLengths[optBits - 1] = (byte)capacity;
        remaining -= capacity;
        for (var bits = optBits + 1; bits <= 16 && remaining > 0; ++bits) {
          var cap = 1 << bits;
          var take = Math.Min(remaining, cap);
          codeLengths[bits - 1] = (byte)take;
          remaining -= take;
        }
      }
    }
  }

  private static int _BitLength(int value) {
    if (value == 0)
      return 0;
    var bits = 0;
    var v = value;
    while (v > 0) {
      ++bits;
      v >>= 1;
    }
    return bits;
  }

  private static int _CeilLog2(int value) {
    if (value <= 1)
      return 1;
    var bits = 0;
    var v = value - 1;
    while (v > 0) {
      ++bits;
      v >>= 1;
    }
    return bits;
  }
}
