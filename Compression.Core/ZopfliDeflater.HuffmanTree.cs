using System;
using System.Collections.Generic;

namespace Compression.Core;

public sealed partial class ZopfliDeflater {
  /// <summary>Length-limited Huffman tree builder using Package-Merge algorithm with canonical code assignment</summary>
  internal sealed partial class HuffmanTree {
    private static readonly Lazy<HuffmanTree> _fixedTree = new(BuildFixed);
    private static readonly Lazy<HuffmanTree> _fixedDistTree = new(BuildFixedDistance);
    public readonly ushort[] Codes;
    public readonly byte[] Lengths;
    public readonly ushort[] ReversedCodes;

    private HuffmanTree(byte[] lengths, ushort[] codes) {
      this.Lengths = lengths;
      this.Codes = codes;
      this.ReversedCodes = _BuildReversedCodes(codes, lengths);
    }

    /// <summary>Cached fixed Huffman tree (RFC 1951 section 3.2.6)</summary>
    public static HuffmanTree Fixed => _fixedTree.Value;

    /// <summary>Cached fixed distance tree (all 5 bits, 32 symbols)</summary>
    public static HuffmanTree FixedDistance => _fixedDistTree.Value;

    /// <summary>Precompute LSB-first versions of MSB-first canonical codes</summary>
    private static ushort[] _BuildReversedCodes(ushort[] codes, byte[] lengths) {
      var n = codes.Length;
      var reversed = new ushort[n];
      for (var i = 0; i < n; ++i) {
        var len = lengths[i];
        if (len == 0)
          continue;

        var code = codes[i];
        var rev = 0;
        for (var b = 0; b < len; ++b) {
          rev = (rev << 1) | (code & 1);
          code >>= 1;
        }

        reversed[i] = (ushort)rev;
      }

      return reversed;
    }

    /// <summary>Build a length-limited Huffman tree from frequency counts</summary>
    public static HuffmanTree Build(int[] frequencies, int maxBits) {
      var n = frequencies.Length;
      var lengths = new byte[n];

      // Count non-zero frequencies
      var nonZeroCount = 0;
      var lastNonZero = -1;
      for (var i = 0; i < n; ++i) {
        if (frequencies[i] <= 0)
          continue;

        ++nonZeroCount;
        lastNonZero = i;
      }

      if (nonZeroCount == 0) {
        // All zeros: assign length 1 to symbol 0 so the tree is valid
        lengths[0] = 1;
        return new HuffmanTree(lengths, _BuildCanonicalCodes(lengths, maxBits));
      }

      if (nonZeroCount == 1) {
        // Single symbol: assign length 1
        lengths[lastNonZero] = 1;
        return new HuffmanTree(lengths, _BuildCanonicalCodes(lengths, maxBits));
      }

      // Package-Merge algorithm for length-limited Huffman codes
      _PackageMerge(frequencies, maxBits, lengths);
      return new HuffmanTree(lengths, _BuildCanonicalCodes(lengths, maxBits));
    }

    /// <summary>Build tree from fixed Huffman code lengths (RFC 1951 section 3.2.6)</summary>
    public static HuffmanTree BuildFixed() {
      var lengths = new byte[288];
      for (var i = 0; i <= 143; ++i) lengths[i] = 8;
      for (var i = 144; i <= 255; ++i) lengths[i] = 9;
      for (var i = 256; i <= 279; ++i) lengths[i] = 7;
      for (var i = 280; i <= 287; ++i) lengths[i] = 8;
      return new HuffmanTree(lengths, _BuildCanonicalCodes(lengths, 15));
    }

    /// <summary>Build fixed distance tree (all 5 bits, 32 symbols)</summary>
    public static HuffmanTree BuildFixedDistance() {
      var lengths = new byte[32];
      Array.Fill(lengths, (byte)5);
      return new HuffmanTree(lengths, _BuildCanonicalCodes(lengths, 15));
    }

    /// <summary>Package-Merge algorithm for optimal length-limited prefix codes (tree-based, memory-efficient)</summary>
    private static void _PackageMerge(int[] frequencies, int maxBits, byte[] lengths) {
      var n = frequencies.Length;

      // Build list of (frequency, symbolIndex) for active symbols, sorted by frequency
      var active = new List<(long freq, int symbol)>();
      for (var i = 0; i < n; ++i)
        if (frequencies[i] > 0)
          active.Add((frequencies[i], i));

      active.Sort((a, b) => a.freq != b.freq ? a.freq.CompareTo(b.freq) : a.symbol.CompareTo(b.symbol));

      var activeCount = active.Count;

      // All items stored in a flat list; each level references them by index
      var items = new List<PMItem>(activeCount * maxBits * 2);

      // Sorted item indices per level
      var prevSorted = Array.Empty<int>();

      for (var level = 0; level < maxBits; ++level) {
        var currentItems = new List<(long weight, int itemIndex)>(activeCount * 2);

        // Add leaves for this level
        for (var i = 0; i < activeCount; ++i) {
          var idx = items.Count;
          items.Add(new PMItem(active[i].freq, -1, i));
          currentItems.Add((active[i].freq, idx));
        }

        // Add packages by pairing consecutive items from previous level's sorted order
        for (var i = 0; i + 1 < prevSorted.Length; i += 2) {
          var leftIdx = prevSorted[i];
          var rightIdx = prevSorted[i + 1];
          var weight = items[leftIdx].Weight + items[rightIdx].Weight;
          var idx = items.Count;
          items.Add(new PMItem(weight, leftIdx, rightIdx));
          currentItems.Add((weight, idx));
        }

        // Sort by weight
        currentItems.Sort((a, b) => a.weight.CompareTo(b.weight));

        // Extract sorted item indices for next level's pairing
        prevSorted = new int[currentItems.Count];
        for (var i = 0; i < currentItems.Count; ++i)
          prevSorted[i] = currentItems[i].itemIndex;
      }

      // Take the first 2*activeCount - 2 items, walk each tree to count leaf appearances
      var needed = 2 * activeCount - 2;
      var totalCoverage = new int[activeCount];
      var stack = new Stack<int>(maxBits + 1);

      for (var i = 0; i < needed && i < prevSorted.Length; ++i) {
        stack.Push(prevSorted[i]);
        while (stack.Count > 0) {
          var item = items[stack.Pop()];
          if (item.IsLeaf) {
            ++totalCoverage[item.Right];
          } else {
            stack.Push(item.Left);
            stack.Push(item.Right);
          }
        }
      }

      // Coverage count = code length for each active symbol
      for (var i = 0; i < activeCount; ++i) {
        var len = totalCoverage[i];
        if (len > maxBits) len = maxBits;
        if (len < 1) len = 1;
        lengths[active[i].symbol] = (byte)len;
      }
    }

    /// <summary>Build canonical Huffman codes from code lengths</summary>
    private static ushort[] _BuildCanonicalCodes(byte[] lengths, int maxBits) {
      var n = lengths.Length;
      var codes = new ushort[n];
      var blCount = new int[maxBits + 1];

      for (var i = 0; i < n; ++i)
        if (lengths[i] > 0)
          ++blCount[lengths[i]];

      // Find the numerical value of the smallest code for each code length
      var nextCode = new int[maxBits + 1];
      var code = 0;
      for (var bits = 1; bits <= maxBits; ++bits) {
        code = (code + blCount[bits - 1]) << 1;
        nextCode[bits] = code;
      }

      // Assign codes
      for (var i = 0; i < n; ++i) {
        var len = lengths[i];
        if (len == 0)
          continue;

        codes[i] = (ushort)nextCode[len];
        ++nextCode[len];
      }

      return codes;
    }
  }
}
