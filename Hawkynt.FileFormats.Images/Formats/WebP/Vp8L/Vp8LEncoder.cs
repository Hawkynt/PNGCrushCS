using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.WebP.Vp8L;

/// <summary>Pure C# VP8L (WebP lossless) encoder: SubtractGreen + Huffman + LZ77.</summary>
internal static class Vp8LEncoder {

  /// <summary>Encode ARGB pixels into a VP8L bitstream (including the 5-byte header).</summary>
  /// <param name="argb">ARGB uint array (alpha in bits 24-31, red 16-23, green 8-15, blue 0-7).</param>
  /// <param name="width">Image width.</param>
  /// <param name="height">Image height.</param>
  /// <param name="hasAlpha">Whether to signal alpha in the VP8L header.</param>
  /// <returns>Complete VP8L chunk data starting with the 0x2F signature byte.</returns>
  public static byte[] Encode(uint[] argb, int width, int height, bool hasAlpha) {
    ArgumentNullException.ThrowIfNull(argb);
    if (width <= 0 || height <= 0)
      throw new ArgumentException("Dimensions must be positive.");

    var numPixels = width * height;

    // Apply SubtractGreen transform
    var transformed = new uint[numPixels];
    Array.Copy(argb, transformed, numPixels);
    _ApplySubtractGreen(transformed, numPixels);

    using var ms = new MemoryStream();
    var writer = new Vp8LBitWriter(ms);

    // Write VP8L signature (0x2F) and 4-byte header
    // Byte 0: signature 0x2F
    // Bytes 1-4: 14-bit width-1 | 14-bit height-1 | 1-bit alpha | 3-bit version(0)
    ms.WriteByte(0x2F);
    var w = (uint)(width - 1);
    var h = (uint)(height - 1);
    var headerBits = w | (h << 14) | ((hasAlpha ? 1u : 0u) << 28);
    ms.WriteByte((byte)(headerBits & 0xFF));
    ms.WriteByte((byte)((headerBits >> 8) & 0xFF));
    ms.WriteByte((byte)((headerBits >> 16) & 0xFF));
    ms.WriteByte((byte)((headerBits >> 24) & 0xFF));

    // Write transform flag: 1 bit = 1 (we have transforms)
    writer.WriteBits(1, 1);
    // Transform type: SubtractGreen = 2
    writer.WriteBits(2, 2);

    // No more transforms
    writer.WriteBits(0, 1);

    // Encode the main image
    _EncodeImageData(writer, transformed, width, height);
    writer.Flush();

    return ms.ToArray();
  }

  private static void _ApplySubtractGreen(uint[] pixels, int count) {
    for (var i = 0; i < count; ++i) {
      var argb = pixels[i];
      var green = (argb >> 8) & 0xFF;
      var red = ((argb >> 16) & 0xFF) - green;
      var blue = (argb & 0xFF) - green;
      pixels[i] = (argb & 0xFF00FF00) | (((uint)(red & 0xFF)) << 16) | ((uint)(blue & 0xFF));
    }
  }

  private static void _EncodeImageData(Vp8LBitWriter writer, uint[] pixels, int width, int height) {
    var numPixels = width * height;

    // Colour-cache info precedes the meta-Huffman flag in the VP8L bitstream.
    writer.WriteBits(0, 1); // no colour cache
    writer.WriteBits(0, 1); // no meta-Huffman

    // Build frequency histograms for the 5 channels
    var greenHist = new int[256 + 24]; // green literals + length prefix codes
    var redHist = new int[256];
    var blueHist = new int[256];
    var alphaHist = new int[256];
    var distHist = new int[40];

    // Simple LZ77: find backward references
    var symbols = _ComputeLz77Symbols(pixels, width, height);

    foreach (var sym in symbols) {
      if (sym.IsLiteral) {
        var argb = sym.Pixel;
        ++greenHist[(argb >> 8) & 0xFF];
        ++redHist[(argb >> 16) & 0xFF];
        ++blueHist[argb & 0xFF];
        ++alphaHist[(argb >> 24) & 0xFF];
      } else {
        var (lengthCode, _) = _EncodeLengthOrDistance(sym.Length);
        ++greenHist[256 + lengthCode];
        var (distCode, _) = _EncodeLengthOrDistance(_DistanceToPlaneCode(sym.Distance));
        ++distHist[distCode];
      }
    }

    // Build Huffman codes for each channel
    var greenCodes = _BuildHuffmanCodes(greenHist);
    var redCodes = _BuildHuffmanCodes(redHist);
    var blueCodes = _BuildHuffmanCodes(blueHist);
    var alphaCodes = _BuildHuffmanCodes(alphaHist);
    var distCodes = _BuildHuffmanCodes(distHist);

    // Write 5 Huffman trees
    _WriteHuffmanTree(writer, greenCodes, greenHist.Length, greenHist);
    _WriteHuffmanTree(writer, redCodes, 256, redHist);
    _WriteHuffmanTree(writer, blueCodes, 256, blueHist);
    _WriteHuffmanTree(writer, alphaCodes, 256, alphaHist);
    _WriteHuffmanTree(writer, distCodes, 40, distHist);

    // Encode symbols
    foreach (var sym in symbols) {
      if (sym.IsLiteral) {
        var argb = sym.Pixel;
        var g = (int)((argb >> 8) & 0xFF);
        var r = (int)((argb >> 16) & 0xFF);
        var b = (int)(argb & 0xFF);
        var a = (int)((argb >> 24) & 0xFF);
        writer.WriteBits(greenCodes[g].Code, greenCodes[g].Length);
        writer.WriteBits(redCodes[r].Code, redCodes[r].Length);
        writer.WriteBits(blueCodes[b].Code, blueCodes[b].Length);
        writer.WriteBits(alphaCodes[a].Code, alphaCodes[a].Length);
      } else {
        var (lengthCode, lengthExtra) = _EncodeLengthOrDistance(sym.Length);
        writer.WriteBits(greenCodes[256 + lengthCode].Code, greenCodes[256 + lengthCode].Length);
        if (lengthExtra.Bits > 0)
          writer.WriteBits(lengthExtra.Value, lengthExtra.Bits);

        var (distCode, distExtra) = _EncodeLengthOrDistance(_DistanceToPlaneCode(sym.Distance));
        writer.WriteBits(distCodes[distCode].Code, distCodes[distCode].Length);
        if (distExtra.Bits > 0)
          writer.WriteBits(distExtra.Value, distExtra.Bits);
      }
    }
  }

  private readonly struct Lz77Symbol {
    public readonly bool IsLiteral;
    public readonly uint Pixel;
    public readonly int Length;
    public readonly int Distance;

    public static Lz77Symbol Literal(uint pixel) => new(true, pixel, 0, 0);
    public static Lz77Symbol BackRef(int length, int distance) => new(false, 0, length, distance);

    private Lz77Symbol(bool isLiteral, uint pixel, int length, int distance) {
      IsLiteral = isLiteral;
      Pixel = pixel;
      Length = length;
      Distance = distance;
    }
  }

  private static List<Lz77Symbol> _ComputeLz77Symbols(uint[] pixels, int width, int height) {
    var numPixels = width * height;
    var result = new List<Lz77Symbol>(numPixels);

    // Simple hash chain for LZ77
    const int hashBits = 16;
    const int hashSize = 1 << hashBits;
    var hashHead = new int[hashSize];
    var hashChain = new int[numPixels];
    Array.Fill(hashHead, -1);

    for (var pos = 0; pos < numPixels;) {
      var bestLen = 0;
      var bestDist = 0;

      var hash = _Hash(pixels, pos, numPixels) & (hashSize - 1);
      var chainPos = hashHead[hash];
      var maxChainDepth = 32;

      while (chainPos >= 0 && maxChainDepth-- > 0) {
        var dist = pos - chainPos;
        if (dist > 1 << 20)
          break;

        var len = 0;
        var maxLen = Math.Min(4096, numPixels - pos);
        while (len < maxLen && pixels[chainPos + len] == pixels[pos + len])
          ++len;

        if (len > bestLen) {
          bestLen = len;
          bestDist = dist;
        }

        chainPos = hashChain[chainPos];
      }

      if (bestLen >= 3) {
        result.Add(Lz77Symbol.BackRef(bestLen, bestDist));
        for (var i = 0; i < bestLen; ++i) {
          if (pos + i < numPixels) {
            var h = _Hash(pixels, pos + i, numPixels) & (hashSize - 1);
            hashChain[pos + i] = hashHead[h];
            hashHead[h] = pos + i;
          }
        }
        pos += bestLen;
      } else {
        result.Add(Lz77Symbol.Literal(pixels[pos]));
        hashChain[pos] = hashHead[hash];
        hashHead[hash] = pos;
        ++pos;
      }
    }

    return result;
  }

  private static uint _Hash(uint[] pixels, int pos, int numPixels) {
    var p = pixels[pos];
    var p1 = pos + 1 < numPixels ? pixels[pos + 1] : 0u;
    return (p * 0x1E35A7BD + p1 * 0x85EBCA6B) >> 16;
  }

  /// <summary>Exact inverse of the decoder's prefix decoding: a value <c>v</c> (1-based) becomes a
  /// prefix code plus extra bits such that <c>offset + extra + 1 == v</c>, where
  /// <c>offset = (2 + (prefix &amp; 1)) &lt;&lt; ((prefix - 2) >> 1)</c>.</summary>
  private static (int PrefixCode, ExtraBits Extra) _EncodeLengthOrDistance(int value) {
    if (value <= 0)
      return (0, default);

    var v = value - 1; // 0-based, matching the decoder's trailing +1
    if (v < 4)
      return (v, default);

    // v lies in [2^(e+1), 2^(e+2)); the bit below the highest selects between the two
    // prefix codes that share e.
    var highestBit = 0;
    for (var t = v; t > 1; t >>= 1)
      ++highestBit;

    var extraBits = highestBit - 1;
    var second = (v >> extraBits) & 1;
    var prefix = 2 * extraBits + 2 + second;
    var offset = (2 + (prefix & 1)) << extraBits;

    return (prefix, new ExtraBits(extraBits, (uint)(v - offset)));
  }

  /// <summary>Converts a linear pixel distance to the plane code the decoder expects. Codes 1..120
  /// are the 2D locality map; anything else is sent as <c>distance + 120</c>.</summary>
  private static int _DistanceToPlaneCode(int distance) => distance + _DISTANCE_MAP_SIZE;

  /// <summary>Number of entries in the decoder's 2D distance map.</summary>
  private const int _DISTANCE_MAP_SIZE = 120;

  /// <summary>Forces <paramref name="lengths"/> to be a complete prefix code no deeper than
  /// <paramref name="maxLength"/>. Huffman construction can exceed the depth limit, and simply
  /// truncating leaves the Kraft sum off 1 — which decoders reject as a malformed code.</summary>
  private static void _LimitCodeLengths(int[] lengths, int maxLength) {
    var symbols = new List<int>();
    for (var i = 0; i < lengths.Length; ++i)
      if (lengths[i] > 0)
        symbols.Add(i);

    if (symbols.Count <= 1)
      return;

    foreach (var s in symbols)
      if (lengths[s] > maxLength)
        lengths[s] = maxLength;

    // Kraft sum in units of 2^-maxLength, so it stays integral.
    var one = 1L << maxLength;
    long total = 0;
    foreach (var s in symbols)
      total += one >> lengths[s];

    // Over-subscribed: push the deepest symbols deeper still isn't possible, so lengthen
    // the shallowest ones until the code fits.
    while (total > one) {
      var victim = -1;
      foreach (var s in symbols)
        if (lengths[s] < maxLength && (victim < 0 || lengths[s] < lengths[victim]))
          victim = s;

      if (victim < 0)
        break;

      total -= one >> lengths[victim];
      ++lengths[victim];
      total += one >> lengths[victim];
    }

    // Under-subscribed: shorten the deepest symbols until the code is complete.
    while (true) {
      var victim = -1;
      foreach (var s in symbols)
        if (lengths[s] > 1 && (victim < 0 || lengths[s] > lengths[victim]))
          victim = s;

      if (victim < 0)
        break;

      var gain = (one >> (lengths[victim] - 1)) - (one >> lengths[victim]);
      if (total + gain > one)
        break;

      total += gain;
      --lengths[victim];
    }
  }

  private readonly struct ExtraBits {
    public readonly int Bits;
    public readonly uint Value;
    public ExtraBits(int bits, uint value) {
      Bits = bits;
      Value = value;
    }
  }

  private readonly struct HuffmanCode {
    public readonly uint Code;
    public readonly int Length;
    public HuffmanCode(uint code, int length) {
      Code = code;
      Length = length;
    }
  }

  private static HuffmanCode[] _BuildHuffmanCodes(int[] histogram) {
    var n = histogram.Length;
    var codes = new HuffmanCode[n];

    // Count non-zero entries
    var nonZeroCount = 0;
    var lastNonZero = -1;
    for (var i = 0; i < n; ++i)
      if (histogram[i] > 0) {
        ++nonZeroCount;
        lastNonZero = i;
      }

    // A tree with a single reachable symbol carries no information, so decoders resolve it without
    // consuming any bits. Emitting a 1-bit code here would desynchronize everything that follows.
    if (nonZeroCount == 0) {
      codes[0] = new HuffmanCode(0, 0);
      return codes;
    }

    if (nonZeroCount == 1) {
      codes[lastNonZero] = new HuffmanCode(0, 0);
      return codes;
    }

    // Build code lengths using simple length-limited Huffman
    var lengths = _ComputeHuffmanLengths(histogram, n, 15);
    _LimitCodeLengths(lengths, 15);

    // Assign codes from lengths (canonical Huffman)
    var blCount = new int[16];
    for (var i = 0; i < n; ++i)
      if (lengths[i] > 0)
        ++blCount[lengths[i]];

    var nextCode = new uint[16];
    uint code = 0;
    for (var bits = 1; bits <= 15; ++bits) {
      code = (code + (uint)blCount[bits - 1]) << 1;
      nextCode[bits] = code;
    }

    for (var i = 0; i < n; ++i)
      if (lengths[i] > 0) {
        codes[i] = new HuffmanCode(_ReverseBits(nextCode[lengths[i]], lengths[i]), lengths[i]);
        ++nextCode[lengths[i]];
      }

    return codes;
  }

  private static int[] _ComputeHuffmanLengths(int[] histogram, int n, int maxLength) {
    // Package-Merge algorithm simplified: use greedy approach
    var lengths = new int[n];
    var symbols = new List<(int Index, int Freq)>();
    for (var i = 0; i < n; ++i)
      if (histogram[i] > 0)
        symbols.Add((i, histogram[i]));

    if (symbols.Count <= 1) {
      if (symbols.Count == 1)
        lengths[symbols[0].Index] = 1;
      return lengths;
    }

    // Sort by frequency ascending
    symbols.Sort((a, b) => a.Freq.CompareTo(b.Freq));

    // Build a min-heap Huffman tree
    var queue = new PriorityQueue<int, long>();
    var tree = new (int Left, int Right)[symbols.Count * 2];
    var nodeCount = symbols.Count;

    for (var i = 0; i < symbols.Count; ++i)
      queue.Enqueue(i, symbols[i].Freq);

    while (queue.Count > 1) {
      queue.TryDequeue(out var left, out var leftFreq);
      queue.TryDequeue(out var right, out var rightFreq);
      var parent = nodeCount++;
      if (parent >= tree.Length)
        Array.Resize(ref tree, tree.Length * 2);
      tree[parent] = (left, right);
      queue.Enqueue(parent, leftFreq + rightFreq);
    }

    queue.TryDequeue(out var root, out _);

    // Compute depths
    var depths = new int[nodeCount];
    _ComputeDepths(tree, depths, root, 0, symbols.Count);

    // Assign depths to original symbols, clamping to maxLength
    for (var i = 0; i < symbols.Count; ++i) {
      var depth = Math.Min(depths[i], maxLength);
      lengths[symbols[i].Index] = Math.Max(depth, 1);
    }

    return lengths;
  }

  private static void _ComputeDepths(
    (int Left, int Right)[] tree,
    int[] depths,
    int node,
    int depth,
    int leafCount
  ) {
    if (node < leafCount) {
      depths[node] = depth;
      return;
    }

    _ComputeDepths(tree, depths, tree[node].Left, depth + 1, leafCount);
    _ComputeDepths(tree, depths, tree[node].Right, depth + 1, leafCount);
  }

  private static uint _ReverseBits(uint value, int numBits) {
    var result = 0u;
    for (var i = 0; i < numBits; ++i) {
      result = (result << 1) | (value & 1);
      value >>= 1;
    }
    return result;
  }

  private static void _WriteHuffmanTree(Vp8LBitWriter writer, HuffmanCode[] codes, int alphabetSize, int[] histogram) {
    // Which symbols occur is read from the histogram, not from the code lengths: a single-symbol
    // tree deliberately carries length 0, and counting lengths would miss it entirely.
    var nonZero = 0;
    var lastSymbol = 0;
    var secondLastSymbol = 0;
    for (var i = 0; i < alphabetSize; ++i)
      if (histogram[i] > 0) {
        ++nonZero;
        secondLastSymbol = lastSymbol;
        lastSymbol = i;
      }

    // The simple code encodes symbols in at most 8 bits, so it can't express the green
    // alphabet's length-prefix symbols (>= 256). Those fall through to the normal path.
    var s0 = nonZero == 2 ? Math.Min(secondLastSymbol, lastSymbol) : lastSymbol;
    var s1 = nonZero == 2 ? Math.Max(secondLastSymbol, lastSymbol) : 0;
    if (nonZero <= 2 && s0 <= 0xFF && s1 <= 0xFF) {
      // Simple code layout: num_symbols-1 (1 bit), is_first_8bits (1 bit),
      // symbol0 (1 or 8 bits), then symbol1 (always 8 bits) when there are two.
      writer.WriteBits(1, 1); // simple

      if (nonZero == 0) {
        writer.WriteBits(0, 1); // num_symbols - 1
        writer.WriteBits(0, 1); // is_first_8bits = 0
        writer.WriteBits(0, 1); // symbol0 in 1 bit
        return;
      }

      var isFirst8Bits = s0 > 1 ? 1u : 0u;
      writer.WriteBits((uint)(nonZero - 1), 1);
      writer.WriteBits(isFirst8Bits, 1);
      writer.WriteBits((uint)s0, isFirst8Bits == 1 ? 8 : 1);
      if (nonZero == 2)
        writer.WriteBits((uint)s1, 8);

      return;
    }

    // Normal Huffman code: write code lengths using code length codes
    writer.WriteBits(0, 1); // not simple

    // Collect code lengths
    var codeLengths = new int[alphabetSize];
    var maxCodeLen = 0;
    for (var i = 0; i < alphabetSize; ++i) {
      codeLengths[i] = codes[i].Length;
      if (codeLengths[i] > maxCodeLen)
        maxCodeLen = codeLengths[i];
    }

    // Code length alphabet order: 17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15
    var clOrder = new[] { 17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

    // Build code length histogram
    var clHist = new int[19];
    for (var i = 0; i < alphabetSize; ++i)
      ++clHist[codeLengths[i]];

    // The code-length alphabet needs a real Huffman code: a uniform 3 bits per used symbol is only
    // a complete code when exactly 8 symbols are used, and decoders reject incomplete codes.
    var clCodeLengths = _ComputeHuffmanLengths(clHist, 19, 7);
    _LimitCodeLengths(clCodeLengths, 7);

    // Find num_code_lengths: how many of the 19 positions we need to write
    var numCodeLengths = 4;
    for (var i = 18; i >= 4; --i)
      if (clCodeLengths[clOrder[i]] != 0) {
        numCodeLengths = i + 1;
        break;
      }

    // Write num_code_lengths - 4
    writer.WriteBits((uint)(numCodeLengths - 4), 4);

    // Write code length code lengths (3 bits each)
    for (var i = 0; i < numCodeLengths; ++i)
      writer.WriteBits((uint)clCodeLengths[clOrder[i]], 3);

    // use_length = 0: read code lengths for the whole alphabet rather than an explicit
    // max_symbol cut-off. Omitting this flag desynchronizes every following bit.
    writer.WriteBits(0, 1);

    // Build canonical codes for code length alphabet
    var clCodes = new uint[19];
    var clBlCount = new int[8];
    for (var i = 0; i < 19; ++i)
      if (clCodeLengths[i] > 0)
        ++clBlCount[clCodeLengths[i]];

    var clNext = new uint[8];
    uint c = 0;
    for (var bits = 1; bits <= 7; ++bits) {
      c = (c + (uint)clBlCount[bits - 1]) << 1;
      clNext[bits] = c;
    }

    for (var i = 0; i < 19; ++i)
      if (clCodeLengths[i] > 0) {
        clCodes[i] = _ReverseBits(clNext[clCodeLengths[i]], clCodeLengths[i]);
        ++clNext[clCodeLengths[i]];
      }

    // Write code lengths using the code length codes
    for (var i = 0; i < alphabetSize; ++i) {
      var cl = codeLengths[i];
      if (clCodeLengths[cl] > 0)
        writer.WriteBits(clCodes[cl], clCodeLengths[cl]);
    }
  }
}

/// <summary>LSB-first bit writer for VP8L encoding.</summary>
internal sealed class Vp8LBitWriter {
  private readonly Stream _stream;
  private ulong _buffer;
  private int _bitsInBuffer;

  public Vp8LBitWriter(Stream stream) => _stream = stream;

  public void WriteBits(uint value, int numBits) {
    _buffer |= (ulong)(value & ((1u << numBits) - 1)) << _bitsInBuffer;
    _bitsInBuffer += numBits;
    while (_bitsInBuffer >= 8) {
      _stream.WriteByte((byte)(_buffer & 0xFF));
      _buffer >>= 8;
      _bitsInBuffer -= 8;
    }
  }

  public void Flush() {
    while (_bitsInBuffer > 0) {
      _stream.WriteByte((byte)(_buffer & 0xFF));
      _buffer >>= 8;
      _bitsInBuffer -= 8;
    }
    _bitsInBuffer = 0;
    _buffer = 0;
  }
}
