using System;
using System.Buffers;

namespace Compression.Core;

public sealed partial class ZopfliDeflater {
  /// <summary>Forward-DP shortest-path LZ77 parser using Huffman code lengths as bit costs</summary>
  internal static class OptimalParser {
    private const long INFINITY = long.MaxValue / 2;
    private const int MIN_MATCH = 3;

    /// <summary>Parse input using DP with given code length costs, returning optimal LzSymbol sequence</summary>
    public static LzSymbol[] Parse(byte[] input, int inputLength, HashChain hashChain, byte[] litLenLengths,
      byte[] distLengths) {
      // dp[i] = minimum bit cost to encode positions [0, i)
      var dpSize = inputLength + 1;
      var dp = ArrayPool<DpNode>.Shared.Rent(dpSize);
      try {
        for (var i = 0; i < dpSize; ++i)
          dp[i].Cost = INFINITY;

        dp[0].Cost = 0;

        Span<LzMatch> matchBuffer = stackalloc LzMatch[32];

        for (var i = 0; i < inputLength; ++i) {
          if (dp[i].Cost >= INFINITY)
            continue;

          // Literal edge: i → i+1
          var litByte = input[i];
          var litCost = dp[i].Cost + litLenLengths[litByte];
          if (litCost < dp[i + 1].Cost) {
            dp[i + 1].Cost = litCost;
            dp[i + 1].Length = 1;
            dp[i + 1].Distance = 0;
          }

          // Match edges with adaptive depth
          var maxLen = Math.Min(258, inputLength - i);
          var adaptiveDepth = HashChain.EstimateLocalDepth(input, i, inputLength, hashChain.MaxChainDepth);
          var matchCount = hashChain.FindMatches(i, maxLen, matchBuffer, adaptiveDepth);

          var prevMatchLen = MIN_MATCH - 1;
          var baseCost = dp[i].Cost;

          for (var m = 0; m < matchCount; ++m) {
            var match = matchBuffer[m];
            var startLen = prevMatchLen + 1;
            if (startLen < MIN_MATCH)
              startLen = MIN_MATCH;

            var distCode = GetDistanceCode(match.Distance);
            var distCostBase = distLengths[distCode] + DistanceExtraBits[distCode];

            for (var len = startLen; len <= match.Length; ++len) {
              var lengthCode = GetLengthCode(len);
              var matchCost = baseCost
                              + litLenLengths[lengthCode]
                              + LengthExtraBits[lengthCode - 257]
                              + distCostBase;

              var target = i + len;
              if (matchCost < dp[target].Cost) {
                dp[target].Cost = matchCost;
                dp[target].Length = len;
                dp[target].Distance = match.Distance;
              }
            }

            prevMatchLen = match.Length;
          }
        }

        // Traceback from dp[inputLength] to reconstruct symbol sequence
        return _Traceback(dp, input, inputLength);
      } finally {
        ArrayPool<DpNode>.Shared.Return(dp);
      }
    }

    /// <summary>Trace back through DP array to reconstruct LzSymbol sequence</summary>
    private static LzSymbol[] _Traceback(DpNode[] dp, byte[] input, int inputLength) {
      // Count symbols by walking backward
      var count = 0;
      var pos = inputLength;
      while (pos > 0) {
        ++count;
        var node = dp[pos];
        pos -= node.Distance > 0 ? node.Length : 1;
      }

      // Allocate exact-size array, walk backward again filling from end
      var symbols = new LzSymbol[count];
      pos = inputLength;
      var idx = count - 1;
      while (pos > 0) {
        var node = dp[pos];
        if (node.Distance > 0) {
          symbols[idx--] = new LzSymbol((ushort)node.Length, (ushort)node.Distance);
          pos -= node.Length;
        } else {
          symbols[idx--] = new LzSymbol(input[pos - 1], 0);
          --pos;
        }
      }

      return symbols;
    }
  }
}
