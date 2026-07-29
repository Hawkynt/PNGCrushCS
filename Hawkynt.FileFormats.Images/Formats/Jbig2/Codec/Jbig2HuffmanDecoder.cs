using System;
using System.Collections.Generic;

namespace FileFormat.Jbig2.Codec;

/// <summary>Huffman table decoder for JBIG2 (ITU-T T.88 Annex B).
/// Implements standard tables B.1 through B.15 for integer decoding
/// used in Huffman-coded symbol dictionaries, text regions, and other segments.</summary>
internal sealed class Jbig2HuffmanDecoder {

  /// <summary>A single entry in a Huffman table.</summary>
  internal readonly record struct HuffmanEntry(int PrefixLength, int RangeLength, int RangeLow, bool IsLowerRange, bool IsOob);

  /// <summary>Decoded Huffman table for fast lookup.</summary>
  internal sealed class HuffmanTable {
    internal readonly HuffmanEntry[] Entries;

    internal HuffmanTable(HuffmanEntry[] entries) => Entries = entries;
  }

  private readonly byte[] _data;
  private int _offset;
  private int _bitPos;

  internal Jbig2HuffmanDecoder(byte[] data, int offset) {
    _data = data;
    _offset = offset;
    _bitPos = 0;
  }

  /// <summary>Current byte offset.</summary>
  internal int ByteOffset => _offset;

  /// <summary>Decodes one integer value using the given Huffman table.</summary>
  /// <param name="table">Huffman table to use.</param>
  /// <returns>The decoded integer, or null for OOB.</returns>
  internal int? DecodeValue(HuffmanTable table) {
    var code = 0;
    var codeLen = 0;

    while (codeLen < 32) {
      var bit = _ReadBit();
      if (bit < 0)
        return null;

      code = (code << 1) | bit;
      ++codeLen;

      foreach (var entry in table.Entries) {
        if (entry.PrefixLength != codeLen)
          continue;

        // Check if this entry's prefix matches
        var prefix = _ExtractPrefix(entry, codeLen, code);
        if (prefix < 0)
          continue;

        if (entry.IsOob)
          return null;

        if (entry.RangeLength == 0)
          return entry.RangeLow;

        if (entry.IsLowerRange) {
          // Lower range: read extra bits, subtract from range low
          var extra = _ReadBits(entry.RangeLength);
          return entry.RangeLow - extra;
        }

        {
          var extra = _ReadBits(entry.RangeLength);
          return entry.RangeLow + extra;
        }
      }
    }

    return null;
  }

  private int _ExtractPrefix(HuffmanEntry entry, int codeLen, int code) {
    if (entry.PrefixLength != codeLen)
      return -1;
    return code; // The caller already accumulated the code
  }

  private int _ReadBit() {
    if (_offset >= _data.Length)
      return -1;

    var bit = (_data[_offset] >> (7 - _bitPos)) & 1;
    ++_bitPos;
    if (_bitPos >= 8) {
      _bitPos = 0;
      ++_offset;
    }
    return bit;
  }

  private int _ReadBits(int count) {
    var value = 0;
    for (var i = 0; i < count; ++i) {
      var bit = _ReadBit();
      if (bit < 0)
        break;
      value = (value << 1) | bit;
    }
    return value;
  }

  // ---- Standard Huffman Tables (T.88 Annex B) ----

  /// <summary>Table B.1: DH in symbol dictionary.</summary>
  internal static readonly HuffmanTable TableB1 = _BuildTable([
    new(1, 4, 0, false, false),
    new(2, 8, 16, false, false),
    new(3, 16, 272, false, false),
    new(3, 32, 65808, false, false),
  ]);

  /// <summary>Table B.2: DW in symbol dictionary.</summary>
  internal static readonly HuffmanTable TableB2 = _BuildTable([
    new(1, 2, 0, false, false),
    new(2, 6, 4, false, false),
    new(3, 10, 68, false, false),
    new(3, 32, 1092, false, false),
  ]);

  /// <summary>Table B.3: BM size in symbol dictionary.</summary>
  internal static readonly HuffmanTable TableB3 = _BuildTable([
    new(1, 0, 0, false, false),
    new(2, 0, 0, false, true), // OOB
    new(3, 4, 0, false, false),
    new(4, 8, 16, false, false),
    new(5, 16, 272, false, false),
    new(6, 32, 65808, false, false),
  ]);

  /// <summary>Table B.4: Aggregate instances in symbol dictionary.</summary>
  internal static readonly HuffmanTable TableB4 = _BuildTable([
    new(1, 0, 1, false, false),
    new(2, 0, 2, false, false),
    new(3, 0, 3, false, false),
    new(4, 3, 4, false, false),
    new(5, 6, 12, false, false),
    new(5, 32, 76, false, false),
  ]);

  /// <summary>Table B.5: DT in text region.</summary>
  internal static readonly HuffmanTable TableB5 = _BuildTable([
    new(1, 0, 1, false, false),
    new(2, 0, 2, false, false),
    new(3, 0, 3, false, false),
    new(4, 3, 4, false, false),
    new(5, 6, 12, false, false),
    new(7, 32, 76, false, false),
  ]);

  /// <summary>Table B.6: First S in text region.</summary>
  internal static readonly HuffmanTable TableB6 = _BuildTable([
    new(1, 7, -128, false, false),
    new(2, 7, 0, false, false),
    new(3, 8, 128, false, false),
    new(3, 32, 384, false, false),
    new(3, 32, -384, true, false),
  ]);

  /// <summary>Table B.7: DS in text region.</summary>
  internal static readonly HuffmanTable TableB7 = _BuildTable([
    new(1, 5, -16, false, false),
    new(2, 5, 16, false, false),
    new(3, 6, 48, false, false),
    new(3, 32, 112, false, false),
    new(3, 32, -112, true, false),
    new(2, 0, 0, false, true), // OOB
  ]);

  /// <summary>Table B.8: Symbol ID in text region.</summary>
  internal static readonly HuffmanTable TableB8 = _BuildTable([
    new(1, 0, 0, false, false),
    new(2, 1, 1, false, false),
    new(3, 2, 3, false, false),
    new(4, 3, 7, false, false),
    new(5, 4, 15, false, false),
    new(6, 5, 31, false, false),
    new(7, 6, 63, false, false),
    new(7, 32, 127, false, false),
  ]);

  /// <summary>Table B.9: RDW in text region refinement.</summary>
  internal static readonly HuffmanTable TableB9 = _BuildTable([
    new(1, 0, 0, false, false),
    new(2, 3, 1, false, false),
    new(3, 5, 9, false, false),
    new(3, 32, 41, false, false),
    new(3, 32, -41, true, false),
  ]);

  /// <summary>Table B.10: RDH in text region refinement.</summary>
  internal static readonly HuffmanTable TableB10 = _BuildTable([
    new(1, 0, 0, false, false),
    new(2, 3, 1, false, false),
    new(3, 5, 9, false, false),
    new(3, 32, 41, false, false),
    new(3, 32, -41, true, false),
  ]);

  /// <summary>Table B.11: RDX in text region refinement.</summary>
  internal static readonly HuffmanTable TableB11 = _BuildTable([
    new(1, 0, 0, false, false),
    new(2, 3, -1, false, false),
    new(3, 5, 7, false, false),
    new(3, 32, 39, false, false),
    new(3, 32, -39, true, false),
  ]);

  /// <summary>Table B.12: RDY in text region refinement.</summary>
  internal static readonly HuffmanTable TableB12 = _BuildTable([
    new(1, 0, 0, false, false),
    new(2, 3, -1, false, false),
    new(3, 5, 7, false, false),
    new(3, 32, 39, false, false),
    new(3, 32, -39, true, false),
  ]);

  /// <summary>Table B.13: EXRUN length in symbol dict export.</summary>
  internal static readonly HuffmanTable TableB13 = _BuildTable([
    new(1, 0, 0, false, false),
    new(2, 2, 1, false, false),
    new(3, 4, 5, false, false),
    new(4, 8, 21, false, false),
    new(5, 32, 277, false, false),
  ]);

  /// <summary>Table B.14: Generic region (line height delta).</summary>
  internal static readonly HuffmanTable TableB14 = _BuildTable([
    new(1, 0, 0, false, false),
    new(2, 3, 0, false, false),
    new(3, 6, 0, false, false),
    new(3, 32, 0, false, false),
  ]);

  /// <summary>Table B.15: Generic region (line width delta).</summary>
  internal static readonly HuffmanTable TableB15 = _BuildTable([
    new(1, 0, 0, false, false),
    new(2, 2, 0, false, false),
    new(3, 4, 0, false, false),
    new(3, 32, 0, false, false),
  ]);

  /// <summary>Decodes a custom Huffman table from the data stream (T.88 B.2).</summary>
  internal static HuffmanTable DecodeCustomTable(byte[] data, ref int offset) {
    var entries = new List<HuffmanEntry>();

    if (offset + 2 > data.Length)
      return new HuffmanTable([]);

    var flags = data[offset++];
    var hasOob = (flags & 0x01) != 0;
    var hasLower = (flags & 0x02) != 0;
    var hasUpper = (flags & 0x04) != 0;

    // Low value (4 bytes BE)
    if (offset + 4 > data.Length)
      return new HuffmanTable([]);
    var lowValue = _ReadInt32BE(data, offset);
    offset += 4;

    // High value (4 bytes BE)
    if (offset + 4 > data.Length)
      return new HuffmanTable([]);
    var highValue = _ReadInt32BE(data, offset);
    offset += 4;

    // Read entries until we reach the high value
    var currentValue = lowValue;
    var prefixLen = 1;

    while (currentValue <= highValue && offset < data.Length) {
      var rangeLen = data[offset++];
      entries.Add(new HuffmanEntry(prefixLen, rangeLen, currentValue, false, false));
      currentValue += 1 << rangeLen;
      ++prefixLen;
    }

    if (hasLower)
      entries.Add(new HuffmanEntry(prefixLen++, 32, lowValue, true, false));

    if (hasUpper)
      entries.Add(new HuffmanEntry(prefixLen++, 32, highValue + 1, false, false));

    if (hasOob)
      entries.Add(new HuffmanEntry(prefixLen, 0, 0, false, true));

    return new HuffmanTable([.. entries]);
  }

  private static HuffmanTable _BuildTable(HuffmanEntry[] entries) => new(entries);

  private static int _ReadInt32BE(byte[] data, int offset)
    => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
