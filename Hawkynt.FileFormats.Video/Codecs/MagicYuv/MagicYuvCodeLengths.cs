using System;

namespace FileFormat.Codecs.MagicYuv;

/// <summary>Builds MagicYUV's length-limited canonical Huffman codes.</summary>
/// <remarks>
/// Adapted from the package-merge construction in FFmpeg's <c>libavcodec/magicyuvenc.c</c>,
/// copyright (c) 2017 Paul B Mahol, LGPL-2.1-or-later; this adaptation is distributed with
/// PNGCrushCS under LGPL-3.0-or-later. Unlike FFmpeg's current encoder, this implementation also
/// handles the 1,024/4,096/16,384-symbol alphabets used by MagicYUV's 10/12/14-bit formats.
/// </remarks>
internal static class MagicYuvCodeLengths {
  /// <summary>Chooses one length per symbol, none longer than <paramref name="longest"/>.</summary>
  internal static byte[] Choose(ReadOnlySpan<long> counts, int longest) {
    var symbolCount = counts.Length;
    if (symbolCount is < 2 or > 1 << 14 || (symbolCount & (symbolCount - 1)) != 0)
      throw new ArgumentException("MagicYUV Huffman alphabets are powers of two from 256 through 16384 symbols.", nameof(counts));

    var minimumLength = 0;
    for (var values = symbolCount - 1; values > 0; values >>= 1)
      ++minimumLength;
    if (longest < minimumLength || longest > 32)
      throw new ArgumentOutOfRangeException(nameof(longest), longest,
        $"A code of {longest} bits cannot cover {symbolCount} symbols within the format's limit.");

    var weight = new long[symbolCount];
    var order = new int[symbolCount];
    for (var symbol = 0; symbol < symbolCount; ++symbol) {
      weight[symbol] = counts[symbol] + 1;
      order[symbol] = symbol;
    }

    Array.Sort(order, (a, b) => weight[a] != weight[b] ? weight[a].CompareTo(weight[b]) : a.CompareTo(b));

    var from = new _List(symbolCount, longest);
    var to = new _List(symbolCount, longest);
    var i = 0;
    for (var pass = 0; pass <= longest; ++pass) {
      to.Count = 0;
      to.Start[0] = 0;
      var j = 0;
      if (pass < longest)
        i = 0;

      while (i < symbolCount || j + 1 < from.Count) {
        ++to.Count;
        to.Start[to.Count] = to.Start[to.Count - 1];
        if (i < symbolCount && (j + 1 >= from.Count || weight[order[i]] < from.Weight[j] + from.Weight[j + 1])) {
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

    var lengths = new byte[symbolCount];
    var taken = Math.Min(symbolCount - 1, from.Count);
    for (var k = 0; k < from.Start[taken]; ++k)
      ++lengths[from.Items[k]];

    return lengths;
  }

  /// <summary>Returns the longest-first canonical code implied by each length.</summary>
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
      if (length != 0)
        codes[symbol] = first[length]++;
    }

    return codes;
  }

  private sealed class _List {
    internal int Count;
    internal readonly int[] Start;
    internal readonly long[] Weight;
    internal readonly int[] Items;

    internal _List(int symbols, int longest) {
      this.Start = new int[2 * symbols + 2];
      this.Weight = new long[2 * symbols + 1];
      this.Items = new int[checked(symbols * (longest + 1))];
    }
  }
}
