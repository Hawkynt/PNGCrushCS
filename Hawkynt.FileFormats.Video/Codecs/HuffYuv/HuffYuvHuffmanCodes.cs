using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.HuffYuv;

/// <summary>
/// One of a HuffYUV encoder's Huffman tables: the code and length of each of the 256 symbols, and
/// the run-length coded lengths a stream description carries.
/// </summary>
/// <remarks>
/// The encoding side of <see cref="HuffYuvHuffmanTable"/>. The lengths come from symbol counts by
/// the reference encoder's construction, which is an ordinary Huffman merge with one addition: a
/// small term is added to every count so that no symbol is ever left without a code, and the merge
/// is repeated with that term doubled until the longest code fits in the thirty-one bits a table
/// can state. A picture whose residuals are all one value would otherwise give the other 255
/// symbols codes hundreds of bits long.
/// <para/>
/// The codes are then handed out from the longest length down, as the format has it and as the
/// decoder's table expects — see <see cref="HuffYuvHuffmanTable"/> for why that is not the
/// canonical assignment.
/// </remarks>
internal sealed class HuffYuvHuffmanCodes {

  private const int _SYMBOL_COUNT = HuffYuvHuffmanTable.SYMBOL_COUNT;
  private const int _MAX_LENGTH = 31;

  private readonly byte[] _lengths;
  private readonly uint[] _codes;

  private HuffYuvHuffmanCodes(byte[] lengths, uint[] codes) {
    this._lengths = lengths;
    this._codes = codes;
  }

  /// <summary>Writes one symbol's code.</summary>
  internal void Write(HuffYuvBitWriter bits, int symbol) => bits.Write(this._codes[symbol], this._lengths[symbol]);

  /// <summary>How many bits the code of a symbol is, for sizing a frame before writing it.</summary>
  internal int LengthOf(int symbol) => this._lengths[symbol];

  /// <summary>Builds a table from how often each symbol occurs.</summary>
  internal static HuffYuvHuffmanCodes FromStatistics(ReadOnlySpan<ulong> statistics) {
    if (statistics.Length != _SYMBOL_COUNT)
      throw new ArgumentException($"A table has {_SYMBOL_COUNT} symbols, not {statistics.Length}.", nameof(statistics));

    var lengths = _LengthsOf(statistics);
    return new(lengths, _CodesOf(lengths));
  }

  /// <summary>
  /// The lengths as a description carries them: a length in the low five bits of a byte, the run
  /// of symbols sharing it in the top three, and a whole byte for the run where three bits will
  /// not hold it.
  /// </summary>
  internal void Store(List<byte> into) {
    for (var i = 0; i < _SYMBOL_COUNT;) {
      var length = this._lengths[i];
      var repeat = 0;
      for (; i < _SYMBOL_COUNT && this._lengths[i] == length && repeat < 255; ++i)
        ++repeat;

      if (repeat > 7) {
        into.Add(length);
        into.Add((byte)repeat);
      } else
        into.Add((byte)(length | (repeat << 5)));
    }
  }

  // ============================================================================================
  // Lengths from counts
  // ============================================================================================

  private static byte[] _LengthsOf(ReadOnlySpan<ulong> statistics) {
    var lengths = new byte[_SYMBOL_COUNT];
    var heap = new (ulong Value, int Name)[_SYMBOL_COUNT];
    var parent = new int[2 * _SYMBOL_COUNT];
    var depth = new byte[2 * _SYMBOL_COUNT];

    for (ulong offset = 1; ; offset <<= 1) {
      for (var i = 0; i < _SYMBOL_COUNT; ++i)
        heap[i] = ((statistics[i] << 14) + offset, i);

      for (var i = _SYMBOL_COUNT / 2 - 1; i >= 0; --i)
        _Sift(heap, i, _SYMBOL_COUNT);

      // Every merge takes the two smallest, parks a sentinel where the first was, and puts their
      // sum back where the second was. The sentinels sink and stay sunk, so the heap keeps its size
      // while the live entries dwindle to one.
      for (var next = _SYMBOL_COUNT; next < 2 * _SYMBOL_COUNT - 1; ++next) {
        var smallest = heap[0].Value;
        parent[heap[0].Name] = next;
        heap[0].Value = ulong.MaxValue;
        _Sift(heap, 0, _SYMBOL_COUNT);
        parent[heap[0].Name] = next;
        heap[0].Name = next;
        heap[0].Value += smallest;
        _Sift(heap, 0, _SYMBOL_COUNT);
      }

      depth[2 * _SYMBOL_COUNT - 2] = 0;
      for (var i = 2 * _SYMBOL_COUNT - 3; i >= _SYMBOL_COUNT; --i)
        depth[i] = (byte)(depth[parent[i]] + 1);

      var fits = true;
      for (var i = 0; i < _SYMBOL_COUNT; ++i) {
        var length = depth[parent[i]] + 1;
        if (length > _MAX_LENGTH) {
          fits = false;
          break;
        }

        lengths[i] = (byte)length;
      }

      if (fits)
        return lengths;
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

  // ============================================================================================
  // Codes from lengths
  // ============================================================================================

  /// <summary>Longest first, with the running number halved at every step down.</summary>
  private static uint[] _CodesOf(ReadOnlySpan<byte> lengths) {
    Span<int> count = stackalloc int[_MAX_LENGTH + 2];
    Span<uint> first = stackalloc uint[_MAX_LENGTH + 2];
    foreach (var length in lengths)
      ++count[length];

    first[_MAX_LENGTH + 1] = 0;
    for (var length = _MAX_LENGTH + 1; length > 0; --length) {
      var taken = (uint)count[length] + first[length];
      if ((taken & 1) != 0)
        throw new InvalidDataException($"The lengths leave a code of {length - 1} bits half assigned, which is not a complete code.");

      first[length - 1] = taken >> 1;
    }

    var codes = new uint[_SYMBOL_COUNT];
    for (var i = 0; i < _SYMBOL_COUNT; ++i)
      codes[i] = first[lengths[i]]++;

    return codes;
  }
}
