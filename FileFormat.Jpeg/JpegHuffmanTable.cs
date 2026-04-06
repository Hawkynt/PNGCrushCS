using System;

namespace FileFormat.Jpeg;

/// <summary>Huffman table with encode/decode accelerators.</summary>
internal sealed class JpegHuffmanTable {
  public byte[] Bits { get; init; } = new byte[16]; // BITS[1..16]
  public byte[] Values { get; init; } = []; // Symbol values

  // Decode accelerators
  public int[] MaxCode { get; private set; } = new int[18];
  public int[] ValOffset { get; private set; } = new int[18];

  // 8-bit decode lookup table: (symbol, bitsUsed); bitsUsed=0 means fallback needed
  public (int symbol, int bitsUsed)[]? LookupTable { get; private set; }

  // Encode accelerators
  public int[] EhufCo { get; private set; } = new int[256]; // Code for each symbol
  public int[] EhufSi { get; private set; } = new int[256]; // Code length for each symbol

  /// <summary>Builds decode and encode lookup tables from BITS/VALUES.</summary>
  public void BuildTables() {
    // Build decode tables (JPEG spec Figure F.15)
    var code = 0;
    var si = 0;
    MaxCode = new int[18];
    ValOffset = new int[18];

    for (var i = 0; i < 16; ++i) {
      if (Bits[i] == 0) {
        MaxCode[i + 1] = -1;
      } else {
        ValOffset[i + 1] = si - code;
        si += Bits[i];
        MaxCode[i + 1] = code + Bits[i] - 1;
        code += Bits[i];
      }
      code <<= 1;
    }
    MaxCode[17] = int.MaxValue;

    // Build encode tables
    EhufCo = new int[256];
    EhufSi = new int[256];
    Array.Fill(EhufSi, 0);

    code = 0;
    si = 0;
    for (var l = 0; l < 16; ++l) {
      for (var j = 0; j < Bits[l]; ++j) {
        if (si < Values.Length) {
          var symbol = Values[si];
          EhufCo[symbol] = code;
          EhufSi[symbol] = l + 1;
          ++si;
        }
        ++code;
      }
      code <<= 1;
    }

    // Build 8-bit decode lookup table
    LookupTable = new (int, int)[256];
    code = 0;
    si = 0;
    for (var l = 0; l < 16; ++l) {
      var codeLen = l + 1;
      for (var j = 0; j < Bits[l]; ++j) {
        if (codeLen <= 8 && si < Values.Length) {
          // Fill all 8-bit entries that start with this code
          var prefix = code << (8 - codeLen);
          var fillCount = 1 << (8 - codeLen);
          for (var f = 0; f < fillCount; ++f)
            LookupTable[prefix + f] = (Values[si], codeLen);
        }
        ++si;
        ++code;
      }
      code <<= 1;
    }
  }

  /// <summary>Creates a Huffman table from frequency counts (optimal Huffman construction).
  /// Follows libjpeg's jpeg_gen_optimal_table algorithm including pseudo-symbol 256
  /// to ensure the code space is never completely filled at any level.</summary>
  public static JpegHuffmanTable FromFrequencies(long[] freq, int maxSymbol) {
    // Collect symbols with non-zero frequency
    var count = 0;
    for (var i = 0; i <= maxSymbol; ++i)
      if (freq[i] > 0)
        ++count;

    if (count == 0) {
      // Empty table — return minimal valid table with a single dummy symbol
      var empty = new JpegHuffmanTable { Bits = new byte[16], Values = [0] };
      empty.Bits[0] = 1;
      empty.BuildTables();
      return empty;
    }

    // Build extended frequency array with pseudo-symbol (index maxSymbol+1)
    // The pseudo-symbol prevents the code space from being completely filled
    // at any level, which libjpeg's decoder validation requires.
    var numSymbols = maxSymbol + 2;
    var extFreq = new long[numSymbols];
    for (var i = 0; i <= maxSymbol; ++i)
      extFreq[i] = freq[i];
    extFreq[maxSymbol + 1] = 1; // pseudo-symbol

    // Compute code lengths via Huffman tree (no depth limit)
    var codeSizes = _BuildHuffmanTree(extFreq, numSymbols);

    // Convert code lengths to BITS[1..MAX_CLEN] — allow up to 32-bit depths
    const int maxClen = 32;
    var bits = new int[maxClen + 1]; // bits[1..maxClen]
    for (var i = 0; i < numSymbols; ++i)
      if (codeSizes[i] > 0)
        ++bits[codeSizes[i]];

    // Limit code lengths to 16 bits using JPEG spec Section K.2 algorithm
    for (var i = maxClen; i > 16; --i)
      while (bits[i] > 0) {
        var j = i - 2;
        while (j > 0 && bits[j] == 0)
          --j;
        bits[i] -= 2;     // remove two symbols from length i
        bits[i - 1]++;     // one goes in length i-1
        bits[j + 1] += 2;  // two new symbols in length j+1
        bits[j]--;          // one prefix freed from length j
      }

    // Remove pseudo-symbol from the longest code length
    var longest = 16;
    while (longest > 0 && bits[longest] == 0)
      --longest;
    if (longest > 0)
      --bits[longest];

    // Copy to 16-element BITS array
    var finalBits = new byte[16];
    for (var i = 1; i <= 16; ++i)
      finalBits[i - 1] = (byte)bits[i];

    // Generate VALUES: real symbols sorted by code length ascending, then by symbol value ascending
    var symbolList = new (int sym, int codeLen)[count];
    var si = 0;
    for (var i = 0; i <= maxSymbol; ++i)
      if (freq[i] > 0)
        symbolList[si++] = (i, codeSizes[i]);

    Array.Sort(symbolList, (a, b) => {
      var cmp = a.codeLen.CompareTo(b.codeLen);
      return cmp != 0 ? cmp : a.sym.CompareTo(b.sym);
    });

    // Total values count is sum of finalBits
    var totalValues = 0;
    for (var i = 0; i < 16; ++i)
      totalValues += finalBits[i];

    var values = new byte[totalValues];
    for (var i = 0; i < totalValues; ++i)
      values[i] = (byte)symbolList[i].sym;

    var table = new JpegHuffmanTable { Bits = finalBits, Values = values };
    table.BuildTables();
    return table;
  }

  /// <summary>Builds a Huffman tree and returns code lengths for each symbol index.</summary>
  private static int[] _BuildHuffmanTree(long[] freq, int numSymbols) {
    // Collect symbols with non-zero frequency
    var leaves = new (int idx, long freq)[numSymbols];
    var leafCount = 0;
    for (var i = 0; i < numSymbols; ++i)
      if (freq[i] > 0)
        leaves[leafCount++] = (i, freq[i]);

    var codeSizes = new int[numSymbols];
    if (leafCount <= 1) {
      if (leafCount == 1)
        codeSizes[leaves[0].idx] = 1;
      return codeSizes;
    }

    // Sort leaves by frequency ascending
    Array.Sort(leaves, 0, leafCount, System.Collections.Generic.Comparer<(int idx, long freq)>.Create(
      (a, b) => a.freq.CompareTo(b.freq)
    ));

    // Build Huffman tree using array-based merge (SortedSet for priority queue)
    var treeSize = leafCount * 2;
    var treeParent = new int[treeSize];
    Array.Fill(treeParent, -1);

    var next = leafCount;
    var pq = new System.Collections.Generic.SortedSet<(long freq, int idx)>();
    for (var i = 0; i < leafCount; ++i)
      pq.Add((leaves[i].freq, i));

    while (pq.Count > 1) {
      var min1 = pq.Min;
      pq.Remove(min1);
      var min2 = pq.Min;
      pq.Remove(min2);

      var combinedFreq = min1.freq + min2.freq;
      treeParent[min1.idx] = next;
      treeParent[min2.idx] = next;
      pq.Add((combinedFreq, next));
      ++next;
    }

    // Compute depths (code lengths) by walking from each leaf to root
    for (var i = 0; i < leafCount; ++i) {
      var depth = 0;
      var node = i;
      while (treeParent[node] >= 0) {
        ++depth;
        node = treeParent[node];
      }
      codeSizes[leaves[i].idx] = depth;
    }

    return codeSizes;
  }
}
