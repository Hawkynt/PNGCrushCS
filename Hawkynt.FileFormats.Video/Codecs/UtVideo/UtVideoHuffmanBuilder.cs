using System;
using System.IO;

namespace FileFormat.Codecs.UtVideo;

/// <summary>
/// Builds a Ut Video Huffman table from how often each symbol occurs: the code lengths a plane
/// states, and the codes those lengths imply.
/// </summary>
/// <remarks>
/// The length construction is adapted from FFmpeg's <c>libavcodec/huffman.c</c>
/// (<c>ff_huff_gen_len_table</c>), copyright (c) 2006 Konstantin Shishkov and (c) 2007 Loren
/// Merritt, LGPL-2.1-or-later; this adaptation is distributed with PNGCrushCS under
/// LGPL-3.0-or-later.
/// <para/>
/// <b>The lengths are a plain Huffman tree, flattened where it grows too deep.</b> The tree is built
/// on a heap of the symbol counts, and every count carries a small offset that is doubled and the
/// tree rebuilt whenever a code comes out longer than the format allows. Each doubling makes the
/// rare symbols a little less rare, which trades a fraction of a bit on the common ones for a
/// shallower tree. The reference stops at thirty-one bits; the format's author states twenty-four
/// as the longest code and <see cref="UtVideoHuffmanTable"/> refuses anything longer, so this stops
/// at twenty-four. A picture has to be pathological to reach it — a plane in which one difference
/// occurs millions of times and two dozen others once each — but a picture may be anything.
/// <para/>
/// <b>The codes are the ones <see cref="UtVideoHuffmanTable"/> reads back:</b> handed out from the
/// longest length down, and within a length from the highest symbol down, with the running number
/// halved at every step to a shorter length. Both halves of that rule are written out at the reader;
/// this is the same rule run the other way, and the round-trip tests are what hold the two together.
/// </remarks>
internal static class UtVideoHuffmanBuilder {

  /// <summary>The length a symbol that does not occur is given.</summary>
  internal const byte UNUSED = 0xFF;

  /// <summary>The longest code the format allows.</summary>
  private const int _MAX_LENGTH = 24;

  /// <summary>
  /// The code lengths for a plane, one byte a symbol: <see cref="UNUSED"/> where a symbol does not
  /// occur, nought where one symbol is the whole plane, and one to twenty-four otherwise.
  /// </summary>
  internal static byte[] Lengths(ReadOnlySpan<long> counts) {
    var lengths = new byte[UtVideoHuffmanTable.SYMBOL_COUNT];
    Array.Fill(lengths, UNUSED);

    var map = new int[UtVideoHuffmanTable.SYMBOL_COUNT];
    var size = 0;
    for (var symbol = 0; symbol < UtVideoHuffmanTable.SYMBOL_COUNT; ++symbol)
      if (counts[symbol] > 0)
        map[size++] = symbol;

    if (size == 0)
      throw new InvalidDataException("A plane with no samples in it has no table to build.");

    // One symbol and no other: the format says so with a length of nought and codes no bits.
    if (size == 1) {
      lengths[map[0]] = 0;
      return lengths;
    }

    var heap = new HeapEntry[size];
    var up = new int[2 * size];
    var depth = new int[2 * size];

    for (var offset = 1L; ; offset <<= 1) {
      for (var i = 0; i < size; ++i)
        heap[i] = new((counts[map[i]] << 14) + offset, i);

      for (var i = size / 2 - 1; i >= 0; --i)
        _Sift(heap, i, size);

      for (var next = size; next < 2 * size - 1; ++next) {
        // Merge the two smallest entries and put the merged node back in their place.
        var smallest = heap[0].Value;
        up[heap[0].Name] = next;
        heap[0].Value = long.MaxValue;
        _Sift(heap, 0, size);

        up[heap[0].Name] = next;
        heap[0].Name = next;
        heap[0].Value += smallest;
        _Sift(heap, 0, size);
      }

      depth[2 * size - 2] = 0;
      for (var i = 2 * size - 3; i >= size; --i)
        depth[i] = depth[up[i]] + 1;

      var longest = 0;
      for (var i = 0; i < size; ++i) {
        var length = depth[up[i]] + 1;
        lengths[map[i]] = (byte)Math.Min(length, byte.MaxValue - 1);
        if (length > longest)
          longest = length;
      }

      if (longest <= _MAX_LENGTH)
        return lengths;
    }
  }

  /// <summary>The code each symbol gets under the lengths, read as the decoder reads them.</summary>
  internal static uint[] Codes(ReadOnlySpan<byte> lengths) {
    var codes = new uint[UtVideoHuffmanTable.SYMBOL_COUNT];
    var next = 0u;
    for (var length = _MAX_LENGTH; length >= 1; --length) {
      for (var symbol = UtVideoHuffmanTable.SYMBOL_COUNT - 1; symbol >= 0; --symbol)
        if (lengths[symbol] == length)
          codes[symbol] = next++;

      if (length > 1)
        next >>= 1;
    }

    return codes;
  }

  private static void _Sift(HeapEntry[] heap, int root, int size) {
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

  private struct HeapEntry(long value, int name) {
    public long Value = value;
    public int Name = name;
  }
}
