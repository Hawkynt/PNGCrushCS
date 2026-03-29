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

    // No meta-Huffman
    writer.WriteBits(0, 1);

    // No color cache
    writer.WriteBits(0, 1);

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
        var (distCode, _) = _EncodeLengthOrDistance(sym.Distance);
        ++distHist[Math.Min(distCode, 39)];
      }
    }

    // Build Huffman codes for each channel
    var greenCodes = _BuildHuffmanCodes(greenHist);
    var redCodes = _BuildHuffmanCodes(redHist);
    var blueCodes = _BuildHuffmanCodes(blueHist);
    var alphaCodes = _BuildHuffmanCodes(alphaHist);
    var distCodes = _BuildHuffmanCodes(distHist);

    // Write 5 Huffman trees
    _WriteHuffmanTree(writer, greenCodes, greenHist.Length);
    _WriteHuffmanTree(writer, redCodes, 256);
    _WriteHuffmanTree(writer, blueCodes, 256);
    _WriteHuffmanTree(writer, alphaCodes, 256);
    _WriteHuffmanTree(writer, distCodes, 40);

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

        var (distCode, distExtra) = _EncodeLengthOrDistance(sym.Distance);
        var dc = Math.Min(distCode, 39);
        writer.WriteBits(distCodes[dc].Code, distCodes[dc].Length);
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

  private static (int PrefixCode, ExtraBits Extra) _EncodeLengthOrDistance(int value) {
    if (value <= 0)
      return (0, default);
    --value; // convert to 0-based

    if (value < 4)
      return (value, default);

    var extraBits = 0;
    var v = value - 2;
    while (v >= (2 << extraBits))
      ++extraBits;

    var prefix = 2 + extraBits * 2 + ((value - 2) >> extraBits >= (1 << extraBits) + (1 << extraBits) / 2 ? 1 : 0);

    // Recalculate properly using the VP8L prefix code formula
    extraBits = (prefix - 2) >> 1;
    var offset = (2 + (prefix & 1)) << extraBits;
    var extraValue = value - offset;

    return (prefix, new ExtraBits(extraBits, (uint)extraValue));
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

    if (nonZeroCount == 0) {
      // All zero: assign code 0 length 1 to symbol 0
      codes[0] = new HuffmanCode(0, 1);
      return codes;
    }

    if (nonZeroCount == 1) {
      codes[lastNonZero] = new HuffmanCode(0, 1);
      return codes;
    }

    // Build code lengths using simple length-limited Huffman
    var lengths = _ComputeHuffmanLengths(histogram, n, 15);

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

  private static void _WriteHuffmanTree(Vp8LBitWriter writer, HuffmanCode[] codes, int alphabetSize) {
    // Determine how many symbols are non-zero
    var nonZero = 0;
    var lastSymbol = 0;
    var secondLastSymbol = 0;
    for (var i = 0; i < alphabetSize; ++i)
      if (codes[i].Length > 0) {
        ++nonZero;
        secondLastSymbol = lastSymbol;
        lastSymbol = i;
      }

    if (nonZero <= 2) {
      // Use simple code length code (type 1)
      writer.WriteBits(1, 1); // simple
      if (nonZero == 0) {
        writer.WriteBits(0, 1); // num_symbols - 1 = 0
        var symbolBits = alphabetSize <= 2 ? 1 : alphabetSize <= 4 ? 2 : alphabetSize <= 16 ? 4 : 8;
        writer.WriteBits(0, symbolBits);
      } else if (nonZero == 1) {
        writer.WriteBits(0, 1); // num_symbols - 1 = 0
        var symbolBits = alphabetSize <= 2 ? 1 : alphabetSize <= 4 ? 2 : alphabetSize <= 16 ? 4 : 8;
        writer.WriteBits((uint)lastSymbol, symbolBits);
      } else {
        writer.WriteBits(1, 1); // num_symbols - 1 = 1
        var symbolBits = alphabetSize <= 2 ? 1 : alphabetSize <= 4 ? 2 : alphabetSize <= 16 ? 4 : 8;
        // First symbol must be smaller
        var s0 = Math.Min(secondLastSymbol, lastSymbol);
        var s1 = Math.Max(secondLastSymbol, lastSymbol);
        writer.WriteBits((uint)s0, 1); // is_first_8bits: 0 if s0 < 2 TODO fix
        writer.WriteBits((uint)s0, symbolBits);
        writer.WriteBits((uint)s1, symbolBits);
      }
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

    // Build code length code lengths (recursive: length of the code that encodes the code lengths)
    var clCodeLengths = new int[19];
    // Assign simple fixed code lengths for the code length alphabet
    for (var i = 0; i < 19; ++i)
      clCodeLengths[i] = clHist[i] > 0 ? 3 : 0;

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
