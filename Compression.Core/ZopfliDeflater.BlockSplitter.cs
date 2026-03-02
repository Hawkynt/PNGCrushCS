using System;
using System.Collections.Generic;

namespace Compression.Core;

public sealed partial class ZopfliDeflater {
  /// <summary>DP-based block splitting for optimal DEFLATE block boundaries</summary>
  internal static class BlockSplitter {
    private const int MIN_BLOCK_SIZE = 128;
    private const int CANDIDATE_INTERVAL = 1024;

    /// <summary>Split symbol array into optimal block ranges using Huffman-cost DP</summary>
    public static BlockRange[] Split(LzSymbol[] symbols) {
      if (symbols.Length <= MIN_BLOCK_SIZE * 2)
        return [new BlockRange(0, symbols.Length)];

      // Place candidate split points: fixed-interval + statistical
      var candidates = new List<int> { 0 };
      for (var i = CANDIDATE_INTERVAL; i < symbols.Length - MIN_BLOCK_SIZE; i += CANDIDATE_INTERVAL)
        candidates.Add(i);

      // Merge statistical candidates
      var statCandidates = _FindStatisticalCandidates(symbols);
      foreach (var sc in statCandidates)
        candidates.Add(sc);

      candidates.Add(symbols.Length);

      // Deduplicate and sort
      var candidateSet = new SortedSet<int>(candidates);
      candidates = [.. candidateSet];

      var n = candidates.Count;

      // dp[j] = minimum total bit cost for encoding symbols[0..candidates[j])
      var dp = new double[n];
      var splitFrom = new int[n];

      for (var j = 0; j < n; ++j)
        dp[j] = double.MaxValue;

      dp[0] = 0;

      for (var j = 1; j < n; ++j)
      for (var i = 0; i < j; ++i) {
        var start = candidates[i];
        var end = candidates[j];
        if (end - start < MIN_BLOCK_SIZE)
          continue;

        var cost = dp[i] + _EstimateBlockCost(symbols, start, end);
        if (cost >= dp[j])
          continue;

        dp[j] = cost;
        splitFrom[j] = i;
      }

      // Trace back to find block boundaries
      var blockEnds = new List<int>();
      var idx = n - 1;
      while (idx > 0) {
        blockEnds.Add(idx);
        idx = splitFrom[idx];
      }

      blockEnds.Reverse();

      // Convert to BlockRange array
      var ranges = new BlockRange[blockEnds.Count];
      var prevEnd = 0;
      for (var i = 0; i < blockEnds.Count; ++i) {
        var endIdx = candidates[blockEnds[i]];
        ranges[i] = new BlockRange(prevEnd, endIdx);
        prevEnd = endIdx;
      }

      return ranges;
    }

    /// <summary>
    ///   Find candidate split points where symbol statistics change significantly.
    ///   Uses a sliding window with frequency divergence detection.
    /// </summary>
    internal static List<int> _FindStatisticalCandidates(LzSymbol[] symbols) {
      const int WINDOW = 512;
      const double THRESHOLD = 0.3;
      const int STEP = 64;
      const int NUM_BUCKETS = 286; // lit/len code space

      var result = new List<int>();

      if (symbols.Length < WINDOW * 2)
        return result;

      // Sliding window: left half [pos-WINDOW, pos), right half [pos, pos+WINDOW)
      var leftFreq = new int[NUM_BUCKETS];
      var rightFreq = new int[NUM_BUCKETS];

      for (var pos = WINDOW; pos <= symbols.Length - WINDOW; pos += STEP) {
        // Build left window frequencies
        Array.Clear(leftFreq);
        var leftTotal = 0;
        for (var i = pos - WINDOW; i < pos; ++i) {
          var code = _SymbolToCode(symbols[i]);
          ++leftFreq[code];
          ++leftTotal;
        }

        // Build right window frequencies
        Array.Clear(rightFreq);
        var rightTotal = 0;
        for (var i = pos; i < pos + WINDOW; ++i) {
          var code = _SymbolToCode(symbols[i]);
          ++rightFreq[code];
          ++rightTotal;
        }

        if (leftTotal == 0 || rightTotal == 0)
          continue;

        // Compute L1 divergence between distributions
        var divergence = 0.0;
        var invLeft = 1.0 / leftTotal;
        var invRight = 1.0 / rightTotal;
        for (var i = 0; i < NUM_BUCKETS; ++i) {
          if (leftFreq[i] == 0 && rightFreq[i] == 0)
            continue;

          divergence += Math.Abs(leftFreq[i] * invLeft - rightFreq[i] * invRight);
        }

        if (divergence >= THRESHOLD && pos >= MIN_BLOCK_SIZE && pos <= symbols.Length - MIN_BLOCK_SIZE)
          result.Add(pos);
      }

      return result;
    }

    /// <summary>Map a symbol to its lit/len code for frequency counting</summary>
    private static int _SymbolToCode(LzSymbol sym) {
      return sym.Distance == 0 ? sym.LitLen : GetLengthCode(sym.LitLen);
    }

    /// <summary>Estimate encoding cost of a block using actual Huffman code lengths</summary>
    private static double _EstimateBlockCost(LzSymbol[] symbols, int start, int end) {
      // Count frequencies for this block range
      var litLenFreqs = new int[286];
      var distFreqs = new int[30];

      for (var i = start; i < end; ++i) {
        var sym = symbols[i];
        if (sym.Distance == 0) {
          ++litLenFreqs[sym.LitLen];
        } else {
          ++litLenFreqs[GetLengthCode(sym.LitLen)];
          ++distFreqs[GetDistanceCode(sym.Distance)];
        }
      }

      ++litLenFreqs[256]; // EOB

      // Build Huffman trees for this block
      var litLenTree = HuffmanTree.Build(litLenFreqs, 15);
      var distTree = HuffmanTree.Build(distFreqs, 15);

      // Sum actual symbol costs using Huffman code lengths
      double bits = 0;
      for (var i = 0; i < 286; ++i)
        if (litLenFreqs[i] > 0)
          bits += litLenFreqs[i] * litLenTree.Lengths[i];
      for (var i = 0; i < 30; ++i)
        if (distFreqs[i] > 0)
          bits += distFreqs[i] * distTree.Lengths[i];

      // Add extra bits cost
      for (var i = start; i < end; ++i) {
        var sym = symbols[i];
        if (sym.Distance == 0)
          continue;

        bits += LengthExtraBits[GetLengthCode(sym.LitLen) - 257];
        bits += DistanceExtraBits[GetDistanceCode(sym.Distance)];
      }

      // Estimate dynamic header overhead
      bits += _EstimateDynamicHeaderCost(litLenTree, distTree);
      return bits;
    }

    /// <summary>Estimate dynamic Huffman header cost in bits (HLIT/HDIST/HCLEN + code-length RLE)</summary>
    private static double _EstimateDynamicHeaderCost(HuffmanTree litLenTree, HuffmanTree distTree) {
      // 14 bits for HLIT(5) + HDIST(5) + HCLEN(4) header fields
      double bits = 14;

      // Determine HLIT and HDIST
      var hlit = 257;
      for (var i = litLenTree.Lengths.Length - 1; i >= 257; --i)
        if (litLenTree.Lengths[i] != 0) {
          hlit = i + 1;
          break;
        }

      var hdist = 1;
      for (var i = distTree.Lengths.Length - 1; i >= 0; --i)
        if (distTree.Lengths[i] != 0) {
          hdist = i + 1;
          break;
        }

      // Count distinct non-zero code lengths to estimate HCLEN
      var distinctLengths = 0;
      Span<bool> seen = stackalloc bool[16];
      for (var i = 0; i < hlit && i < litLenTree.Lengths.Length; ++i)
        if (litLenTree.Lengths[i] > 0 && !seen[litLenTree.Lengths[i]]) {
          seen[litLenTree.Lengths[i]] = true;
          ++distinctLengths;
        }

      for (var i = 0; i < hdist && i < distTree.Lengths.Length; ++i)
        if (distTree.Lengths[i] > 0 && !seen[distTree.Lengths[i]]) {
          seen[distTree.Lengths[i]] = true;
          ++distinctLengths;
        }

      // HCLEN entries (minimum 4, each 3 bits)
      var hclen = Math.Max(4, distinctLengths + 3);
      if (hclen > 19) hclen = 19;
      bits += hclen * 3;

      // Estimate RLE-encoded code lengths: roughly 4 bits per entry on average
      bits += (hlit + hdist) * 2.5;

      return bits;
    }
  }
}
