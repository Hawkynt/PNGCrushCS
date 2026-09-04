using System;

namespace FileFormat.Codecs.MagicYuv;

/// <summary>
/// Chooses the code lengths a MagicYUV table carries: one for each of the 256 symbols, none longer
/// than the frame allows, and together describing a complete code.
/// </summary>
/// <remarks>
/// Adapted from the package-merge construction in FFmpeg's <c>libavcodec/magicyuvenc.c</c>,
/// copyright (c) 2017 Paul B Mahol, LGPL-2.1-or-later; this adaptation is distributed with
/// PNGCrushCS under LGPL-3.0-or-later.
/// <para/>
/// A plain Huffman construction cannot promise a longest length, and the frame states one in its
/// header, so the lengths are chosen by package-merge instead: the symbols are listed by weight,
/// then for each of the allowed lengths the list is merged with the pairs of the list before it, and
/// a symbol's length is how many of the cheapest packages it ends up inside. That is optimal among
/// codes of the stated limit and always complete.
/// <para/>
/// <b>Every symbol is given a code, whether or not it occurs.</b> Each weight is its count plus one,
/// which is what real frames show — none of their tables has a length of nought in it — and which
/// keeps a slice of a single value from asking for a code of no bits at all. The cost is a few bits
/// of table entropy a frame; the benefit is that the table is complete by construction and a
/// reader never has to guess what an absent symbol would have been.
/// </remarks>
internal static class MagicYuvCodeLengths {

  /// <summary>How many symbols a table covers, one for every value a byte can take.</summary>
  private const int _SYMBOL_COUNT = 256;

  /// <summary>Chooses code lengths for the counts given, none longer than <paramref name="longest"/>.</summary>
  internal static byte[] Choose(ReadOnlySpan<long> counts, int longest) {
    if (counts.Length != _SYMBOL_COUNT)
      throw new ArgumentException($"A table needs {_SYMBOL_COUNT} counts, not {counts.Length}.", nameof(counts));

    if (longest is < 8 or > 32)
      throw new ArgumentOutOfRangeException(nameof(longest), longest, $"A code of {longest} bits cannot cover {_SYMBOL_COUNT} symbols within the format's limit.");

    // the symbols by weight, ties by value so that equal counts always come out the same way
    var weight = new long[_SYMBOL_COUNT];
    var order = new int[_SYMBOL_COUNT];
    for (var symbol = 0; symbol < _SYMBOL_COUNT; ++symbol) {
      weight[symbol] = counts[symbol] + 1;
      order[symbol] = symbol;
    }

    Array.Sort(order, (a, b) => weight[a] != weight[b] ? weight[a].CompareTo(weight[b]) : a.CompareTo(b));

    var from = new _List(longest);
    var to = new _List(longest);
    var i = 0;

    for (var pass = 0; pass <= longest; ++pass) {
      to.Count = 0;
      to.Start[0] = 0;
      var j = 0;

      // the last pass adds no symbols of its own: it only packages what the previous pass built
      if (pass < longest)
        i = 0;

      while (i < _SYMBOL_COUNT || j + 1 < from.Count) {
        ++to.Count;
        to.Start[to.Count] = to.Start[to.Count - 1];

        if (i < _SYMBOL_COUNT && (j + 1 >= from.Count || weight[order[i]] < from.Weight[j] + from.Weight[j + 1])) {
          to.Items[to.Start[to.Count]++] = order[i];
          to.Weight[to.Count - 1] = weight[order[i]];
          ++i;
        } else {
          for (var k = from.Start[j]; k < from.Start[j + 2]; ++k)
            to.Items[to.Start[to.Count]++] = from.Items[k];

          to.Weight[to.Count - 1] = from.Weight[j] + from.Weight[j + 1];
          j += 2;
        }
      }

      (from, to) = (to, from);
    }

    // the cheapest n-1 packages hold every leaf that ends up in the code, one leaf a length
    var lengths = new byte[_SYMBOL_COUNT];
    var taken = Math.Min(_SYMBOL_COUNT - 1, from.Count);
    for (var k = 0; k < from.Start[taken]; ++k)
      ++lengths[from.Items[k]];

    return lengths;
  }

  /// <summary>
  /// The codes the lengths imply, under the format's own assignment: the longest length is handed
  /// out first from nought, the symbols of one length ascending, and each shorter length carries on
  /// from half the running total.
  /// </summary>
  internal static uint[] Codes(ReadOnlySpan<byte> lengths) {
    var count = new int[33];
    foreach (var length in lengths)
      ++count[length];

    var first = new uint[33];
    var next = 0u;
    for (var length = 32; length >= 1; --length) {
      first[length] = next;
      next += (uint)count[length];
      if (length > 1)
        next >>= 1;
    }

    var codes = new uint[lengths.Length];
    for (var symbol = 0; symbol < lengths.Length; ++symbol) {
      var length = lengths[symbol];
      if (length == 0)
        continue;

      codes[symbol] = first[length]++;
    }

    return codes;
  }

  /// <summary>One level of the package-merge lists: items, each a run of the leaves it packages.</summary>
  private sealed class _List {

    internal int Count;
    internal readonly int[] Start;
    internal readonly long[] Weight;
    internal readonly int[] Items;

    internal _List(int longest) {
      // a level holds at most every symbol plus half the level before, and a symbol's leaf from
      // any one level sits in at most one item, so the leaves are bounded by symbols times levels
      this.Start = new int[2 * _SYMBOL_COUNT + 2];
      this.Weight = new long[2 * _SYMBOL_COUNT + 1];
      this.Items = new int[_SYMBOL_COUNT * (longest + 1)];
    }
  }
}
