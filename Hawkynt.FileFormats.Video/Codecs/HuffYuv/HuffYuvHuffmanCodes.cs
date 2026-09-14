using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.HuffYuv;

/// <summary>Encoder-side HuffYUV/FFVHUFF Huffman codes.</summary>
/// <remarks>
/// The alphabet is 256 symbols for eight-bit HuffYUV and grows to at most 16 384 symbols for
/// high-depth FFVHUFF. Code lengths are derived from statistics and the actual codes are assigned
/// longest length first, matching the decoder and FFmpeg's <c>ff_huffyuv_generate_bits_table</c>.
/// </remarks>
internal sealed class HuffYuvHuffmanCodes {

  private const int _MAX_LENGTH = 31;

  private readonly byte[] _lengths;
  private readonly uint[] _codes;

  private HuffYuvHuffmanCodes(byte[] lengths, uint[] codes) {
    this._lengths = lengths;
    this._codes = codes;
  }

  internal int SymbolCount => this._lengths.Length;
  internal void Write(HuffYuvBitWriter bits, int symbol) => bits.Write(this._codes[symbol], this._lengths[symbol]);
  internal int LengthOf(int symbol) => this._lengths[symbol];

  internal static HuffYuvHuffmanCodes FromStatistics(ReadOnlySpan<ulong> statistics) {
    if (statistics.IsEmpty || statistics.Length > HuffYuvHuffmanTable.MAX_SYMBOL_COUNT)
      throw new ArgumentOutOfRangeException(nameof(statistics));
    var lengths = _LengthsOf(statistics);
    return new(lengths, _CodesOf(lengths));
  }

  internal void Store(List<byte> into) {
    for (var i = 0; i < this._lengths.Length;) {
      var length = this._lengths[i];
      var repeat = 0;
      for (; i < this._lengths.Length && this._lengths[i] == length && repeat < 255; ++i)
        ++repeat;
      if (repeat > 7) {
        into.Add(length);
        into.Add((byte)repeat);
      } else
        into.Add((byte)(length | (repeat << 5)));
    }
  }

  private static byte[] _LengthsOf(ReadOnlySpan<ulong> statistics) {
    var symbolCount = statistics.Length;
    var lengths = new byte[symbolCount];
    var heap = new (ulong Value, int Name)[symbolCount];
    var parent = new int[2 * symbolCount];
    var depth = new byte[2 * symbolCount];

    for (ulong offset = 1; ; offset <<= 1) {
      for (var i = 0; i < symbolCount; ++i)
        heap[i] = ((statistics[i] << 14) + offset, i);
      for (var i = symbolCount / 2 - 1; i >= 0; --i)
        _Sift(heap, i, symbolCount);

      for (var next = symbolCount; next < 2 * symbolCount - 1; ++next) {
        var smallest = heap[0].Value;
        parent[heap[0].Name] = next;
        heap[0].Value = ulong.MaxValue;
        _Sift(heap, 0, symbolCount);
        parent[heap[0].Name] = next;
        heap[0].Name = next;
        heap[0].Value += smallest;
        _Sift(heap, 0, symbolCount);
      }

      depth[2 * symbolCount - 2] = 0;
      for (var i = 2 * symbolCount - 3; i >= symbolCount; --i)
        depth[i] = (byte)(depth[parent[i]] + 1);

      var fits = true;
      for (var i = 0; i < symbolCount; ++i) {
        var length = depth[parent[i]] + 1;
        if (length > _MAX_LENGTH) {
          fits = false;
          break;
        }
        lengths[i] = (byte)length;
      }
      if (fits)
        return lengths;
      if (offset > (1UL << 48))
        throw new InvalidDataException("The HuffYUV symbol distribution cannot be represented by codes of at most 31 bits.");
    }
  }

  private static void _Sift(Span<(ulong Value, int Name)> heap, int root, int size) {
    while (root * 2 + 1 < size) {
      var child = root * 2 + 1;
      if (child < size - 1 && heap[child].Value > heap[child + 1].Value)
        ++child;
      if (heap[root].Value <= heap[child].Value)
        return;
      (heap[root], heap[child]) = (heap[child], heap[root]);
      root = child;
    }
  }

  private static uint[] _CodesOf(ReadOnlySpan<byte> lengths) {
    Span<int> count = stackalloc int[_MAX_LENGTH + 2];
    Span<uint> first = stackalloc uint[_MAX_LENGTH + 2];
    foreach (var length in lengths)
      ++count[length];

    for (var length = _MAX_LENGTH + 1; length > 0; --length) {
      var taken = (uint)count[length] + first[length];
      if ((taken & 1) != 0)
        throw new InvalidDataException($"The lengths leave a code of {length - 1} bits half assigned, which is not a complete code.");
      first[length - 1] = taken >> 1;
    }

    var codes = new uint[lengths.Length];
    for (var i = 0; i < lengths.Length; ++i)
      codes[i] = first[lengths[i]]++;
    return codes;
  }
}
