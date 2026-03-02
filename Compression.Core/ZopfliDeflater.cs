using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Compression.Core;

/// <summary>Custom Zopfli-class DEFLATE encoder producing standard zlib-wrapped output</summary>
public sealed partial class ZopfliDeflater {
  // LzSymbol convention:
  //   Literal: LitLen = byte value (0-255), Distance = 0
  //   Match:   LitLen = actual length (3-258), Distance = actual distance (1-32768)
  // The distinction is: Distance > 0 means match, Distance == 0 means literal.

  /// <summary>Compress data to zlib-wrapped DEFLATE format</summary>
  /// <param name="data">Input data to compress</param>
  /// <param name="hyper">If true, use full Zopfli-class iterative refinement + block splitting</param>
  /// <param name="iterations">Number of iterations for Hyper mode (ignored for Ultra)</param>
  public static byte[] Compress(ReadOnlySpan<byte> data, bool hyper, int iterations = 15) {
    if (iterations < 1)
      iterations = 1;

    // Copy input to byte array for hash chain
    var input = data.ToArray();
    var inputLength = input.Length;

    // Compute Adler-32 checksum of uncompressed data
    var adler = _ComputeAdler32(data);

    // Handle empty input
    if (inputLength == 0) {
      var emptyDeflate = _EncodeEmptyBlock();
      return _WrapZlib(emptyDeflate, adler);
    }

    // Build hash chains — in Hyper mode, build both in parallel
    var ultraChain = new HashChain(input, inputLength, 256, 64);
    HashChain hyperChain = null;

    if (hyper) {
      hyperChain = new HashChain(input, inputLength, 2048, 258);
      Parallel.Invoke(
        () => {
          for (var i = 0; i < inputLength; ++i) ultraChain.Insert(i);
        },
        () => {
          for (var i = 0; i < inputLength; ++i) hyperChain.Insert(i);
        }
      );
    } else {
      for (var i = 0; i < inputLength; ++i)
        ultraChain.Insert(i);
    }

    // Ultra parse: 2-pass DP optimal parsing
    var ultraSymbols = _CompressUltra(input, inputLength, ultraChain);
    var deflateData = _EncodeBestBlock(ultraSymbols, true);

    if (hyper) {
      // Also try Ultra parse with the deeper hash chain
      var ultraDeepSymbols = _CompressUltra(input, inputLength, hyperChain);
      var ultraDeepEncoded = _EncodeBestBlock(ultraDeepSymbols, true);
      if (ultraDeepEncoded.Length < deflateData.Length)
        deflateData = ultraDeepEncoded;

      // Hyper: iterative refinement starting from best Ultra symbols, plus block splitting
      var bestUltraSymbols = ultraDeepEncoded.Length <= deflateData.Length ? ultraDeepSymbols : ultraSymbols;
      var hyperSymbols = _CompressHyper(input, inputLength, hyperChain, iterations, bestUltraSymbols);

      var hyperSingle = _EncodeBestBlock(hyperSymbols, true);
      if (hyperSingle.Length < deflateData.Length)
        deflateData = hyperSingle;

      var splitData = _EncodeWithBlockSplitting(hyperSymbols, iterations, input, inputLength, hyperChain);
      if (splitData.Length < deflateData.Length)
        deflateData = splitData;
    }

    // Assemble zlib stream
    return _WrapZlib(deflateData, adler);
  }

  /// <summary>Ultra compression: 2-pass DP optimal parsing, single block</summary>
  private static LzSymbol[] _CompressUltra(byte[] input, int inputLength, HashChain hashChain) {
    // Pass 1: greedy parse to get initial statistics
    var greedySymbols = _GreedyParse(input, inputLength, hashChain);

    // Build initial trees from greedy statistics
    var (litLenFreqs, distFreqs) = _CountFrequencies(greedySymbols);
    var litLenTree = HuffmanTree.Build(litLenFreqs, 15);
    var distTree = HuffmanTree.Build(distFreqs, 15);

    // Pass 2: DP optimal parse using tree code lengths as costs
    var pass2Symbols = OptimalParser.Parse(input, inputLength, hashChain, litLenTree.Lengths, distTree.Lengths);

    // Rebuild trees from pass 2
    (litLenFreqs, distFreqs) = _CountFrequencies(pass2Symbols);
    litLenTree = HuffmanTree.Build(litLenFreqs, 15);
    distTree = HuffmanTree.Build(distFreqs, 15);

    // Final pass with refined trees
    return OptimalParser.Parse(input, inputLength, hashChain, litLenTree.Lengths, distTree.Lengths);
  }

  /// <summary>Hyper compression: N-iteration refinement starting from Ultra symbols, with convergence detection</summary>
  private static LzSymbol[] _CompressHyper(byte[] input, int inputLength, HashChain hashChain, int iterations,
    LzSymbol[] ultraSymbols) {
    var symbols = ultraSymbols;
    var prevHash = _SymbolsHash(symbols);

    // Further iterative refinement beyond Ultra's 2 passes
    for (var iter = 0; iter < iterations; ++iter) {
      var (litLenFreqs, distFreqs) = _CountFrequencies(symbols);
      var litLenTree = HuffmanTree.Build(litLenFreqs, 15);
      var distTree = HuffmanTree.Build(distFreqs, 15);
      symbols = OptimalParser.Parse(input, inputLength, hashChain, litLenTree.Lengths, distTree.Lengths);

      var newHash = _SymbolsHash(symbols);
      if (newHash == prevHash)
        break;

      prevHash = newHash;
    }

    return symbols;
  }

  /// <summary>Compute a quick hash of LZ symbols for convergence detection</summary>
  private static long _SymbolsHash(LzSymbol[] symbols) {
    var hash = (long)symbols.Length;
    foreach (var sym in symbols)
      hash = hash * 31 + sym.LitLen + sym.Distance * 65537L;
    return hash;
  }

  /// <summary>Greedy LZ77 parse with lazy matching for initial statistics</summary>
  private static LzSymbol[] _GreedyParse(byte[] input, int inputLength, HashChain hashChain) {
    var rental = ArrayPool<LzSymbol>.Shared.Rent(inputLength);
    try {
      Span<LzMatch> matchBuffer = stackalloc LzMatch[16];
      Span<LzMatch> nextMatchBuffer = stackalloc LzMatch[16];
      var count = 0;
      var pos = 0;

      while (pos < inputLength) {
        var maxLen = Math.Min(258, inputLength - pos);
        var matchCount = hashChain.FindMatches(pos, maxLen, matchBuffer);

        if (matchCount > 0) {
          var best = matchBuffer[matchCount - 1]; // last match is longest

          // Lazy matching: check if emitting a literal + taking the pos+1 match is cheaper
          if (best.Length < 258 && pos + 1 < inputLength) {
            var nextMaxLen = Math.Min(258, inputLength - pos - 1);
            var nextMatchCount = hashChain.FindMatches(pos + 1, nextMaxLen, nextMatchBuffer);
            if (nextMatchCount > 0) {
              var nextBest = nextMatchBuffer[nextMatchCount - 1];
              // Compare approximate bit costs using fixed Huffman tree estimates
              var currentCost = _EstimateMatchCost(best.Length, best.Distance);
              var lazyCost = _EstimateLiteralCost(input[pos]) +
                             _EstimateMatchCost(nextBest.Length, nextBest.Distance);
              if (lazyCost < currentCost) {
                // Emit literal at pos, let the better match be found at pos+1
                rental[count++] = new LzSymbol(input[pos], 0);
                ++pos;
                continue;
              }
            }
          }

          rental[count++] = new LzSymbol((ushort)best.Length, (ushort)best.Distance);
          pos += best.Length;
        } else {
          rental[count++] = new LzSymbol(input[pos], 0);
          ++pos;
        }
      }

      var result = new LzSymbol[count];
      Array.Copy(rental, result, count);
      return result;
    } finally {
      ArrayPool<LzSymbol>.Shared.Return(rental);
    }
  }

  /// <summary>Estimate the bit cost of a literal using fixed Huffman tree</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _EstimateLiteralCost(byte value) {
    // Fixed Huffman: 0-143 → 8 bits, 144-255 → 9 bits
    return value <= 143 ? 8 : 9;
  }

  /// <summary>Estimate the bit cost of a length+distance match using fixed Huffman tree</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int _EstimateMatchCost(int length, int distance) {
    var lengthCode = GetLengthCode(length);
    // Fixed Huffman: length codes 257-279 → 7 bits, 280-287 → 8 bits
    var lengthCodeBits = lengthCode <= 279 ? 7 : 8;
    var lengthExtra = GetLengthExtraBitCount(lengthCode);
    var distCode = GetDistanceCode(distance);
    // Distance codes always use 5 bits in fixed Huffman
    var distCodeBits = 5;
    var distExtra = GetDistanceExtraBitCount(distCode);
    return lengthCodeBits + lengthExtra + distCodeBits + distExtra;
  }

  /// <summary>Count lit/len and distance frequencies from symbols</summary>
  internal static (int[] litLen, int[] dist) _CountFrequencies(LzSymbol[] symbols) {
    var litLenFreqs = new int[286];
    var distFreqs = new int[30];

    foreach (var sym in symbols)
      if (sym.Distance == 0) {
        // Literal: LitLen is 0-255
        ++litLenFreqs[sym.LitLen];
      } else {
        // Match: LitLen is actual length (3-258), convert to code
        var lengthCode = GetLengthCode(sym.LitLen);
        ++litLenFreqs[lengthCode];
        var distCode = GetDistanceCode(sym.Distance);
        ++distFreqs[distCode];
      }

    // End-of-block must be present
    ++litLenFreqs[256];

    return (litLenFreqs, distFreqs);
  }

  /// <summary>Encode an empty final block (fixed Huffman with just EOB)</summary>
  private static byte[] _EncodeEmptyBlock() {
    var writer = new BitWriter(16);
    try {
      // BFINAL=1, BTYPE=01 (fixed Huffman)
      writer.WriteBits(1, 1);
      writer.WriteBits(1, 2);
      // EOB symbol (256) in fixed Huffman: 7 bits, code 0000000
      var fixedTree = HuffmanTree.Fixed;
      writer.WriteBits(fixedTree.ReversedCodes[256], fixedTree.Lengths[256]);
      writer.AlignToByte();
      return writer.GetOutput();
    } finally {
      writer.Release();
    }
  }

  /// <summary>Encode symbols as a single block, choosing the best block type (fixed vs dynamic Huffman)</summary>
  private static byte[] _EncodeBestBlock(LzSymbol[] symbols, bool isFinal) {
    var (litLenFreqs, distFreqs) = _CountFrequencies(symbols);
    var litLenTree = HuffmanTree.Build(litLenFreqs, 15);
    var distTree = HuffmanTree.Build(distFreqs, 15);

    // Pre-compute dynamic header data once (RLE encoding, code-length tree, HLIT/HDIST/HCLEN)
    var dynHeader = _BuildDynamicHeader(litLenTree, distTree);

    // Measure both encodings arithmetically to avoid throwaway BitWriter passes
    var fixedBits = 3 + _MeasureSymbolBits(symbols, HuffmanTree.Fixed, HuffmanTree.FixedDistance);
    var dynamicBits = 3 + _MeasureDynamicHeaderBitsFromCache(dynHeader) +
                      _MeasureSymbolBits(symbols, litLenTree, distTree);

    // Encode only the winner
    var useFixed = fixedBits <= dynamicBits;
    var writer = new BitWriter(symbols.Length * 2 + 256);
    try {
      if (useFixed)
        _WriteFixedBlock(writer, symbols, isFinal);
      else
        _WriteDynamicBlockFromCache(writer, symbols, litLenTree, distTree, dynHeader, isFinal);
      return writer.GetOutput();
    } finally {
      writer.Release();
    }
  }

  /// <summary>Measure total bits for encoding symbols + EOB using given Huffman trees (no header)</summary>
  private static long _MeasureSymbolBits(LzSymbol[] symbols, HuffmanTree litLenTree, HuffmanTree distTree) {
    var bits = 0L;
    foreach (var sym in symbols)
      if (sym.Distance == 0) {
        bits += litLenTree.Lengths[sym.LitLen];
      } else {
        var lengthCode = GetLengthCode(sym.LitLen);
        bits += litLenTree.Lengths[lengthCode] + GetLengthExtraBitCount(lengthCode);
        var distCode = GetDistanceCode(sym.Distance);
        bits += distTree.Lengths[distCode] + GetDistanceExtraBitCount(distCode);
      }

    // EOB
    bits += litLenTree.Lengths[256];
    return bits;
  }

  /// <summary>Build and cache dynamic header data for a lit/len + distance tree pair</summary>
  private static DynamicHeader _BuildDynamicHeader(HuffmanTree litLenTree, HuffmanTree distTree) {
    // Determine HLIT
    var hlit = 257;
    for (var i = litLenTree.Lengths.Length - 1; i >= 257; --i)
      if (litLenTree.Lengths[i] != 0) {
        hlit = i + 1;
        break;
      }

    // Determine HDIST
    var hdist = 1;
    for (var i = distTree.Lengths.Length - 1; i >= 0; --i)
      if (distTree.Lengths[i] != 0) {
        hdist = i + 1;
        break;
      }

    // Build combined code lengths array for RLE encoding
    var totalCodeLengths = hlit + hdist;
    var codeLengths = new byte[totalCodeLengths];
    var litCount = Math.Min(hlit, litLenTree.Lengths.Length);
    for (var i = 0; i < litCount; ++i)
      codeLengths[i] = litLenTree.Lengths[i];
    var distCount = Math.Min(hdist, distTree.Lengths.Length);
    for (var i = 0; i < distCount; ++i)
      codeLengths[hlit + i] = distTree.Lengths[i];

    // RLE encode code lengths (computed once)
    var rleCodes = _RleEncodeCodeLengths(codeLengths);

    // Build code-length tree
    var clFreqs = new int[19];
    foreach (var (code, _, _) in rleCodes)
      ++clFreqs[code];
    var clTree = HuffmanTree.Build(clFreqs, 7);

    // Determine HCLEN
    var hclen = 4;
    for (var i = 18; i >= 4; --i)
      if (clTree.Lengths[CodeLengthOrder[i]] != 0) {
        hclen = i + 1;
        break;
      }

    return new DynamicHeader(rleCodes, clTree, hlit, hdist, hclen);
  }

  /// <summary>Measure exact bit count for the dynamic Huffman header from cached data</summary>
  private static long _MeasureDynamicHeaderBitsFromCache(DynamicHeader header) {
    var bits = 14L + header.Hclen * 3;
    foreach (var (code, extraBits, _) in header.RleCodes)
      bits += header.ClTree.Lengths[code] + extraBits;

    return bits;
  }

  /// <summary>Write a fixed Huffman block (RFC 1951 section 3.2.6)</summary>
  private static void _WriteFixedBlock(BitWriter writer, LzSymbol[] symbols, bool isFinal) {
    var litLenTree = HuffmanTree.Fixed;
    var distTree = HuffmanTree.FixedDistance;

    // BFINAL + BTYPE=01 (fixed Huffman)
    writer.WriteBits(isFinal ? 1u : 0u, 1);
    writer.WriteBits(1, 2); // BTYPE = fixed

    // Write symbols
    _WriteSymbols(writer, symbols, litLenTree, distTree);

    // Write end-of-block (symbol 256)
    writer.WriteBits(litLenTree.ReversedCodes[256], litLenTree.Lengths[256]);
  }

  /// <summary>Encode with block splitting (Hyper mode), selecting best block type per block, with optional per-block reparse</summary>
  private static byte[] _EncodeWithBlockSplitting(LzSymbol[] symbols, int iterations, byte[] input = null,
    int inputLength = 0, HashChain hashChain = null) {
    // Split symbols into blocks
    var blocks = BlockSplitter.Split(symbols);

    // Compute byte ranges for each block (needed for reparse)
    int[]? blockByteStarts = null;
    int[]? blockByteEnds = null;
    if (input != null && hashChain != null) {
      blockByteStarts = new int[blocks.Length];
      blockByteEnds = new int[blocks.Length];

      var bytePos = 0;
      var symIdx = 0;
      for (var b = 0; b < blocks.Length; ++b) {
        blockByteStarts[b] = bytePos;
        for (; symIdx < blocks[b].End; ++symIdx) {
          var sym = symbols[symIdx];
          bytePos += sym.Distance == 0 ? 1 : sym.LitLen;
        }

        blockByteEnds[b] = bytePos;
      }
    }

    // Phase 1: parallel processing — build trees, optionally reparse each block
    var blockData =
      new (LzSymbol[] symbols, HuffmanTree litLen, HuffmanTree dist, DynamicHeader dynHeader, bool useFixed)
        [blocks.Length];
    Parallel.For(0, blocks.Length, blockIdx => {
      var block = blocks[blockIdx];
      var blockSymbols = new LzSymbol[block.End - block.Start];
      Array.Copy(symbols, block.Start, blockSymbols, 0, blockSymbols.Length);

      var (litFreqs, distFreqs) = _CountFrequencies(blockSymbols);
      var litLenTree = HuffmanTree.Build(litFreqs, 15);
      var distTree = HuffmanTree.Build(distFreqs, 15);

      // Per-block reparse: re-optimize LZ77 parse using block-specific Huffman trees
      if (input != null && hashChain != null && blockByteStarts != null && blockByteEnds != null) {
        var byteStart = blockByteStarts[blockIdx];
        var byteEnd = blockByteEnds[blockIdx];
        var blockLen = byteEnd - byteStart;

        if (blockLen > 0) {
          // Rent a sub-array from the pool for the optimal parser
          var subInput = ArrayPool<byte>.Shared.Rent(blockLen);
          try {
            Array.Copy(input, byteStart, subInput, 0, blockLen);

            // Build a hash chain for this sub-block
            var subChain = new HashChain(subInput, blockLen, hashChain.MaxChainDepth, 258);
            for (var i = 0; i < blockLen; ++i)
              subChain.Insert(i);

            // Reparse with block-specific trees
            var reparsed = OptimalParser.Parse(subInput, blockLen, subChain, litLenTree.Lengths,
              distTree.Lengths);

            // Only use reparsed if it produces a smaller block
            var originalBits = _MeasureSymbolBits(blockSymbols, litLenTree, distTree);
            var (reLitFreqs, reDistFreqs) = _CountFrequencies(reparsed);
            var reLitTree = HuffmanTree.Build(reLitFreqs, 15);
            var reDistTree = HuffmanTree.Build(reDistFreqs, 15);
            var reparsedBits = _MeasureSymbolBits(reparsed, reLitTree, reDistTree);

            if (reparsedBits < originalBits) {
              blockSymbols = reparsed;
              litLenTree = reLitTree;
              distTree = reDistTree;
            }
          } finally {
            ArrayPool<byte>.Shared.Return(subInput);
          }
        }
      }

      // Pre-compute dynamic header (RLE encoding cached)
      var dynHeader = _BuildDynamicHeader(litLenTree, distTree);

      // Measure both encodings arithmetically
      var fixedBits = 3 + _MeasureSymbolBits(blockSymbols, HuffmanTree.Fixed, HuffmanTree.FixedDistance);
      var dynamicBits = 3 + _MeasureDynamicHeaderBitsFromCache(dynHeader) +
                        _MeasureSymbolBits(blockSymbols, litLenTree, distTree);

      blockData[blockIdx] = (blockSymbols, litLenTree, distTree, dynHeader, fixedBits <= dynamicBits);
    });

    // Phase 2: sequential write to shared BitWriter
    var writer = new BitWriter(symbols.Length * 2 + 256);
    try {
      for (var i = 0; i < blocks.Length; ++i) {
        var (blockSymbols, litLenTree, distTree, dynHeader, useFixed) = blockData[i];
        var isLast = i == blocks.Length - 1;
        if (useFixed)
          _WriteFixedBlock(writer, blockSymbols, isLast);
        else
          _WriteDynamicBlockFromCache(writer, blockSymbols, litLenTree, distTree, dynHeader, isLast);
      }

      return writer.GetOutput();
    } finally {
      writer.Release();
    }
  }

  /// <summary>Write a dynamic Huffman block using pre-computed header data</summary>
  private static void _WriteDynamicBlockFromCache(BitWriter writer, LzSymbol[] symbols, HuffmanTree litLenTree,
    HuffmanTree distTree, DynamicHeader header, bool isFinal) {
    // BFINAL + BTYPE=10 (dynamic Huffman)
    writer.WriteBits(isFinal ? 1u : 0u, 1);
    writer.WriteBits(2, 2); // BTYPE = dynamic

    // Write header
    writer.WriteBits((uint)(header.Hlit - 257), 5);
    writer.WriteBits((uint)(header.Hdist - 1), 5);
    writer.WriteBits((uint)(header.Hclen - 4), 4);

    // Write code-length alphabet (HCLEN entries x 3 bits each)
    for (var i = 0; i < header.Hclen; ++i)
      writer.WriteBits(header.ClTree.Lengths[CodeLengthOrder[i]], 3);

    // Write RLE-encoded code lengths using code-length Huffman codes
    foreach (var (code, extraBits, extraValue) in header.RleCodes) {
      writer.WriteBits(header.ClTree.ReversedCodes[code], header.ClTree.Lengths[code]);
      if (extraBits > 0)
        writer.WriteBits((uint)extraValue, extraBits);
    }

    // Write symbols
    _WriteSymbols(writer, symbols, litLenTree, distTree);

    // Write end-of-block (symbol 256)
    writer.WriteBits(litLenTree.ReversedCodes[256], litLenTree.Lengths[256]);
  }

  /// <summary>Write LZ symbols using Huffman codes</summary>
  private static void _WriteSymbols(BitWriter writer, LzSymbol[] symbols, HuffmanTree litLenTree,
    HuffmanTree distTree) {
    foreach (var sym in symbols)
      if (sym.Distance == 0) {
        // Literal (0-255)
        writer.WriteBits(litLenTree.ReversedCodes[sym.LitLen], litLenTree.Lengths[sym.LitLen]);
      } else {
        // Match: LitLen is actual length (3-258)
        var actualLength = sym.LitLen;
        var lengthCode = GetLengthCode(actualLength);

        // Write length code
        writer.WriteBits(litLenTree.ReversedCodes[lengthCode], litLenTree.Lengths[lengthCode]);

        // Write length extra bits
        var lenExtraBitCount = GetLengthExtraBitCount(lengthCode);
        if (lenExtraBitCount > 0)
          writer.WriteBits((uint)GetLengthExtraBitValue(actualLength, lengthCode), lenExtraBitCount);

        // Write distance code
        var distCode = GetDistanceCode(sym.Distance);
        writer.WriteBits(distTree.ReversedCodes[distCode], distTree.Lengths[distCode]);

        // Write distance extra bits
        var distExtraBitCount = GetDistanceExtraBitCount(distCode);
        if (distExtraBitCount > 0)
          writer.WriteBits((uint)GetDistanceExtraBitValue(sym.Distance, distCode), distExtraBitCount);
      }
  }

  /// <summary>RLE-encode code lengths for dynamic Huffman header</summary>
  private static List<(int code, int extraBits, int extraValue)> _RleEncodeCodeLengths(byte[] codeLengths) {
    var result = new List<(int, int, int)>();
    var i = 0;

    while (i < codeLengths.Length) {
      var len = codeLengths[i];

      if (len == 0) {
        // Count consecutive zeros
        var count = 1;
        while (i + count < codeLengths.Length && codeLengths[i + count] == 0 && count < 138)
          ++count;

        switch (count) {
          case >= 11:
            // Code 18: repeat zero 11-138 times
            result.Add((18, 7, count - 11));
            break;
          case >= 3:
            // Code 17: repeat zero 3-10 times
            result.Add((17, 3, count - 3));
            break;
          default: {
            // Emit individual zeros
            for (var j = 0; j < count; ++j)
              result.Add((0, 0, 0));
            break;
          }
        }

        i += count;
      } else {
        // Emit the length value once
        result.Add((len, 0, 0));
        ++i;

        // Emit consecutive code 16 runs for all remaining repetitions
        while (i < codeLengths.Length && codeLengths[i] == len) {
          var count = 0;
          while (i + count < codeLengths.Length && codeLengths[i + count] == len && count < 6)
            ++count;

          if (count >= 3) {
            // Code 16: repeat previous 3-6 times
            result.Add((16, 2, count - 3));
            i += count;
          } else {
            break;
          }
        }
      }
    }

    return result;
  }

  /// <summary>Wrap raw DEFLATE data in a zlib stream (CMF/FLG + deflate + Adler-32)</summary>
  private static byte[] _WrapZlib(byte[] deflateData, uint adler32) {
    var result = new byte[2 + deflateData.Length + 4];

    // CMF: compression method 8 (deflate), window size 32K (log2(32768)-8 = 7, so 7<<4 = 0x70, + 8 = 0x78)
    result[0] = 0x78;

    // FLG: FCHECK so (CMF*256 + FLG) % 31 == 0, FDICT=0, FLEVEL=2 (default compressor)
    // 0x78 * 256 = 30720; 30720 % 31 = 30; FCHECK = 31 - 30 = 1
    // FLEVEL=2 → bits 6-7 = 10 → 0x80; FLG = 0x80 | FCHECK
    // (0x78 * 256 + 0x81) % 31 = (30720 + 129) % 31 = 30849 % 31 = 30849 - 995*31 = 30849 - 30845 = 4 ≠ 0
    // Let me compute properly: base = 0x80 (FLEVEL=2); need (30720 + 0x80 + check) % 31 == 0
    // 30720 + 128 = 30848; 30848 % 31 = 30848 - 995*31 = 30848 - 30845 = 3; check = 31 - 3 = 28
    // FLG = 0x80 | 28 = 0x9C
    // Verify: (30720 + 0x9C) % 31 = (30720 + 156) % 31 = 30876 % 31 = 30876 - 996*31 = 30876 - 30876 = 0 ✓
    result[1] = 0x9C;

    // DEFLATE data
    Buffer.BlockCopy(deflateData, 0, result, 2, deflateData.Length);

    // Adler-32, big-endian
    var offset = 2 + deflateData.Length;
    result[offset] = (byte)(adler32 >> 24);
    result[offset + 1] = (byte)(adler32 >> 16);
    result[offset + 2] = (byte)(adler32 >> 8);
    result[offset + 3] = (byte)adler32;

    return result;
  }

  /// <summary>Compute Adler-32 checksum</summary>
  private static uint _ComputeAdler32(ReadOnlySpan<byte> data) {
    const uint MOD_ADLER = 65521;
    uint a = 1, b = 0;

    const int CHUNK_SIZE = 5552;
    var remaining = data.Length;
    var offset = 0;

    while (remaining > 0) {
      var chunkSize = Math.Min(remaining, CHUNK_SIZE);
      for (var i = 0; i < chunkSize; ++i) {
        a += data[offset + i];
        b += a;
      }

      a %= MOD_ADLER;
      b %= MOD_ADLER;
      offset += chunkSize;
      remaining -= chunkSize;
    }

    return (b << 16) | a;
  }
}
