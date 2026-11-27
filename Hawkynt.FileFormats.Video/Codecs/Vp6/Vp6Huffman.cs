using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.Vp6;

/// <summary>A most-significant-bit-first reader over VP6's Huffman coefficient partition.</summary>
/// <remarks>Reads past the end return zeros, as FFmpeg's padded <c>get_bits</c> does.</remarks>
internal struct Vp6BitReader(ReadOnlyMemory<byte> data) {
  private readonly ReadOnlyMemory<byte> _data = data;
  private long _position;

  public readonly long BitsLeft => (long)this._data.Length * 8 - this._position;

  public int ReadBits(int count) {
    var span = this._data.Span;
    var value = 0;
    for (var i = 0; i < count; ++i) {
      var at = this._position >> 3;
      var bit = at < span.Length ? (span[(int)at] >> (7 - (int)(this._position & 7))) & 1 : 0;
      value = (value << 1) | bit;
      ++this._position;
    }
    return value;
  }
}

/// <summary>
/// A VP6 Huffman table, built from a frame's coefficient probabilities exactly as FFmpeg builds it.
/// </summary>
/// <remarks>
/// Converted from FFmpeg's <c>vp6_build_huff_tree</c> (<c>libavcodec/vp6.c</c>) and
/// <c>ff_huff_build_tree</c> (<c>libavcodec/huffman.c</c>), LGPL-2.1-or-later. The tree is not a
/// textbook Huffman tree: leaf weights come from walking the frame's binary probability tree down from
/// 256, the leaves are sorted by weight and then by descending symbol, a merged node is placed ahead of
/// existing nodes of equal weight (<c>FF_HUFFMAN_FLAG_HNODE_FIRST</c>), and codes are handed out in
/// the depth-first order the leaves are met, as <c>ff_vlc_init_from_lengths</c> does. Every one of those
/// choices decides which bit pattern means which symbol, so each is kept.
/// </remarks>
internal sealed class Vp6Huffman {

  private const int _HNODE = -1;

  private readonly Dictionary<(int Length, int Code), int> _codes = new();
  private readonly int _maxLength;

  private Vp6Huffman(Dictionary<(int, int), int> codes, int maxLength) {
    this._codes = codes;
    this._maxLength = maxLength;
  }

  private struct Node {
    public int Symbol;
    public int Zero;
    public uint Count;
  }

  internal static Vp6Huffman Build(ReadOnlySpan<byte> probabilities, ReadOnlySpan<byte> map, int size) {
    var nodes = new Node[2 * 12];

    // Leaf and internal weights from the probability tree; internal ones sit after the leaves.
    nodes[size].Count = 256;
    for (var i = 0; i < size - 1; ++i) {
      var weight = nodes[size + i].Count;
      var a = (uint)(weight * probabilities[i] >> 8);
      var b = (uint)(weight * (255 - probabilities[i]) >> 8);
      nodes[map[2 * i]].Count = a + (a == 0 ? 1u : 0u);
      nodes[map[2 * i + 1]].Count = b + (b == 0 ? 1u : 0u);
    }

    for (var i = 0; i < size; ++i) {
      nodes[i].Symbol = i;
      nodes[i].Zero = -2;
    }

    // Weight ascending, then symbol descending: a total order, so any sort gives FFmpeg's result.
    Array.Sort(nodes, 0, size, Comparer<Node>.Create(static (a, b) => {
      var byCount = a.Count.CompareTo(b.Count);
      return byCount != 0 ? byCount : b.Symbol.CompareTo(a.Symbol);
    }));

    var current = size;
    nodes[size * 2 - 1].Count = 0;
    for (var i = 0; i < size * 2 - 1; i += 2) {
      var count = nodes[i].Count + nodes[i + 1].Count;
      int j;
      for (j = current; j > i + 2; --j) {
        if (count > nodes[j - 1].Count)
          break;
        nodes[j] = nodes[j - 1];
      }

      nodes[j] = new() { Symbol = _HNODE, Count = count, Zero = i };
      ++current;
    }

    var lengths = new List<(int Length, int Symbol)>();
    _Collect(nodes, size * 2 - 2, 0, lengths);

    var codes = new Dictionary<(int, int), int>();
    var code = 0u;
    var maxLength = 0;
    foreach (var (length, symbol) in lengths) {
      if (length == 0 || length > 31)
        throw new InvalidDataException($"A VP6 Huffman tree produced a code of length {length}.");
      codes[(length, (int)(code >> (32 - length)))] = symbol;
      code += 1u << (32 - length);
      maxLength = Math.Max(maxLength, length);
    }

    return new(codes, maxLength);
  }

  private static void _Collect(Node[] nodes, int node, int depth, List<(int, int)> output) {
    if (nodes[node].Symbol != _HNODE || nodes[node].Count == 0) {
      output.Add((depth, nodes[node].Symbol));
      return;
    }

    _Collect(nodes, nodes[node].Zero, depth + 1, output);
    _Collect(nodes, nodes[node].Zero + 1, depth + 1, output);
  }

  internal int Read(ref Vp6BitReader bits) {
    var code = 0;
    for (var length = 1; length <= this._maxLength; ++length) {
      code = (code << 1) | bits.ReadBits(1);
      if (this._codes.TryGetValue((length, code), out var symbol))
        return symbol;
    }
    throw new InvalidDataException("A VP6 Huffman code names no symbol.");
  }
}
