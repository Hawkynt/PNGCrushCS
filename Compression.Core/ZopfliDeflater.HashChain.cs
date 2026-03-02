using System;

namespace Compression.Core;

public sealed partial class ZopfliDeflater {
  /// <summary>LZ77 hash-chain match finder with 3-byte rolling hash and 32KB sliding window</summary>
  internal sealed class HashChain {
    private const int WINDOW_SIZE = 32768;
    private const int HASH_BITS = 15;
    private const int HASH_SIZE = 1 << HASH_BITS;
    private const int HASH_MASK = HASH_SIZE - 1;
    private const int MIN_MATCH = 3;
    private const int MAX_MATCH = 258;

    private readonly byte[] _data;
    private readonly int _dataLength;

    // head[hash] = most recent position with this hash, -1 if none
    private readonly int[] _head;

    private readonly int _niceMatchLength;

    // prev[pos] = previous position in chain with same hash (-1 if none)
    // Sized to full data length to avoid aliasing when data > WINDOW_SIZE
    private readonly int[] _prev;

    public HashChain(byte[] data, int dataLength, int maxChainDepth, int niceMatchLength) {
      this._data = data;
      this._dataLength = dataLength;
      this.MaxChainDepth = maxChainDepth;
      this._niceMatchLength = Math.Min(niceMatchLength, MAX_MATCH);
      this._head = new int[HASH_SIZE];
      this._prev = new int[dataLength];
      Array.Fill(this._head, -1);
      Array.Fill(this._prev, -1);
    }

    public int MaxChainDepth { get; }

    /// <summary>Compute 3-byte hash at position using Knuth multiplicative hash for better distribution</summary>
    private int _Hash(int pos) {
      if (pos + 2 >= this._dataLength)
        return 0;

      var v = ((uint)this._data[pos] << 16) | ((uint)this._data[pos + 1] << 8) | this._data[pos + 2];
      return (int)((v * 2654435761u) >> (32 - HASH_BITS)) & HASH_MASK;
    }

    /// <summary>Insert position into hash chain (call for every byte advanced)</summary>
    public void Insert(int pos) {
      if (pos + 2 >= this._dataLength)
        return;

      var hash = this._Hash(pos);
      this._prev[pos] = this._head[hash];
      this._head[hash] = pos;
    }

    /// <summary>Estimate adaptive chain depth based on local data entropy</summary>
    /// <param name="data">Input data array</param>
    /// <param name="pos">Current position</param>
    /// <param name="dataLength">Total data length</param>
    /// <param name="baseDepth">Base chain depth to scale from</param>
    /// <returns>Adapted chain depth</returns>
    public static int EstimateLocalDepth(byte[] data, int pos, int dataLength, int baseDepth) {
      const int WINDOW = 64;
      var end = Math.Min(pos + WINDOW, dataLength);
      var start = pos;
      var len = end - start;
      if (len <= 0)
        return baseDepth;

      Span<bool> seen = stackalloc bool[256];
      var distinct = 0;
      for (var i = start; i < end; ++i) {
        var b = data[i];
        if (seen[b])
          continue;

        seen[b] = true;
        ++distinct;
      }

      var diversity = (double)distinct / len;

      // Low diversity (< 20%): double depth for better matches in repetitive data
      if (diversity < 0.20)
        return Math.Min(baseDepth * 2, 4096);

      // High diversity (> 80%): halve depth since matches are unlikely
      if (diversity > 0.80)
        return Math.Max(baseDepth / 2, 16);

      return baseDepth;
    }

    /// <summary>Find matches at the given position, returns count of matches written</summary>
    public int FindMatches(int pos, int maxLen, Span<LzMatch> matches, int depthOverride = -1) {
      if (pos + MIN_MATCH > this._dataLength)
        return 0;

      maxLen = Math.Min(maxLen, this._dataLength - pos);
      maxLen = Math.Min(maxLen, MAX_MATCH);

      if (maxLen < MIN_MATCH)
        return 0;

      var hash = this._Hash(pos);
      var chainPos = this._head[hash];
      var matchCount = 0;
      var bestLength = MIN_MATCH - 1;
      var limit = Math.Max(0, pos - WINDOW_SIZE);
      var chainSteps = 0;
      var effectiveDepth = depthOverride > 0 ? depthOverride : this.MaxChainDepth;

      var skipBudget = this._dataLength;
      while (chainPos >= limit && chainSteps < effectiveDepth) {
        if (chainPos >= pos) {
          chainPos = this._prev[chainPos];
          if (--skipBudget <= 0)
            break;
          continue;
        }

        // Limit match length by how much data remains at chainPos
        var availableAtChain = this._dataLength - chainPos;
        var effectiveMaxLen = Math.Min(maxLen, availableAtChain);

        // Quick reject: check first byte
        if (this._data[chainPos] != this._data[pos]) {
          chainPos = this._prev[chainPos];
          ++chainSteps;
          continue;
        }

        // Quick reject: check second byte (hash insertion guarantees pos+2 < dataLength)
        if (this._data[chainPos + 1] != this._data[pos + 1]) {
          chainPos = this._prev[chainPos];
          ++chainSteps;
          continue;
        }

        // Quick reject: check the byte at bestLength position
        if (bestLength >= MIN_MATCH && bestLength < effectiveMaxLen &&
            this._data[chainPos + bestLength] != this._data[pos + bestLength]) {
          chainPos = this._prev[chainPos];
          ++chainSteps;
          continue;
        }

        // Count actual match length
        var len = 0;
        while (len < effectiveMaxLen && this._data[chainPos + len] == this._data[pos + len])
          ++len;

        if (len >= MIN_MATCH && len > bestLength) {
          bestLength = len;
          if (matchCount < matches.Length)
            matches[matchCount++] = new LzMatch(len, pos - chainPos);
          else
            matches[matchCount - 1] = new LzMatch(len, pos - chainPos);

          if (bestLength >= this._niceMatchLength || bestLength >= maxLen)
            break;
        }

        chainPos = this._prev[chainPos];
        ++chainSteps;
      }

      return matchCount;
    }
  }
}
